<#
.SYNOPSIS
    Validação de integridade, assinaturas e digests imutáveis do release-manifest.json.
.DESCRIPTION
    Garante conformidade com PBI01 e PBI02: rejeita tags mutáveis, verifica formato sha256:[a-f0-9]{64},
    valida integridade de metadados (commitSha, runId), integridade criptográfica e assinatura digital Cosign.
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$ManifestPath = "release-manifest.json",

    [Parameter(Mandatory = $false)]
    [string[]]$ExpectedServices = @("api", "migrator"),

    [Parameter(Mandatory = $false)]
    [switch]$Strict = $true,

    [Parameter(Mandatory = $false)]
    [switch]$VerifySignature = $false,

    [Parameter(Mandatory = $false)]
    [string]$CertificateIdentityRegexp = "^https://github\.com/[^/]+/[^/]+/\.github/workflows/package-release\.yml@refs/heads/main$"
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================================="
Write-Host " CI Integrity: Release Manifest Verification"
Write-Host "=========================================================="
Write-Host "Target Manifest Path: $ManifestPath"

if (-not (Test-Path -Path $ManifestPath)) {
    Write-Error "CRITICAL: Release manifest not found at path '$ManifestPath'."
    exit 1
}

try {
    $rawContent = Get-Content -Path $ManifestPath -Raw -Encoding UTF8
    $manifest = $rawContent | ConvertFrom-Json
}
catch {
    Write-Error "CRITICAL: Failed to parse manifest JSON: $_"
    exit 1
}

# 1. Validação de campos obrigatórios de primeiro nível
$requiredFields = @("version", "releaseId", "timestamp", "commitSha", "runId", "services", "checksum")
$missingFields = @()

foreach ($field in $requiredFields) {
    if (-not ($manifest.PSObject.Properties.Name -contains $field)) {
        $missingFields += $field
    }
}

if ($missingFields.Count -gt 0) {
    Write-Error "CRITICAL: Manifest is missing required top-level fields: $($missingFields -join ', ')"
    exit 1
}

# 2. Validação do formato do commit SHA
if ($manifest.commitSha -notmatch '^[a-fA-F0-9]{40}$') {
    Write-Error "CRITICAL: Invalid commitSha format in manifest: '$($manifest.commitSha)'. Must be a 40-char Git SHA."
    exit 1
}

Write-Host "[OK] Manifest metadata validated (ReleaseId: $($manifest.releaseId), Commit: $($manifest.commitSha))."

# 3. Validação dos serviços e digests imutáveis
$disallowedTags = @("latest", "dev", "hml", "stage", "staging", "prod", "test", "main", "master")
$servicesFound = @()

$servicesObj = $manifest.services
$serviceNames = $servicesObj.PSObject.Properties.Name

foreach ($serviceName in $serviceNames) {
    $servicesFound += $serviceName
    $service = $servicesObj.$serviceName

    Write-Host "--- Inspecting Service: '$serviceName' ---"

    if (-not $service.image) {
        Write-Error "CRITICAL: Service '$serviceName' missing 'image' repository definition."
        exit 1
    }

    if (-not $service.tag) {
        Write-Error "CRITICAL: Service '$serviceName' missing 'tag'."
        exit 1
    }

    if ($disallowedTags -contains $service.tag.ToLowerInvariant()) {
        Write-Error "CRITICAL: Mutable tag '$($service.tag)' rejected for service '$serviceName'. Only immutable SHA tags allowed."
        exit 1
    }

    if (-not $service.digest) {
        Write-Error "CRITICAL: Service '$serviceName' missing immutable 'digest'."
        exit 1
    }

    if ($service.digest -notmatch '^sha256:[a-fA-F0-9]{64}$') {
        Write-Error "CRITICAL: Service '$serviceName' contains invalid SHA256 digest format: '$($service.digest)'. Must match 'sha256:<64 hex chars>'."
        exit 1
    }

    Write-Host "[OK] Service '$serviceName': Image=$($service.image), Tag=$($service.tag), Digest=$($service.digest)"
}

# 4. Checagem de serviços esperados
if ($ExpectedServices -and $ExpectedServices.Count -gt 0) {
    foreach ($expected in $ExpectedServices) {
        if (-not ($servicesFound -contains $expected)) {
            Write-Error "CRITICAL: Expected service '$expected' was not found in release manifest."
            exit 1
        }
    }
}

# 5. Verificação da integridade por checksum
$servicesJson = $manifest.services | ConvertTo-Json -Compress -Depth 10
$hasher = [System.Security.Cryptography.SHA256]::Create()
$computedHashBytes = $hasher.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($servicesJson))
$computedHash = [System.BitConverter]::ToString($computedHashBytes).Replace("-", "").ToLowerInvariant()

if ($manifest.checksum -ne $computedHash) {
    Write-Error "CRITICAL: Manifest integrity check failed! Computed ($computedHash) does not match declared ($($manifest.checksum))."
    exit 1
} else {
    Write-Host "[OK] Manifest SHA256 checksum integrity verified ($computedHash)."
}

# 6. Verificação estrita de assinatura digital Cosign (PBI02)
if ($VerifySignature) {
    $sigBundle = "$ManifestPath.bundle"
    Write-Host "Strict signature verification requested. Target bundle: $sigBundle"
    
    if (-not (Test-Path -Path $sigBundle)) {
        Write-Error "CRITICAL: Digital signature bundle '$sigBundle' is missing! Unsigned release rejected."
        exit 1
    }

    $cosignCmd = Get-Command cosign -ErrorAction SilentlyContinue
    if (-not $cosignCmd) {
        Write-Error "CRITICAL: Cosign CLI is not installed or not found in PATH."
        exit 1
    }

    Write-Host "Verifying digital signature bundle using Cosign keyless OIDC (Identity: $CertificateIdentityRegexp)..."
    # Restrição estrita de emissor para GitHub Actions
    $issuer = "https://token.actions.githubusercontent.com"
    & cosign verify-blob --bundle $sigBundle --certificate-identity-regexp $CertificateIdentityRegexp --certificate-oidc-issuer $issuer $ManifestPath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "CRITICAL: Cosign signature verification failed for manifest! Exit code: $LASTEXITCODE."
        exit 1
    }
    Write-Host "[OK] Manifest digital signature cryptographically verified via Cosign (Issuer: $issuer)."
}

Write-Host "=========================================================="
Write-Host "SUCCESS: Release manifest successfully verified."
Write-Host "=========================================================="
exit 0
