<#
.SYNOPSIS
    Verificação de compatibilidade de schema pré-rollback e salvaguarda de integridade de dados (PBI11 / Q03).
.DESCRIPTION
    Verifica se a revisão N-1 de destino é compatível com o schema do banco ativo.
    Se alterações destrutivas foram aplicadas, bloqueia o rollback automático e orienta o procedimento
    de contingência operacional (Fix-Forward) para evitar corrupção de dados.
    Códigos de saída:
      0 = Compatível (Rollback seguro)
      2 = Incompatível (Rollback inseguro - Contingência Ativada)
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string]$TargetRevision,

    [Parameter(Mandatory = $true)]
    [string]$CurrentRevision,

    [Parameter(Mandatory = $false)]
    [string]$Environment = "production",

    [Parameter(Mandatory = $false)]
    [string]$ContainerAppName = "app-pix-worker-prod",

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroupName = "rg-lab-observabilidade",

    [Parameter(Mandatory = $false)]
    [string]$SqlConnectionString = "",

    [Parameter(Mandatory = $false)]
    [string]$KeyVaultName = "",

    [Parameter(Mandatory = $false)]
    [string]$TargetRevisionImage = "",

    [Parameter(Mandatory = $false)]
    [string]$MatrixPath = "docs/pipeline/schema-compatibility-matrix.json",

    [Parameter(Mandatory = $false)]
    [string]$OutputFile = "schema-compatibility-report.json"
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================================="
Write-Host " Database Schema Pre-Rollback Compatibility Safeguard"
Write-Host "=========================================================="
Write-Host "Target Revision to Restore (N-1): $TargetRevision"
Write-Host "Current Active Revision (N): $CurrentRevision"
Write-Host "Environment: $Environment"

# 1. Validação estrita de ambiente (independente de KeyVaultName ser fornecido ou não)
$allowedEnvs = @("development", "dev", "homologation", "hml", "production", "prod")
if (-not ($allowedEnvs -contains $Environment.ToLowerInvariant())) {
    Write-Error "CRITICAL: Unknown environment '$Environment'. Environment must be development/dev, homologation/hml, or production/prod."
    exit 1
}

if ([string]::IsNullOrEmpty($KeyVaultName)) {
    switch ($Environment.ToLowerInvariant()) {
        { $_ -in @("development", "dev") } { $KeyVaultName = "kv-pixlab-dev-c4vrp5pjwt" }
        { $_ -in @("homologation", "hml") } { $KeyVaultName = "kv-pixlab-hml-c4vrp5pjwt" }
        { $_ -in @("production", "prod") }  { $KeyVaultName = "kv-pixlab-prod-c4vrp5pjw" }
    }
}
Write-Host "Key Vault Mapped for Environment: $KeyVaultName"

$incompatibleChanges = @()
$isDestructiveMigrationApplied = $false
$schemaDetails = @()
$matrixVerified = $false

