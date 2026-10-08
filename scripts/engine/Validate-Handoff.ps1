<#
.SYNOPSIS
    Valida um arquivo handoff.json contra o contrato determinístico de transição de fase.

.DESCRIPTION
    Verifica campos obrigatórios, hashes de oráculo, resultados de testes e bloqueia
    transições inválidas (ex: saltos de estado não permitidos ou promover uma 'TRIPA_VERDE' diretamente para produção).

.PARAMETER HandoffPath
    Caminho do arquivo handoff.json a ser validado.

.EXAMPLE
    .\Validate-Handoff.ps1 -HandoffPath ".\docs\slices\slice-001\handoff.json"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$HandoffPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $HandoffPath)) {
    throw "Arquivo de handoff não encontrado: $HandoffPath"
}

$rawJson = Get-Content -Path $HandoffPath -Raw -Encoding UTF8
$handoff = $rawJson | ConvertFrom-Json

Write-Host ">>> Validando Handoff para Fatia: $($handoff.slice_id)" -ForegroundColor Cyan
Write-Host ">>> Transição: $($handoff.source_state) -> $($handoff.target_state)" -ForegroundColor Cyan
Write-Host ">>> Agente Responsável: $($handoff.agent_role)" -ForegroundColor Cyan

# 1. Checagem de campos obrigatórios do schema
$requiredFields = @("slice_id", "source_state", "target_state", "agent_role", "commit_hash", "oracle_contract_hash", "modified_files", "test_execution", "architecture_compliance", "open_questions")
foreach ($field in $requiredFields) {
    if ($null -eq $handoff.$field) {
        throw "Violação do Handoff: Campo obrigatório '$field' ausente no schema."
    }
}

# 2. Validação da Máquina de Estados (Transições Autorizadas)
$allowedTransitions = @{
    "DESCOBERTA"      = @("ORACULO_FIXADO")
    "ORACULO_FIXADO"  = @("TRIPA_VERDE")
    "TRIPA_VERDE"     = @("CRISTALIZADO", "TRIPA_VERDE")
    "CRISTALIZADO"    = @("ENRIQUECIDO", "CANARY_DEPLOY") # Permite pular enriquecimento se a fatia não exigir componentes extras
    "ENRIQUECIDO"     = @("CANARY_DEPLOY")
    "CANARY_DEPLOY"   = @("CORTE_DEFINITIVO")
}

$validTargets = $allowedTransitions[$handoff.source_state]
if (-not $validTargets -or ($handoff.target_state -notin $validTargets)) {
    throw "Transição de Estado Inválida: Não é permitido transicionar diretamente de '$($handoff.source_state)' para '$($handoff.target_state)'. Máquina de estados violada."
}

# 3. Validação de Formato de Hashes
if ($handoff.commit_hash -notmatch '^[0-9a-fA-F]{7,64}$') {
    throw "Violação de Formato: 'commit_hash' inválido ($($handoff.commit_hash)). Deve ser um hash hexadecimal válido."
}

if ($handoff.oracle_contract_hash -notmatch '^[0-9a-fA-F]{32,64}$') {
    throw "Violação de Formato: 'oracle_contract_hash' inválido ($($handoff.oracle_contract_hash)). Deve ser um hash SHA256 válido."
}

# 4. Gate de Testes e Oráculo
if ($handoff.target_state -eq "ORACULO_FIXADO") {
    # No Tempo 1, é ESPERADO que os testes de contrato falhem no novo repositório (pois a regra ainda não foi implementada)
    if ($handoff.test_execution.total -le 0) {
        throw "Bloqueio do Oráculo: Tempo 1 exige que ao menos um teste de contrato seja criado."
    }
    Write-Host ">>> [ORÁCULO] Registrado com $($handoff.test_execution.total) testes de contrato fixados (falhas esperadas na fase T1)." -ForegroundColor Yellow
} else {
    # A partir do Tempo 2 (TRIPA_VERDE em diante), TODOS os testes devem ser verdes
    if ($handoff.test_execution.failed -gt 0) {
        throw "Bloqueio de Gate: O handoff para '$($handoff.target_state)' contém $($handoff.test_execution.failed) teste(s) falhando. Todos os testes devem estar 100% verdes a partir do Tempo 2."
    }
}

# 5. Regras Arquiteturais Específicas
if ($handoff.target_state -eq "CRISTALIZADO") {
    if ($handoff.architecture_compliance.netarchtest_passed -ne $true) {
        throw "Violação de Arquitetura: Para atingir o estado 'CRISTALIZADO', o NetArchTest deve passar com 100% de conformidade."
    }
    if ($handoff.architecture_compliance.temporary_exceptions.Count -gt 0) {
        throw "Violação de Arquitetura: Fatias 'CRISTALIZADAS' não podem conter exceções temporárias ativas."
    }
}

Write-Host ">>> [SUCESSO] Handoff auditado e validado com 100% de integridade determinística. Transição autorizada para: $($handoff.target_state)" -ForegroundColor Green
