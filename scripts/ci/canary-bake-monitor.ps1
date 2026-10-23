<#
.SYNOPSIS
    Monitoramento da Bake Window de Canary em Produção (PBI08).
.DESCRIPTION
    Supervisiona o tráfego da revisão Canary (10%) durante 10 minutos, verificando amostragem
    mínima de 600 requisições direcionadas especificamente ao endpoint direto da candidata,
    P95 < 400ms e Erros < 0.1%. Em caso de anomalia, aborta imediatamente com Exit 1
    para disparar o rollback determinístico.
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$ContainerAppName,

    [Parameter(Mandatory = $true)]
    [string]$CanaryRevision,

    [Parameter(Mandatory = $true)]
    [string]$StableRevision,

    [Parameter(Mandatory = $false)]
    [int]$BakeWindowMinutes = 10,

    [Parameter(Mandatory = $false)]
    [int]$IntervalSeconds = 15,

    [Parameter(Mandatory = $false)]
    [int]$MinRequests = 600,

    [Parameter(Mandatory = $false)]
    [double]$ThresholdP95Ms = 400.0,

    [Parameter(Mandatory = $false)]
    [double]$ThresholdErrorRate = 0.001, # 0.1%

    [Parameter(Mandatory = $false)]
    [string]$TargetUrl = "",

    [Parameter(Mandatory = $false)]
    [switch]$FailClosed = $true,

    [Parameter(Mandatory = $false)]
    [string]$OutputFile = "canary-bake-report.json"
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================================="
Write-Host " Canary Bake Window Supervisor (10-minute Observability)"
Write-Host "=========================================================="
Write-Host "App Name: $ContainerAppName (RG: $ResourceGroupName)"
Write-Host "Canary Revision (10%): $CanaryRevision"
Write-Host "Stable Revision (90%): $StableRevision"
Write-Host "Bake Window Duration: $BakeWindowMinutes minutes"
Write-Host "Observation Interval: $IntervalSeconds seconds"
Write-Host "SLO P95 Threshold: < ${ThresholdP95Ms} ms"
Write-Host "SLO Error Rate Threshold: < $($ThresholdErrorRate * 100)%"
Write-Host "Min Sample Required: $MinRequests requests (specific to Canary revision)"
Write-Host "Fail-Closed Mode: $FailClosed"
Write-Host "=========================================================="

$startTime = [System.DateTimeOffset]::UtcNow
$endTime = $startTime.AddMinutes($BakeWindowMinutes)
$iteration = 0

$canaryLatencies = [System.Collections.Generic.List[double]]::new()
$canaryStatusList = [System.Collections.Generic.List[int]]::new()
$abortEarly = $false
$abortReason = ""

# 1. Validação prévia de que ambas as revisões estão ativas no ACA
try {
    Write-Host "Verifying revisions active status in Azure Container Apps..."
    $revisionsJson = az containerapp revision list --name $ContainerAppName --resource-group $ResourceGroupName -o json 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($revisionsJson)) {
        throw "Failed to list revisions from Azure Container Apps."
    }
    $revisions = $revisionsJson | ConvertFrom-Json

    $canaryRevObj = $revisions | Where-Object { $_.name -eq $CanaryRevision }
    $stableRevObj = $revisions | Where-Object { $_.name -eq $StableRevision }

    if (-not $canaryRevObj -or -not $canaryRevObj.properties.active) {
        Write-Error "FAIL-CLOSED: Canary revision '$CanaryRevision' is not active in ACA!"
        exit 1
    }
    if (-not $stableRevObj -or -not $stableRevObj.properties.active) {
        Write-Error "FAIL-CLOSED: Stable revision '$StableRevision' is not active in ACA!"
        exit 1
    }
    Write-Host "[OK] Both Canary ($CanaryRevision) and Stable ($StableRevision) revisions confirmed active."
}
catch {
    Write-Error "FAIL-CLOSED: Failed to query Azure Container App revisions: $_"
    exit 1
}

# 2. Resolução do FQDN direto e exclusivo da revisão Canary (Obrigatório - Sem Fallback)
if ([string]::IsNullOrEmpty($TargetUrl)) {
    try {
        Write-Host "Querying direct dedicated FQDN for candidate revision '$CanaryRevision'..."
        $canaryFqdn = az containerapp revision show --name $ContainerAppName --resource-group $ResourceGroupName --revision $CanaryRevision --query "properties.fqdn" -o tsv 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($canaryFqdn)) {
            $TargetUrl = "https://$canaryFqdn/health"
            Write-Host "[OK] Dedicated Canary Revision Endpoint resolved: $TargetUrl"
        } else {
            Write-Error "FAIL-CLOSED: Dedicated FQDN for canary revision '$CanaryRevision' could not be resolved! Shared fallback rejected."
            exit 1
        }
    }
    catch {
        Write-Error "FAIL-CLOSED: Error resolving dedicated revision FQDN: $_"
        exit 1
    }
}

if ([string]::IsNullOrEmpty($TargetUrl)) {
    Write-Error "FAIL-CLOSED: Target health probe URL could not be resolved."
    exit 1
}