function Emit-ReportAndExit {
    param(
        [Parameter(Mandatory = $true)]
        [int]$ExitCode,
        [Parameter(Mandatory = $true)]
        [string]$Verdict
    )
    $report = [PSCustomObject]@{
        TargetRevision            = $TargetRevision
        CurrentRevision           = $CurrentRevision
        Environment               = $Environment
        KeyVaultUsed              = $KeyVaultName
        EvaluationTimestamp       = [System.DateTimeOffset]::UtcNow.ToString("o")
        CompatibilityVerdict      = $Verdict
        IsDestructiveApplied      = $isDestructiveMigrationApplied
        IncompatibleChanges       = $incompatibleChanges
        AppliedMigrationsSample   = $schemaDetails
        RecommendedAction         = if ($Verdict -eq "COMPATIBLE") {
            "PROCEED_WITH_ROLLBACK"
        } else {
            "TRIGGER_CONTINGENCY_FIX_FORWARD"
        }
    }
    $report | ConvertTo-Json -Depth 5 | Set-Content -Path $OutputFile -Encoding UTF8

    if ($env:GITHUB_STEP_SUMMARY) {
        $markdown = @"
## 🛡️ Pre-Rollback Schema Compatibility Safeguard ($Verdict)

- **Target Revision (N-1):** \`$TargetRevision\`
- **Current Revision (N):** \`$CurrentRevision\`
- **Environment:** \`$Environment\`
- **Key Vault:** \`$KeyVaultName\`
- **Status:** $(if ($Verdict -eq "COMPATIBLE") { "✅ SAFE FOR AUTOMATIC ROLLBACK" } else { "🚨 ROLLBACK INCOMPATIBLE - CONTINGENCY REQUIRED" })

$(if ($Verdict -eq "INCOMPATIBLE") {
@"
> [!CAUTION]
> **ATENÇÃO - RISCO DE CORRUPÇÃO DE DADOS (PBI11 / Q03):**
> O schema do banco de dados ativo sofreu alterações irreversíveis ou o contrato de compatibilidade não foi comprovado.
> O rollback de tráfego simples para a revisão anterior causará **falhas operacionais e inconsistência transacional**.
> 
> **Procedimento de Contingência Operacional Ativado:**
> 1. Rota/recurso isolado com mensagem padronizada de manutenção.
> 2. Bloqueio automático de rollback de tráfego.
> 3. Acionamento de esteira de correção emergencial direta (**Fix-Forward / Hotfix**).
"@
} else {
"> [!NOTE]`n> O schema ativo do banco de dados possui compatibilidade retroativa total (N-1 Expand Phase). A comutação de tráfego para a revisão anterior é segura."
})
"@
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $markdown
    }

    if ($Verdict -eq "INCOMPATIBLE") {
        Write-Host "=========================================================="
        [Console]::Error.WriteLine("PRE-ROLLBACK FAILED: Incompatible schema detected! Aborting rollback to prevent data corruption.")
        exit 2
    }

    Write-Host "=========================================================="
    Write-Host "SUCCESS: Database schema is fully compatible with revision $TargetRevision."
    Write-Host "=========================================================="
    exit 0
}

# 2. Verificação estrita dos identificadores de revisão
if ([string]::IsNullOrWhiteSpace($TargetRevision) -or $TargetRevision -eq $CurrentRevision) {
    $incompatibleChanges += "Invalid target revision '$TargetRevision'. Target revision must be an active, distinct prior stable revision."
    Emit-ReportAndExit -ExitCode 2 -Verdict "INCOMPATIBLE"
}

# 3. Verificação da matriz de compatibilidade de schema (PBI04 / PBI11)
$matrix = $null
if (Test-Path -Path $MatrixPath) {
    try {
        $matrix = Get-Content -Path $MatrixPath -Raw | ConvertFrom-Json
        if ($matrix.revisions) {
            Write-Host "[OK] Verified schema compatibility matrix contract ($($matrix.contract)) with explicit revisions registry."
            $matrixVerified = $true

            # Verificação estrita e antecipada da TargetRevision no mapa de revisões certificadas
            if ($matrix.revisions.PSObject.Properties[$TargetRevision]) {
                Write-Host "[OK] Target revision '$TargetRevision' is explicitly certified in schema matrix."
            } else {
                $incompatibleChanges += "FAIL-CLOSED: Target revision '$TargetRevision' is NOT explicitly certified in schema matrix 'revisions' registry! Uncertified or unknown revisions cannot be automatically restored."
                Emit-ReportAndExit -ExitCode 2 -Verdict "INCOMPATIBLE"
            }
        } else {
            $incompatibleChanges += "Schema compatibility matrix does not contain required 'revisions' map."
            Emit-ReportAndExit -ExitCode 2 -Verdict "INCOMPATIBLE"
        }
    } catch {
        $incompatibleChanges += "Failed to parse schema compatibility matrix: $_"
        Emit-ReportAndExit -ExitCode 2 -Verdict "INCOMPATIBLE"
    }
} else {
    $incompatibleChanges += "Schema compatibility matrix not found at '$MatrixPath'. Unknown schema state is fail-closed."
    Emit-ReportAndExit -ExitCode 2 -Verdict "INCOMPATIBLE"
}

