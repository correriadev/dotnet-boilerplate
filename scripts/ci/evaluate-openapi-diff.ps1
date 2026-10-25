<#
.SYNOPSIS
    Quality Gate de Retrocompatibilidade de Contrato OpenAPI.
.DESCRIPTION
    Compara a especificacao OpenAPI base com a candidata,
    detectando remocoes de rotas, metodos HTTP ou codigos de resposta.
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$BaseSpecPath = "docs/specs/api/openapi.v1.json",

    [Parameter(Mandatory = $false)]
    [string]$CandidateSpecPath = "docs/specs/api/openapi.v1.json",

    [Parameter(Mandatory = $false)]
    [string]$OutputFile = "openapi-diff-summary.md",

    [Parameter(Mandatory = $false)]
    [switch]$FailClosed = $true
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================================="
Write-Host " OpenAPI Contract Breaking Changes Evaluator"
Write-Host "=========================================================="
Write-Host "Base Spec: $BaseSpecPath"
Write-Host "Candidate Spec: $CandidateSpecPath"
Write-Host "Fail Closed: $FailClosed"
Write-Host "=========================================================="

if (-not (Test-Path $BaseSpecPath)) {
    Write-Error "FAIL-CLOSED: Arquivo base OpenAPI '$BaseSpecPath' nao encontrado!"
    exit 1
}

if (-not (Test-Path $CandidateSpecPath)) {
    Write-Error "FAIL-CLOSED: Arquivo candidato OpenAPI '$CandidateSpecPath' nao encontrado!"
    exit 1
}

$baseJson = Get-Content $BaseSpecPath -Raw | ConvertFrom-Json
$candJson = Get-Content $CandidateSpecPath -Raw | ConvertFrom-Json

$breakingChanges = [System.Collections.Generic.List[string]]::new()

# 1. Verificar remocao de rotas
foreach ($pathProperty in $baseJson.paths.PSObject.Properties) {
    $pathName = $pathProperty.Name
    $candPathMatch = $candJson.paths.PSObject.Properties[$pathName]

    if ($null -eq $candPathMatch) {
        $breakingChanges.Add("Rota removida: " + $pathName)
        continue
    }

    $basePathObj = $pathProperty.Value
    $candPathObj = $candPathMatch.Value

    # 2. Verificar remocao de metodos HTTP
    foreach ($methodProperty in $basePathObj.PSObject.Properties) {
        $methodName = $methodProperty.Name
        $candMethodMatch = $candPathObj.PSObject.Properties[$methodName]

        if ($null -eq $candMethodMatch) {
            $breakingChanges.Add("Metodo HTTP removido na rota " + $pathName + " - " + $methodName.ToUpper())
            continue
        }

        # 3. Verificar remocao de respostas HTTP esperadas
        $baseResponses = $methodProperty.Value.responses
        $candResponses = $candMethodMatch.Value.responses

        foreach ($statusProp in $baseResponses.PSObject.Properties) {
            $statusCode = $statusProp.Name
            $candStatusMatch = $candResponses.PSObject.Properties[$statusCode]
            if ($null -eq $candStatusMatch) {
                $breakingChanges.Add("Codigo de status " + $statusCode + " removido do endpoint " + $methodName.ToUpper() + " " + $pathName)
            }
        }
    }
}

$hasBreaking = $breakingChanges.Count -gt 0
$statusVerdict = if ($hasBreaking) { "REPROVADO" } else { "APROVADO" }
$failOrPass = if ($hasBreaking) { "FAIL" } else { "PASS" }
$compatDesc = if ($hasBreaking) { "Quebra de Contrato" } else { "Retrocompativel" }

$lines = @(
    "# Relatorio de Verificacao de Retrocompatibilidade OpenAPI",
    "",
    "**Data/Hora:** " + [System.DateTimeOffset]::UtcNow.ToString('o'),
    "**Especificacao Base:** " + $BaseSpecPath,
    "**Especificacao Candidata:** " + $CandidateSpecPath,
    "",
    "## Veredicto do Quality Gate: **" + $statusVerdict + "**",
    "",
    "| Tipo de Analise | Resultado | Status |",
    "| :--- | :--- | :--- |",
    "| **Breaking Changes Detectadas** | " + $breakingChanges.Count + " violacoes | " + $failOrPass + " |",
    "| **Compatibilidade Semantica** | " + $compatDesc + " | " + $failOrPass + " |",
    ""
)

if ($hasBreaking) {
    $lines += "### Violacoes Criticas Detectadas:"
    foreach ($viol in $breakingChanges) {
        $lines += "- :x: " + $viol
    }
} else {
    $lines += "### Nenhuma quebra de contrato detectada."
    $lines += "Todas as rotas, metodos e codigos de status da API permanecem retrocompativeis."
}

$markdown = $lines -join "`n"
$markdown | Set-Content -Path $OutputFile -Encoding UTF8
Write-Host $markdown

if ($hasBreaking -and $FailClosed) {
    Write-Error "FAIL-CLOSED: Quebra de retrocompatibilidade detectada na especificacao OpenAPI!"
    exit 1
}

Write-Host "Contrato OpenAPI verificado com sucesso."
exit 0