Write-Host "Target Health Endpoint for Bake: $TargetUrl"

$handler = [System.Net.Http.SocketsHttpHandler]::new()
$handler.AllowAutoRedirect = $false # Desativa redirects automáticos para que 3xx sejam detectados e contabilizados como erro
$handler.PooledConnectionLifetime = [System.TimeSpan]::FromMinutes(2)
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [System.TimeSpan]::FromSeconds(5)

$requiredDeps = @("azure_key_vault", "azure_sql", "azure_redis", "azure_service_bus")

# 3. Loop de observação ativa durante a janela de bake (20 requisições assíncronas concorrentes a cada 10s = ~1200 requisições)
while ([System.DateTimeOffset]::UtcNow -lt $endTime) {
    $iteration++
    $currentElapsed = ([System.DateTimeOffset]::UtcNow - $startTime).TotalMinutes
    Write-Host ("[{0:HH:mm:ss}] Iteration {1} (Elapsed: {2:N1}/{3} min | Sampled: {4})..." -f [System.DateTime]::UtcNow, $iteration, $currentElapsed, $BakeWindowMinutes, $canaryStatusList.Count)

    # 20 requisições paralelas com cronometragem independente e desacoplada do consumo
    $sampleTasks = [System.Collections.Generic.List[System.Threading.Tasks.Task[psobject]]]::new()
    for ($p = 0; $p -lt 20; $p++) {
        $task = [System.Threading.Tasks.Task]::Run([System.Func[psobject]]{
            $req = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $TargetUrl)
            $req.Headers.Add("X-MS-Routing-Name", $CanaryRevision)
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $resp = $null
            $err = $null
            try {
                $resp = $client.SendAsync($req).GetAwaiter().GetResult()
            } catch {
                $err = $_
            }
            $sw.Stop() # Cronômetro para imediatamente no instante em que o socket termina a requisição
            return [PSCustomObject]@{
                Response  = $resp
                ElapsedMs = $sw.Elapsed.TotalMilliseconds
                Request   = $req
                Error     = $err
            }
        })
        $sampleTasks.Add($task)
    }

    # Aguarda todas as requisições paralelas da iteração terminarem
    [System.Threading.Tasks.Task]::WaitAll($sampleTasks.ToArray())

    # Validação estrita de contrato HTTP 200 + payload Healthy com 4 dependências obrigatórias
    foreach ($st in $sampleTasks) {
        $item = $st.Result
        $code = 500
        $elapsedMs = $item.ElapsedMs
        try {
            if ($item.Response) {
                $resp = $item.Response
                $code = [int]$resp.StatusCode
                $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

                # Validação profunda: status HTTP estritamente 200 OK E parsing estrito de JSON Healthy
                $isHealthy = $false
                if ($code -eq 200 -and -not [string]::IsNullOrWhiteSpace($body)) {
                    try {
                        $jsonPayload = $body | ConvertFrom-Json
                        if ($jsonPayload.status -eq "Healthy" -and $jsonPayload.entries) {
                            $depsOk = $true
                            # 1. Valida presença e status das 4 dependências obrigatórias da arquitetura
                            foreach ($dep in $requiredDeps) {
                                if (-not $jsonPayload.entries.$dep -or -not $jsonPayload.entries.$dep.status -or $jsonPayload.entries.$dep.status -ne "Healthy") {
                                    $depsOk = $false
                                    break
                                }
                            }
                            # 2. Valida que nenhuma outra dependência esteja em estado diferente de Healthy
                            if ($depsOk) {
                                foreach ($prop in $jsonPayload.entries.PSObject.Properties) {
                                    if (-not $prop.Value.status -or $prop.Value.status -ne "Healthy") {
                                        $depsOk = $false
                                        break
                                    }
                                }
                            }
                            if ($depsOk) { $isHealthy = $true }
                        }
                    } catch {
                        $isHealthy = $false
                    }
                }

                if (-not $isHealthy -or $code -ne 200) {
                    # Qualquer retorno diferente de 200 ou sem saúde confirmada é tratado como erro (503/falha)
                    $code = if ($code -eq 200) { 503 } else { $code }
                }
                $resp.Dispose()
            }
            if ($item.Request) { $item.Request.Dispose() }
        }
        catch {
            $code = 500
            try { if ($item.Request) { $item.Request.Dispose() } } catch {}
        }
        $canaryLatencies.Add($elapsedMs)
        $canaryStatusList.Add($code)
    }

    # Avaliação de anomalia deslizante durante a janela (Early Abort) - Somente 200 OK é sucesso
    $currentCount = $canaryStatusList.Count
    $currentErrors = ($canaryStatusList | Where-Object { $_ -ne 200 }).Count
    $currentErrorRate = if ($currentCount -gt 0) { $currentErrors / $currentCount } else { 0.0 }

    if ($currentCount -gt 100 -and $currentErrorRate -ge $ThresholdErrorRate) {
        $abortEarly = $true
        $abortReason = "Immediate abort triggered! Error rate ($([Math]::Round($currentErrorRate * 100, 3))%) breached threshold during bake window."
        Write-Warning "CRITICAL: $abortReason"
        break
    }

    Start-Sleep -Seconds $IntervalSeconds
}