# 4. Verificação de alterações destrutivas de schema (Expand vs Contract)
$baseDir = if ($env:GITHUB_WORKSPACE) { $env:GITHUB_WORKSPACE } else { "." }
$contractPhaseMarker = Join-Path $baseDir "docs/pipeline/contract-phase.lock"
if (Test-Path -Path $contractPhaseMarker) {
    $incompatibleChanges += "Destructive Contract Phase marker found! Schema contraction was completed and columns/tables were dropped."
    $isDestructiveMigrationApplied = $true
    Emit-ReportAndExit -ExitCode 2 -Verdict "INCOMPATIBLE"
}

# 5. Verificação de imutabilidade de imagem da TargetRevision contra o digest certificado
$targetContract = $matrix.revisions.$TargetRevision
if (-not $targetContract.imageDigest -or [string]::IsNullOrWhiteSpace($targetContract.imageDigest)) {
    $incompatibleChanges += "FAIL-CLOSED: Target revision '$TargetRevision' does not define required certified 'imageDigest' in schema matrix!"
    Emit-ReportAndExit -ExitCode 2 -Verdict "INCOMPATIBLE"
}

$expectedDigest = $targetContract.imageDigest.Trim()
Write-Host "Verifying immutable container image digest for target revision '$TargetRevision' against certified digest '$expectedDigest'..."

$actualImage = $TargetRevisionImage
$querySuccess = $true
if ([string]::IsNullOrWhiteSpace($actualImage)) {
    $azCmd = Get-Command az -ErrorAction SilentlyContinue
    if ($azCmd) {
        try {
            $actualImage = & az containerapp revision show --name "$ContainerAppName" --resource-group "$ResourceGroupName" --revision "$TargetRevision" --query "properties.template.containers[0].image" -o tsv 2>$null
            if ($LASTEXITCODE -ne 0) {
                $querySuccess = $false
                $actualImage = ""
            }
        } catch {
            $querySuccess = $false
            $actualImage = ""
        }
    } else {
        $querySuccess = $false
    }
}

if (-not $querySuccess -or [string]::IsNullOrWhiteSpace($actualImage)) {
    $incompatibleChanges += "FAIL-CLOSED: Failed to resolve container image for target revision '$TargetRevision' via Azure CLI or parameters!"
    Emit-ReportAndExit -ExitCode 2 -Verdict "INCOMPATIBLE"
}

# Extrai e compara rigorosamente o digest SHA256 exato
$actualDigest = if ($actualImage -match '@(sha256:[a-f0-9]{64})') { $matches[1] } elseif ($actualImage -match '(sha256:[a-f0-9]{64})') { $matches[1] } else { "" }

if ([string]::IsNullOrWhiteSpace($actualDigest) -or $actualDigest -ne $expectedDigest) {
    $incompatibleChanges += "FAIL-CLOSED: Deployed target revision image digest '$actualDigest' does NOT exactly match certified immutable digest '$expectedDigest'! Immutability contract breached."
    Emit-ReportAndExit -ExitCode 2 -Verdict "INCOMPATIBLE"
} else {
    Write-Host "[OK] Target revision '$TargetRevision' immutable image digest exact match confirmed: '$expectedDigest'."
}

# 6. Tentativa de consulta ativa de schema no banco via Key Vault
if ([string]::IsNullOrEmpty($SqlConnectionString)) {
    try {
        Write-Host "Querying Key Vault '$KeyVaultName' for database credentials..."
        $SqlConnectionString = az keyvault secret show --vault-name $KeyVaultName --name "SqlConnectionString" --query "value" -o tsv 2>$null
    }
    catch {
        Write-Warning "Key Vault query encountered exception: $_"
    }
}

