<#
.SYNOPSIS
    Quality Gate Automatizado de SLOs com Política Fail-Closed (PBI06 / Q01).
.DESCRIPTION
    Dispara carga sintética controlada contra o endpoint de Homologação, calcula Latência P95
    e Taxa de Erros com amostragem mínima de 1.000 requisições distribuídas em 5 minutos (300s).
    Se o endpoint ou telemetria falhar, aborta a esteira imediatamente (Fail-Closed).
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string]$TargetUrl,

    [Parameter(Mandatory = $false)]
    [double]$ThresholdP95Ms = 400.0,

    [Parameter(Mandatory = $false)]
    [double]$ThresholdErrorRate = 0.001, # 0.1% max errors

    [Parameter(Mandatory = $false)]
    [int]$MinRequests = 1000,

    [Parameter(Mandatory = $false)]
    [int]$Concurrency = 20,

    [Parameter(Mandatory = $false)]
    [int]$DurationSeconds = 300, # 5 minutos conforme especificado no PBB

    [Parameter(Mandatory = $false)]
    [switch]$FailClosed = $true,

    [Parameter(Mandatory = $false)]
    [string]$OutputFile = "quality-gate-report.json"
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================================="
Write-Host " Quality Gate Evaluator (SLO Verification - Fail Closed)"
Write-Host "=========================================================="
Write-Host "Target Endpoint: $TargetUrl"
Write-Host "SLO Threshold P95 Latency: < ${ThresholdP95Ms} ms"
Write-Host "SLO Threshold Error Rate: < $($ThresholdErrorRate * 100)%"
Write-Host "Minimum Sample Count: $MinRequests requests"
Write-Host "Test Duration: $DurationSeconds seconds"
Write-Host "Concurrency: $Concurrency threads"
Write-Host "Fail-Closed Mode: $FailClosed"
Write-Host "=========================================================="

$startTime = [System.DateTimeOffset]::UtcNow
$endTime = $startTime.AddSeconds($DurationSeconds)

$latencies = [System.Collections.Generic.List[double]]::new()
$statusCodes = [System.Collections.Generic.Dictionary[int, int]]::new()
$errorsCount = 0
$totalRequests = 0
$generatorFailed = $false
$generatorErrorMessage = ""

# 1. Sonda de pré-voo estrita (Fail-Closed Preflight Probe): Somente 200 OK é aceito
try {
    Write-Host "Executing strict pre-flight connectivity probe (HTTP 200 required)..."
    $handler = [System.Net.Http.SocketsHttpHandler]::new()
    $handler.AllowAutoRedirect = $false # Desativa redirects para capturar 3xx
    $handler.PooledConnectionLifetime = [System.TimeSpan]::FromMinutes(2)
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [System.TimeSpan]::FromSeconds(5)

    $probeSw = [System.Diagnostics.Stopwatch]::StartNew()
    $preflight = $client.GetAsync($TargetUrl).GetAwaiter().GetResult()
    $probeSw.Stop()

    $code = [int]$preflight.StatusCode
    $preBody = $preflight.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $preJson = $preBody | ConvertFrom-Json
    $reqDeps = @("azure_key_vault", "azure_sql", "azure_redis", "azure_service_bus")

    if ($code -ne 200 -or $preJson.status -ne "Healthy" -or -not $preJson.entries) {
        Write-Error "FAIL-CLOSED: Pre-flight check failed! Target returned HTTP $code, status '$($preJson.status)'."
        exit 1
    }
    foreach ($d in $reqDeps) {
        if (-not $preJson.entries.$d -or $preJson.entries.$d.status -ne "Healthy") {
            Write-Error "FAIL-CLOSED: Pre-flight check failed! Required dependency '$d' is not Healthy."
            exit 1
        }
    }
    Write-Host "[OK] Pre-flight probe succeeded: HTTP 200 OK and all 4 dependencies Healthy in $($probeSw.ElapsedMilliseconds)ms."
    $client.Dispose()
    $handler.Dispose()
}
catch {
    Write-Error "FAIL-CLOSED: Target endpoint is unreachable during pre-flight check: $_"
    exit 1
}

# 2. Geração de Carga Sintética Multithreaded via Runspace Pool
Write-Host "Starting synthetic workload generation for $DurationSeconds seconds..."

$runspacePool = [runspacefactory]::CreateRunspacePool(1, $Concurrency)
$runspacePool.Open()

