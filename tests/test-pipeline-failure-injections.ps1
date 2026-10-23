<#
.SYNOPSIS
    Bateria Abrangente de Injeção de Falhas para Governança de Pipeline (Rodada 5 de Sparring).
.DESCRIPTION
    Valida empiricamente os caminhos de falha:
    1. Revisão desconhecida (ausente do mapa revisions) -> Aborto imediato Fail-Closed (Exit 2).
    2. Marker de contração destrutiva (contract-phase.lock) -> Bloqueio Fail-Closed (Exit 2).
    3. Alvo idêntico à revisão atual -> Rejeição imediata (Exit 2).
    4. Digest imutável violado (imagem em execução diverge do digest certificado) -> Bloqueio Fail-Closed (Exit 2).
    5. Injeção real de falha no Key Vault -> Salvaguarda em arquivo de contingência local com alerta CRITICAL_RECORD_FAILURE.
    6. Estrutura estrita da matriz de compatibilidade -> Sem wildcard 'default'.
#>

$ErrorActionPreference = "Stop"
$testDir = "$PSScriptRoot/temp"
if (-not (Test-Path $testDir)) { New-Item -ItemType Directory -Path $testDir -Force | Out-Null }

$allPassed = $true
$testResults = @()

function Run-Test {
    param (
        [string]$Name,
        [scriptblock]$Action
    )
    Write-Host "RUNNING TEST: $Name..."
    try {
        & $Action
        Write-Host "[PASS] $Name`n" -ForegroundColor Green
        $script:testResults += [PSCustomObject]@{ Test = $Name; Status = "PASSED"; Details = "OK" }
    } catch {
        Write-Host "[FAIL] ${Name}: $_`n" -ForegroundColor Red
        $script:allPassed = $false
        $script:testResults += [PSCustomObject]@{ Test = $Name; Status = "FAILED"; Details = "$_" }
    }
}

# --- TEST 1: Revisão Desconhecida / Não Certificada na Matriz ---
Run-Test "Revisão Desconhecida Aborta Imediatamente em Fail-Closed (Exit 2)" {
    $reportFile = "$testDir/test1-report.json"
    $p = Start-Process powershell -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "scripts/ci/verify-schema-compatibility.ps1", `
        "-TargetRevision", "app-pix-worker-prod--rel-99-unknown", `
        "-CurrentRevision", "app-pix-worker-prod--rel-100", `
        "-Environment", "production", `
        "-OutputFile", $reportFile `
        -NoNewWindow -PassThru -Wait

    if ($p.ExitCode -ne 2) {
        throw "Expected ExitCode 2 (INCOMPATIBLE / CONTINGENCY_REQUIRED), but got $($p.ExitCode)"
    }
    if (-not (Test-Path $reportFile)) {
        throw "Report file was not generated!"
    }
    $report = Get-Content $reportFile -Raw | ConvertFrom-Json
    if ($report.CompatibilityVerdict -ne "INCOMPATIBLE") {
        throw "Expected CompatibilityVerdict 'INCOMPATIBLE', got '$($report.CompatibilityVerdict)'"
    }
    $hasUnknownMsg = $report.IncompatibleChanges | Where-Object { $_ -match "NOT explicitly certified in schema matrix 'revisions' registry" }
    if (-not $hasUnknownMsg) {
        throw "Expected IncompatibleChanges to state revision is not explicitly certified in revisions registry!"
    }
}

# --- TEST 2: Presença do Lock de Contração Destrutiva ---
Run-Test "Detecção de Lock de Fase de Contração (contract-phase.lock)" {
    $lockDir = "docs/pipeline"
    $lockFile = "$lockDir/contract-phase.lock"
    Set-Content -Path $lockFile -Value "MIGRATION_DROP_COLUMN_EXECUTED" -Encoding UTF8
    
    $reportFile = "$testDir/test2-report.json"
    $p = Start-Process powershell -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "scripts/ci/verify-schema-compatibility.ps1", `
        "-TargetRevision", "app-pix-worker-prod--rel-10", `
        "-CurrentRevision", "app-pix-worker-prod--rel-11", `
        "-Environment", "production", `
        "-OutputFile", $reportFile `
        -NoNewWindow -PassThru -Wait

    Remove-Item -Path $lockFile -Force -ErrorAction SilentlyContinue

    if ($p.ExitCode -ne 2) {
        throw "Expected ExitCode 2, got $($p.ExitCode)"
    }
    $report = Get-Content $reportFile -Raw | ConvertFrom-Json
    if ($report.IsDestructiveApplied -ne $true) {
        throw "Expected IsDestructiveApplied to be true!"
    }
}