if ([string]::IsNullOrWhiteSpace($SqlConnectionString)) {
    $incompatibleChanges += "FAIL-CLOSED: Database connection string could not be retrieved from Key Vault '$KeyVaultName' or parameters. Active schema state unverified."
    Emit-ReportAndExit -ExitCode 2 -Verdict "INCOMPATIBLE"
} else {
    try {
        Write-Host "Querying database migration history (DbUp SchemaVersions / EF Core __EFMigrationsHistory)..."
        $latestMigrationStr = ""
        $querySql = "SET NOCOUNT ON; IF OBJECT_ID('dbo.SchemaVersions', 'U') IS NOT NULL SELECT TOP 1 ScriptName FROM dbo.SchemaVersions ORDER BY Id DESC ELSE IF OBJECT_ID('dbo.__EFMigrationsHistory', 'U') IS NOT NULL SELECT TOP 1 MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC"
        
        # 1. Tentativa via sqlcmd CLI nativa
        $server = if ($SqlConnectionString -match 'Server=([^;]+)') { $matches[1] } else { "" }
        $db = if ($SqlConnectionString -match 'Database=([^;]+)') { $matches[1] } else { "" }
        $user = if ($SqlConnectionString -match 'User (?:Id|ID)=([^;]+)') { $matches[1] } else { "" }
        $pass = if ($SqlConnectionString -match 'Password=([^;]+)') { $matches[1] } else { "" }

        $sqlcmdExe = Get-Command sqlcmd -ErrorAction SilentlyContinue
        if ($sqlcmdExe -and $server -and $db) {
            Write-Host "Executing schema query via sqlcmd CLI..."
            $sqlcmdOutput = & sqlcmd -S $server -d $db -U $user -P $pass -Q $querySql -h -1 -W 2>$null
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($sqlcmdOutput)) {
                $latestMigrationStr = ($sqlcmdOutput -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1).Trim()
            }
        }

        # 2. Tentativa via .NET ADO.NET Providers
        if (-not $latestMigrationStr) {
            try {
                $conn = $null
                if ([System.Type]::GetType("Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient")) {
                    $conn = New-Object Microsoft.Data.SqlClient.SqlConnection($SqlConnectionString)
                } elseif ([System.Type]::GetType("System.Data.SqlClient.SqlConnection, System.Data.SqlClient")) {
                    $conn = New-Object System.Data.SqlClient.SqlConnection($SqlConnectionString)
                }
                if ($conn) {
                    $conn.Open()
                    $cmd = $conn.CreateCommand()
                    $cmd.CommandText = $querySql
                    $latestMigration = $cmd.ExecuteScalar()
                    $conn.Close()
                    if ($latestMigration) {
                        $latestMigrationStr = $latestMigration.ToString().Trim()
                    }
                }
            } catch {
                Write-Warning ".NET SQL connection attempt: $_"
            }
        }

        if ($latestMigrationStr) {
            $schemaDetails += $latestMigrationStr
            Write-Host "Latest applied migration in DB: $latestMigrationStr"

            $range = $targetContract
            if ($range) {
                $minSupported = $range.minSupportedMigration
                $maxTested = $range.maxTestedMigration

                Write-Host "Evaluating migration bounds: Current DB ($latestMigrationStr) vs Range [$minSupported, $maxTested] for target $TargetRevision"
                if ($latestMigrationStr -gt $maxTested) {
                    $incompatibleChanges += "FAIL-CLOSED: Active DB migration '$latestMigrationStr' is newer than maximum tested migration '$maxTested' for target $TargetRevision. Contract phase or unknown schema applied!"
                }
                if ($latestMigrationStr -lt $minSupported) {
                    $incompatibleChanges += "FAIL-CLOSED: Active DB migration '$latestMigrationStr' is older than minimum supported migration '$minSupported'."
                }
            }
        } else {
            $incompatibleChanges += "FAIL-CLOSED: Could not query __EFMigrationsHistory or SchemaVersions via sqlcmd or .NET SqlConnection. Active schema unverified."
        }
    }
    catch {
        Write-Warning "Direct SQL query could not be executed: $_"
        $incompatibleChanges += "FAIL-CLOSED: Direct SQL query failed with exception: $_"
    }
}

# 7. Finalização
$finalVerdict = if ($incompatibleChanges.Count -eq 0) { "COMPATIBLE" } else { "INCOMPATIBLE" }
$finalExitCode = if ($finalVerdict -eq "COMPATIBLE") { 0 } else { 2 }
Emit-ReportAndExit -ExitCode $finalExitCode -Verdict $finalVerdict