$scriptBlock = {
    param($url, $deadlineIso)
    $deadline = [System.DateTimeOffset]::Parse($deadlineIso)
    $localLatencies = [System.Collections.Generic.List[double]]::new()
    $localCodes = [System.Collections.Generic.List[int]]::new()
    $requiredDeps = @("azure_key_vault", "azure_sql", "azure_redis", "azure_service_bus")

    $handler = [System.Net.Http.SocketsHttpHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [System.TimeSpan]::FromSeconds(10)

    try {
        while ([System.DateTimeOffset]::UtcNow -lt $deadline) {
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            try {
                $resp = $client.GetAsync($url).GetAwaiter().GetResult()
                $sw.Stop()
                $statusCode = [int]$resp.StatusCode
                if ($statusCode -eq 200) {
                    $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    try {
                        $json = $body | ConvertFrom-Json
                        if ($json.status -eq "Healthy" -and $json.entries) {
                            $depsOk = $true
                            # Validação das 4 dependências obrigatórias
                            foreach ($dep in $requiredDeps) {
                                if (-not $json.entries.$dep -or -not $json.entries.$dep.status -or $json.entries.$dep.status -ne "Healthy") {
                                    $depsOk = $false
                                    break
                                }
                            }
                            # Validação de qualquer outra dependência presente
                            if ($depsOk) {
                                foreach ($prop in $json.entries.PSObject.Properties) {
                                    if (-not $prop.Value.status -or $prop.Value.status -ne "Healthy") {
                                        $depsOk = $false
                                        break
                                    }
                                }
                            }
                            if (-not $depsOk) {
                                $statusCode = 503
                            }
                        } else {
                            $statusCode = 503
                        }
                    } catch {
                        $statusCode = 503
                    }
                }
                $resp.Dispose()
            }
            catch {
                $sw.Stop()
                $statusCode = 500
            }
            $localLatencies.Add($sw.Elapsed.TotalMilliseconds)
            $localCodes.Add($statusCode)
            [System.Threading.Thread]::Sleep(20) # Controle de taxa distribuída
        }
    }
    catch {
        $client.Dispose()
        $handler.Dispose()
        throw $_
    }

    $client.Dispose()
    $handler.Dispose()

    return [PSCustomObject]@{
        Latencies = $localLatencies
        Codes     = $localCodes
    }
}

$tasks = [System.Collections.Generic.List[PSObject]]::new()

for ($i = 0; $i -lt $Concurrency; $i++) {
    $ps = [powershell]::Create()
    $ps.RunspacePool = $runspacePool
    [void]$ps.AddScript($scriptBlock)
    [void]$ps.AddArgument($TargetUrl)
    [void]$ps.AddArgument($endTime.ToString("o"))
    $asyncResult = $ps.BeginInvoke()
    $tasks.Add([PSCustomObject]@{
        PowerShell  = $ps
        AsyncResult = $asyncResult
    })
}

# 3. Coleta de Resultados das Tarefas Concorrentes com Fail-Closed
foreach ($task in $tasks) {
    try {
        $result = $task.PowerShell.EndInvoke($task.AsyncResult)
        if ($result -and $result.Count -gt 0) {
            foreach ($item in $result) {
                if ($item.Latencies) {
                    $latencies.AddRange($item.Latencies)
                }
                if ($item.Codes) {
                    foreach ($c in $item.Codes) {
                        $totalRequests++
                        if (-not $statusCodes.ContainsKey($c)) {
                            $statusCodes[$c] = 0
                        }
                        $statusCodes[$c]++
                        # Contrato estrito: apenas HTTP 200 OK é aceito como sucesso. Qualquer código != 200 é erro.
                        if ($c -ne 200) {
                            $errorsCount++
                        }
                    }
                }
            }
        }
    }
    catch {
        # Falha de instrumentação invalida o ensaio inteiro em conformidade com Fail-Closed
        $generatorFailed = $true
        $generatorErrorMessage = "Runspace worker failed during load execution: $_"
        Write-Warning "CRITICAL: $generatorErrorMessage"
    }
    finally {
        $task.PowerShell.Dispose()
    }
}

$runspacePool.Close()
$runspacePool.Dispose()

if ($generatorFailed) {
    Write-Error "FAIL-CLOSED: Synthetic load generation failed due to infrastructure error: $generatorErrorMessage"
    exit 1
}

Write-Host "Synthetic workload finished. Total Requests Sampled: $totalRequests"

# 4. Cálculo Estatístico de Latência e Erros
if ($totalRequests -eq 0) {
    Write-Error "FAIL-CLOSED: Zero requests recorded. Quality Gate aborted."
    exit 1
}

$latenciesArray = $latencies.ToArray()
[System.Array]::Sort($latenciesArray)

$p50Index = [Math]::Floor($latenciesArray.Length * 0.50)
$p90Index = [Math]::Floor($latenciesArray.Length * 0.90)
$p95Index = [Math]::Floor($latenciesArray.Length * 0.95)
$p99Index = [Math]::Floor($latenciesArray.Length * 0.99)

$p50 = [Math]::Round($latenciesArray[[Math]::Min($p50Index, $latenciesArray.Length - 1)], 2)
$p90 = [Math]::Round($latenciesArray[[Math]::Min($p90Index, $latenciesArray.Length - 1)], 2)
$p95 = [Math]::Round($latenciesArray[[Math]::Min($p95Index, $latenciesArray.Length - 1)], 2)
$p99 = [Math]::Round($latenciesArray[[Math]::Min($p99Index, $latenciesArray.Length - 1)], 2)

$errorRate = ($errorsCount / $totalRequests)
$errorPercentage = [Math]::Round(($errorRate * 100), 4)

Write-Host "--- Quality Gate Metrics ---"
Write-Host "Sample Size : $totalRequests (Min Required: $MinRequests)"
Write-Host "Latency P50 : $p50 ms"
Write-Host "Latency P90 : $p90 ms"
Write-Host "Latency P95 : $p95 ms (Threshold: < $ThresholdP95Ms ms)"
Write-Host "Latency P99 : $p99 ms"
Write-Host "Error Count : $errorsCount"
Write-Host "Error Rate  : $errorPercentage % (Threshold: < $($ThresholdErrorRate * 100) %)"

# 5. Validação das Regras de SLO com Critério Estrito (<)
$violations = @()

if ($totalRequests -lt $MinRequests) {
    $violations += "Insufficient sample size: $totalRequests requests collected, minimum required is $MinRequests."
}

if ($p95 -ge $ThresholdP95Ms) {
    $violations += "Latency P95 violation: ${p95}ms breaches threshold of ${ThresholdP95Ms}ms."
}

if ($errorRate -ge $ThresholdErrorRate) {
    $violations += "Error rate violation: ${errorPercentage}% breaches threshold of $($ThresholdErrorRate * 100)%."
}

$statusString = if ($violations.Count -eq 0) { "PASSED" } else { "FAILED" }

$report = [PSCustomObject]@{
    TargetUrl           = $TargetUrl
    EvaluationTimestamp = [System.DateTimeOffset]::UtcNow.ToString("o")
    Verdict             = $statusString
    SampleSize          = $totalRequests
    MinRequiredSample   = $MinRequests
    LatencyP50Ms        = $p50
    LatencyP90Ms        = $p90
    LatencyP95Ms        = $p95
    LatencyP99Ms        = $p99
    ThresholdP95Ms      = $ThresholdP95Ms
    ErrorCount          = $errorsCount
    ErrorRatePercentage = $errorPercentage
    ThresholdErrorRate  = ($ThresholdErrorRate * 100)
    Violations          = $violations
}

$report | ConvertTo-Json -Depth 5 | Set-Content -Path $OutputFile -Encoding UTF8
Write-Host "Audit report exported to $OutputFile."

# Exportação do GitHub Step Summary
if ($env:GITHUB_STEP_SUMMARY) {
    $markdown = @"
## 🎯 Quality Gate SLO Evaluation ($statusString)

| Metric | Target | Observed | Result |
| :--- | :--- | :--- | :--- |
| **Sample Size** | >= $MinRequests reqs | $totalRequests reqs | $(if ($totalRequests -ge $MinRequests) { '✅ PASS' } else { '❌ FAIL' }) |
| **Latency P95** | < ${ThresholdP95Ms} ms | ${p95} ms | $(if ($p95 -lt $ThresholdP95Ms) { '✅ PASS' } else { '❌ FAIL' }) |
| **Error Rate** | < $($ThresholdErrorRate * 100)% | ${errorPercentage}% | $(if ($errorRate -lt $ThresholdErrorRate) { '✅ PASS' } else { '❌ FAIL' }) |
| **Latency P99** | Reference | ${p99} ms | ℹ️ INFO |

**Policy Enforcement:** Fail-Closed (Strict)
"@
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $markdown
}

if ($violations.Count -gt 0) {
    Write-Host "=========================================================="
    Write-Error "QUALITY GATE REJECTED with $($violations.Count) violation(s):`n$($violations -join "`n")"
    exit 1
}

Write-Host "=========================================================="
Write-Host "SUCCESS: Quality gate passed all SLO criteria."
Write-Host "=========================================================="
exit 0