$client.Dispose()
$handler.Dispose()

# 4. Avaliação Final Consolidada ao Término da Janela (Somente 200 OK é Sucesso)
$totalSampled = $canaryStatusList.Count
$totalErrors = ($canaryStatusList | Where-Object { $_ -ne 200 }).Count
$finalErrorRate = if ($totalSampled -gt 0) { $totalErrors / $totalSampled } else { 1.0 }

$sortedLatencies = $canaryLatencies.ToArray()
[System.Array]::Sort($sortedLatencies)
$p95Index = [Math]::Floor($sortedLatencies.Length * 0.95)
$finalP95 = if ($sortedLatencies.Length -gt 0) {
    [Math]::Round($sortedLatencies[[Math]::Min($p95Index, $sortedLatencies.Length - 1)], 2)
} else {
    9999.0
}

Write-Host "--- Bake Window Finished: Final Results ---"
Write-Host "Total Samples Recorded for Canary: $totalSampled (Minimum Required: $MinRequests)"
Write-Host "Latency P95: $finalP95 ms (Threshold: < ${ThresholdP95Ms} ms)"
Write-Host "Error Count: $totalErrors"
Write-Host "Error Rate: $([Math]::Round($finalErrorRate * 100, 4)) % (Threshold: < $($ThresholdErrorRate * 100) %)"

$violations = @()

if ($abortEarly) {
    $violations += $abortReason
}

if ($totalSampled -lt $MinRequests) {
    $violations += "FAIL-CLOSED: Insufficient representative traffic. Sample size ($totalSampled) is below minimum of $MinRequests requests."
}

if ($finalP95 -ge $ThresholdP95Ms) {
    $violations += "Latency P95 violation: ${finalP95}ms breaches threshold of ${ThresholdP95Ms}ms."
}

if ($finalErrorRate -ge $ThresholdErrorRate) {
    $violations += "Error rate violation: $([Math]::Round($finalErrorRate * 100, 4))% breaches threshold of $($ThresholdErrorRate * 100)%."
}

$bakeVerdict = if ($violations.Count -eq 0) { "PASSED" } else { "FAILED" }

$summaryReport = [PSCustomObject]@{
    CanaryRevision    = $CanaryRevision
    StableRevision    = $StableRevision
    TargetUrlUsed     = $TargetUrl
    BakeWindowMinutes = $BakeWindowMinutes
    Verdict           = $bakeVerdict
    TotalSamples      = $totalSampled
    MinRequired       = $MinRequests
    LatencyP95Ms      = $finalP95
    ThresholdP95Ms    = $ThresholdP95Ms
    ErrorCount        = $totalErrors
    ErrorRatePercent  = [Math]::Round($finalErrorRate * 100, 4)
    ThresholdError    = ($ThresholdErrorRate * 100)
    Violations        = $violations
}

$summaryReport | ConvertTo-Json -Depth 5 | Set-Content -Path $OutputFile -Encoding UTF8

if ($env:GITHUB_STEP_SUMMARY) {
    $markdown = @"
## 🐣 Canary Bake Window Observability Report ($bakeVerdict)

- **Canary Revision:** \`$CanaryRevision\` (10%)
- **Stable Revision:** \`$StableRevision\` (90%)
- **Dedicated Target URL:** \`$TargetUrl\`
- **Window Duration:** $BakeWindowMinutes minutes

| Metric | Target | Observed | Status |
| :--- | :--- | :--- | :--- |
| **Amostragem Mínima** | >= $MinRequests reqs | $totalSampled reqs | $(if ($totalSampled -ge $MinRequests) { '✅ PASS' } else { '❌ FAIL' }) |
| **Latência P95** | < ${ThresholdP95Ms} ms | ${finalP95} ms | $(if ($finalP95 -lt $ThresholdP95Ms) { '✅ PASS' } else { '❌ FAIL' }) |
| **Taxa de Erros** | < $($ThresholdErrorRate * 100)% | $([Math]::Round($finalErrorRate * 100, 4))% | $(if ($finalErrorRate -lt $ThresholdErrorRate) { '✅ PASS' } else { '❌ FAIL' }) |

$(if ($violations.Count -gt 0) { "**Violações Detectadas (Rollback Imediato Exigido):**`n- " + ($violations -join "`n- ") } else { "**Status:** Revisão estável e segura para comutação de 100% do tráfego." })
"@
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $markdown
}

if ($violations.Count -gt 0) {
    Write-Host "=========================================================="
    Write-Error "CANARY BAKE REJECTED: Rollback must be triggered immediately!`n$($violations -join "`n")"
    exit 1
}

Write-Host "=========================================================="
Write-Host "SUCCESS: Canary revision successfully passed 10-minute bake window."
Write-Host "=========================================================="
exit 0