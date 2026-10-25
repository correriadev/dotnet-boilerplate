<#
.SYNOPSIS
    Quality Gate Automatizado de Cobertura de Código (Coverlet / Cobertura XML).
.DESCRIPTION
    Analisa os arquivos coverage.cobertura.xml gerados na pasta TestResults, calcula
    taxas de cobertura de linha e de branch globais e por módulo, gera sumário Markdown
    para publicação no GitHub Actions Step Summary e aplica política Fail-Closed.
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$TestResultsPath = "TestResults",

    [Parameter(Mandatory = $false)]
    [double]$MinLineRate = 0.50, # 50% mínimo

    [Parameter(Mandatory = $false)]
    [double]$MinBranchRate = 0.50, # 50% mínimo

    [Parameter(Mandatory = $false)]
    [string]$OutputFile = "coverage-summary.md",

    [Parameter(Mandatory = $false)]
    [switch]$FailClosed = $true
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================================="
Write-Host " Code Coverage Quality Gate Evaluator"
Write-Host "=========================================================="
Write-Host "Results Path: $TestResultsPath"
Write-Host "Min Line Coverage: $($MinLineRate * 100)%"
Write-Host "Min Branch Coverage: $($MinBranchRate * 100)%"
Write-Host "Fail Closed: $FailClosed"
Write-Host "=========================================================="

if (-not (Test-Path $TestResultsPath)) {
    Write-Error "FAIL-CLOSED: Diretório de resultados '$TestResultsPath' não encontrado!"
    exit 1
}

$coverageFiles = Get-ChildItem -Path $TestResultsPath -Recurse -Filter "coverage.cobertura.xml"
if ($coverageFiles.Count -eq 0) {
    Write-Error "FAIL-CLOSED: Nenhum arquivo 'coverage.cobertura.xml' encontrado em '$TestResultsPath'!"
    exit 1
}

$totalLinesCovered = 0
$totalLinesValid = 0
$totalBranchesCovered = 0
$totalBranchesValid = 0
$moduleRows = @()

foreach ($file in $coverageFiles) {
    [xml]$xml = Get-Content $file.FullName
    $root = $xml.coverage

    $linesCovered = [int]$root.'lines-covered'
    $linesValid = [int]$root.'lines-valid'
    $branchesCovered = [int]$root.'branches-covered'
    $branchesValid = [int]$root.'branches-valid'

    $totalLinesCovered += $linesCovered
    $totalLinesValid += $linesValid
    $totalBranchesCovered += $branchesCovered
    $totalBranchesValid += $branchesValid

    # Módulos / Pacotes
    foreach ($pkg in $root.packages.package) {
        $pkgName = [string]$pkg.name
        $pkgLineRate = [double]$pkg.'line-rate' * 100
        $pkgBranchRate = [double]$pkg.'branch-rate' * 100
        $moduleRows += "| ``$pkgName`` | $($pkgLineRate.ToString('F2'))% | $($pkgBranchRate.ToString('F2'))% |"
    }
}

$globalLineRate = if ($totalLinesValid -gt 0) { [double]$totalLinesCovered / $totalLinesValid } else { 0.0 }
$globalBranchRate = if ($totalBranchesValid -gt 0) { [double]$totalBranchesCovered / $totalBranchesValid } else { 0.0 }

$linePass = $globalLineRate -ge $MinLineRate
$branchPass = $globalBranchRate -ge $MinBranchRate
$gatePass = $linePass -and $branchPass

$markdown = @"
# Relatório de Cobertura de Testes Unitários (.NET 8)

**Data/Hora:** $([System.DateTimeOffset]::UtcNow.ToString("o"))

## Métricas Consolidadas

| Métrica | Obtido | Mínimo Exigido | Status |
| :--- | :--- | :--- | :--- |
| **Linhas Cobertas** | $(($globalLineRate * 100).ToString('F2'))% ($totalLinesCovered/$totalLinesValid) | $(($MinLineRate * 100).ToString('F2'))% | $(if ($linePass) { 'PASS' } else { 'FAIL' }) |
| **Branches Cobertos** | $(($globalBranchRate * 100).ToString('F2'))% ($totalBranchesCovered/$totalBranchesValid) | $(($MinBranchRate * 100).ToString('F2'))% | $(if ($branchPass) { 'PASS' } else { 'FAIL' }) |

## Detalhamento por Módulo

| Módulo / Pacote | Cobertura de Linha | Cobertura de Branch |
| :--- | :--- | :--- |
$($moduleRows -join "`n")

---
**Veredicto do Quality Gate:** $(if ($gatePass) { 'APROVADO' } else { 'REPROVADO' })
"@

$markdown | Set-Content -Path $OutputFile -Encoding UTF8
Write-Host $markdown

if (-not $gatePass -and $FailClosed) {
    Write-Error "FAIL-CLOSED: Cobertura de testes abaixo da linha de corte (Linha: $(($globalLineRate*100).ToString('F2'))%, Branch: $(($globalBranchRate*100).ToString('F2'))%)!"
    exit 1
}

Write-Host "Cobertura de código aprovada com sucesso."
exit 0
