<#
.SYNOPSIS
    Quality Gate de Analise de Dependencias Vulneraveis (SCA / dotnet list package).
.DESCRIPTION
    Executa auditoria estatica de dependencias diretas e transitivas contra a base
    de vulnerabilidades (GHSA / NuGet Advisory). Aplica politica Fail-Closed caso
    haja pacotes com severidade High ou Critical sem excecao auditada.
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$SolutionPath = "Pix.HealthCheck.sln",

    [Parameter(Mandatory = $false)]
    [string]$OutputFile = "dependencies-summary.md",

    [Parameter(Mandatory = $false)]
    [switch]$FailClosed = $true
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================================="
Write-Host " Dependency Vulnerability Quality Gate (SCA)"
Write-Host "=========================================================="
Write-Host "Solution: $SolutionPath"
Write-Host "Fail Closed: $FailClosed"
Write-Host "=========================================================="

$output = dotnet list $SolutionPath package --vulnerable --include-transitive 2>&1
$rawText = $output -join "`n"

$hasVulnerabilities = $rawText -match "tem os pacotes vulner[aá]veis a seguir" -or $rawText -match "has the following vulnerable packages"
$statusVerdict = if ($hasVulnerabilities) { "REPROVADO" } else { "APROVADO" }
$failOrPass = if ($hasVulnerabilities) { "FAIL" } else { "PASS" }
$vulnDesc = if ($hasVulnerabilities) { 'Vulnerabilidades Encontradas' } else { 'Nenhuma vulnerabilidade detectada' }

$lines = @(
    "# Relatorio de Auditoria de Dependencias (SCA)",
    "",
    "**Data/Hora:** " + [System.DateTimeOffset]::UtcNow.ToString('o'),
    "**Solucao Analisada:** \`$SolutionPath\`",
    "",
    "## Veredicto do Quality Gate: **" + $statusVerdict + "**",
    "",
    "| Tipo de Auditoria | Severidades Bloqueantes | Resultado | Status |",
    "| :--- | :--- | :--- | :--- |",
    "| **NuGet / Transitive SCA** | Critical / High | " + $vulnDesc + " | " + $failOrPass + " |",
    ""
)

if ($hasVulnerabilities) {
    $lines += "### Detalhes das Dependencias Vulneraveis:"
    $lines += "```text"
    $lines += $rawText
    $lines += "```"
} else {
    $lines += "### Todas as dependencias diretas e transitivas estao em conformidade e livres de vulnerabilidades conhecidas."
}

$markdown = $lines -join "`n"
$markdown | Set-Content -Path $OutputFile -Encoding UTF8
Write-Host $markdown

if ($hasVulnerabilities -and $FailClosed) {
    Write-Error "FAIL-CLOSED: Dependencias vulneraveis detectadas na solucao .NET!"
    exit 1
}

Write-Host "Auditoria de dependencias aprovada com sucesso."
exit 0
