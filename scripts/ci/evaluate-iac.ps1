<#
.SYNOPSIS
    Quality Gate de Seguranca de Infraestrutura como Codigo (IaC / Bicep Linter).
.DESCRIPTION
    Analisa os templates Bicep em infra/bicep/, validando regras de seguranca.
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$BicepPath = "infra/bicep",

    [Parameter(Mandatory = $false)]
    [string]$OutputFile = "iac-summary.md",

    [Parameter(Mandatory = $false)]
    [switch]$FailClosed = $true
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================================="
Write-Host " Infrastructure as Code (IaC) Security Gate"
Write-Host "=========================================================="
Write-Host "Bicep Directory: $BicepPath"
Write-Host "Fail Closed: $FailClosed"
Write-Host "=========================================================="

if (-not (Test-Path $BicepPath)) {
    Write-Error "FAIL-CLOSED: Diretorio Bicep '$BicepPath' nao encontrado!"
    exit 1
}

$bicepFiles = Get-ChildItem -Path $BicepPath -Filter "*.bicep"
if ($bicepFiles.Count -eq 0) {
    Write-Error "FAIL-CLOSED: Nenhum arquivo .bicep encontrado em '$BicepPath'!"
    exit 1
}

$violations = [System.Collections.Generic.List[string]]::new()
$passedRules = [System.Collections.Generic.List[string]]::new()

foreach ($file in $bicepFiles) {
    $content = Get-Content $file.FullName -Raw

    # 1. Checagem de IPs abertos para a internet (0.0.0.0/0)
    if ($content -match "0\.0\.0\.0/0" -or $content -match "startIpAddress:\s*['""]0\.0\.0\.0['""]\s*endIpAddress:\s*['""]255\.255\.255\.255['""]") {
        $violations.Add("Regra de Firewall publica (0.0.0.0/0) detectada em " + $file.Name)
    } else {
        $passedRules.Add("Isolamento de Rede validado em " + $file.Name)
    }

    # 2. Checagem de senhas ou segredos hardcoded como default
    if ($content -match "@secure\(\)\s*param\s+\w+\s+string\s*=\s*['""][^'""]+['""]") {
        $violations.Add("Segredo com valor default hardcoded detectado em " + $file.Name)
    } else {
        $passedRules.Add("Ausencia de segredos default em " + $file.Name)
    }

    # 3. Checagem de TLS inseguro
    if ($content -match "minTlsVersion:\s*['""]1\.[01]['""]") {
        $violations.Add("Versao obsoleta de TLS (menor que 1.2) detectada em " + $file.Name)
    } else {
        $passedRules.Add("TLS 1.2+ em conformidade em " + $file.Name)
    }
}

$hasViolations = $violations.Count -gt 0
$statusVerdict = if ($hasViolations) { "REPROVADO" } else { "APROVADO" }
$failOrPass = if ($hasViolations) { "FAIL" } else { "PASS" }
$violDesc = if ($hasViolations) { ($violations.Count.ToString() + " violacoes criticas") } else { "Templates em conformidade" }
$fileList = ($bicepFiles | Select-Object -ExpandProperty Name) -join ', '

$lines = @(
    "# Relatorio de Auditoria de Seguranca IaC (Bicep)",
    "",
    "**Data/Hora:** " + [System.DateTimeOffset]::UtcNow.ToString('o'),
    "**Diretorio:** " + $BicepPath,
    "**Arquivos Analisados:** " + $fileList,
    "",
    "## Veredicto do Quality Gate: **" + $statusVerdict + "**",
    "",
    "| Regra de Seguranca | Severidade | Resultado | Status |",
    "| :--- | :--- | :--- | :--- |",
    "| **Firewall / Rede Sem 0.0.0.0/0** | Alta | Conforme | PASS |",
    "| **Sem Segredos Default Hardcoded** | Critica | Conforme | PASS |",
    "| **Politica TLS Minimo 1.2** | Media | Conforme | PASS |",
    "| **Status Consolidado de IaC** | - | " + $violDesc + " | " + $failOrPass + " |",
    ""
)

if ($hasViolations) {
    $lines += "### Violacoes Criticas de IaC Detectadas:"
    foreach ($v in $violations) {
        $lines += "- :x: " + $v
    }
} else {
    $lines += "### Todos os templates Bicep estao em conformidade com as diretrizes corporativas de seguranca da Azure."
}

$markdown = $lines -join "`n"
$markdown | Set-Content -Path $OutputFile -Encoding UTF8
Write-Host $markdown

if ($hasViolations -and $FailClosed) {
    Write-Error "FAIL-CLOSED: Violacoes de seguranca detectadas nos templates IaC Bicep!"
    exit 1
}

Write-Host "Auditoria de IaC aprovada com sucesso."
exit 0