# --- TEST 3: Alvo idêntico à revisão atual ---
Run-Test "Alvo idêntico à revisão atual é rejeitado imediatamente (Exit 2)" {
    $reportFile = "$testDir/test3-report.json"
    $p = Start-Process powershell -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "scripts/ci/verify-schema-compatibility.ps1", `
        "-TargetRevision", "app-pix-worker-prod--rel-10", `
        "-CurrentRevision", "app-pix-worker-prod--rel-10", `
        "-Environment", "production", `
        "-OutputFile", $reportFile `
        -NoNewWindow -PassThru -Wait

    if ($p.ExitCode -ne 2) {
        throw "Expected ExitCode 2, got $($p.ExitCode)"
    }
}

# --- TEST 4: Violação de Digest Imutável da Revisão ---
Run-Test "Digest Imutável Incorreto Viola Contrato de Imutabilidade (Exit 2)" {
    $reportFile = "$testDir/test4-report.json"
    # Passa uma imagem com digest divergente do certificado na matriz
    $p = Start-Process powershell -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "scripts/ci/verify-schema-compatibility.ps1", `
        "-TargetRevision", "app-pix-worker-prod--rel-10", `
        "-CurrentRevision", "app-pix-worker-prod--rel-11", `
        "-Environment", "production", `
        "-TargetRevisionImage", "acrpixdev.azurecr.io/pix/worker@sha256:0000000000000000000000000000000000000000000000000000000000000000", `
        "-OutputFile", $reportFile `
        -NoNewWindow -PassThru -Wait

    if ($p.ExitCode -ne 2) {
        throw "Expected ExitCode 2, got $($p.ExitCode)"
    }
    $report = Get-Content $reportFile -Raw | ConvertFrom-Json
    $hasMismatchMsg = $report.IncompatibleChanges | Where-Object { $_ -match "Immutability contract breached" }
    if (-not $hasMismatchMsg) {
        throw "Expected IncompatibleChanges to state 'Immutability contract breached'!"
    }
}

# --- TEST 5: Injeção Real de Falha de Persistência no Key Vault ---
Run-Test "Injeção de Falha em 'az keyvault secret set' e Validação de Salvaguarda Local" {
    $emergencyFile = "$testDir/emergency-incident-reconciliation.json"
    $errorLogFile = "$testDir/vault_error.log"
    if (Test-Path $emergencyFile) { Remove-Item $emergencyFile -Force }
    if (Test-Path $errorLogFile) { Remove-Item $errorLogFile -Force }

    # Bloco idêntico às condições de execução do workflow de produção
    $testScript = @"
        function az {
            param([Parameter(ValueFromRemainingArguments)]`$Args)
            [Console]::Error.WriteLine("az : CLI_TIMEOUT: Simulated Key Vault network timeout")
            `$global:LASTEXITCODE = 1
            return `$null
        }

        `$recRecord = [PSCustomObject]@{
            phase               = "ReconciliationRequired"
            reason              = "AutomaticRollbackTrafficSetFailed"
            activeRevision      = "app-pix-worker-prod--rel-10"
            candidateRevision   = "app-pix-worker-prod--rel-11"
            environment         = "production"
            timestamp           = [System.DateTimeOffset]::UtcNow.ToString("o")
            reconciliationState = "INCIDENT_OPEN"
        }

        # Bloco rigorosamente idêntico ao workflow de produção
        `$recRecord | ConvertTo-Json -Depth 5 | Set-Content -Path "$emergencyFile" -Encoding UTF8
        az keyvault secret set --vault-name "kv-pixlab-prod-c4vrp5pjwt" --name "DeploymentState" --value (`$recRecord | ConvertTo-Json -Compress)
        if (`$LASTEXITCODE -ne 0) {
            Write-Error "CRITICAL_RECORD_FAILURE: Failed to persist incident in Key Vault (Exit `$LASTEXITCODE)! Local emergency evidence preserved at emergency-incident-reconciliation.json."
            exit 1
        }
"@

    $testScriptFile = "$testDir/simulate_vault_failure.ps1"
    Set-Content -Path $testScriptFile -Value $testScript -Encoding UTF8

    $p = Start-Process powershell -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $testScriptFile `
        -RedirectStandardError $errorLogFile -NoNewWindow -PassThru -Wait

    if ($p.ExitCode -ne 1) {
        throw "Expected workflow persistence block to abort with exit code 1, but got $($p.ExitCode)"
    }
    if (-not (Test-Path $emergencyFile)) {
        throw "Emergency incident file was not created!"
    }
    $saved = Get-Content $emergencyFile -Raw | ConvertFrom-Json
    if ($saved.reconciliationState -ne "INCIDENT_OPEN" -or $saved.reason -ne "AutomaticRollbackTrafficSetFailed") {
        throw "Emergency incident file does not contain expected INCIDENT_OPEN payload!"
    }
    if (-not (Test-Path $errorLogFile)) {
        throw "Error log file was not generated!"
    }
    $errContent = Get-Content $errorLogFile -Raw
    if (-not ($errContent -match "CRITICAL_RECORD_FAILURE")) {
        throw "Expected error output to explicitly state CRITICAL_RECORD_FAILURE, but got: $errContent"
    }
    Write-Host "[OK] Verified CRITICAL_RECORD_FAILURE emitted, exit code 1 confirmed, and emergency JSON preserved."
}

# --- TEST 6: Integridade do Schema da Matriz de Compatibilidade ---
Run-Test "Matriz de Compatibilidade Contém Revisions Estritas sem Wildcard Default Cego" {
    $matrix = Get-Content -Path "docs/pipeline/schema-compatibility-matrix.json" -Raw | ConvertFrom-Json
    if (-not $matrix.revisions) {
        throw "Matrix is missing 'revisions' property!"
    }
    if ($matrix.revisions.PSObject.Properties["default"]) {
        throw "Blind wildcard 'default' must NOT exist in revisions map to prevent permissive rollback!"
    }
    $prodRel10 = $matrix.revisions."app-pix-worker-prod--rel-10"
    if (-not $prodRel10.minSupportedMigration -or -not $prodRel10.maxTestedMigration -or -not $prodRel10.imageDigest) {
        throw "app-pix-worker-prod--rel-10 missing certified migration bounds or immutable imageDigest!"
    }
}

Write-Host "=========================================================="
Write-Host " SUMMARY OF INJECTION TESTS"
Write-Host "=========================================================="
$testResults | Format-Table -AutoSize

if (-not $allPassed) {
    Write-Error "One or more injection tests failed!"
    exit 1
}

Write-Host "ALL INJECTION TESTS COMPLETED SUCCESSFULLY."
exit 0

