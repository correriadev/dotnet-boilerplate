<#
.SYNOPSIS
    Gera um novo repositório limpo (Walking Skeleton) a partir do template neutro do boilerplate.

.DESCRIPTION
    Copia o template skeleton-host desidratado (sem entidades Pix), renomeia namespaces,
    solução e projetos para o nome do novo projeto do cliente e valida a compilação.

.PARAMETER DestinationPath
    Caminho absoluto do diretório onde o novo repositório será criado.

.PARAMETER ProjectName
    Nome base da solução e projetos (ex: 'Cliente.Cobranca').

.EXAMPLE
    .\New-SkeletonProject.ps1 -DestinationPath "C:\Projetos\novo-repo" -ProjectName "Empresa.Faturamento"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationPath,

    [Parameter(Mandatory = $true)]
    [string]$ProjectName
)

$ErrorActionPreference = 'Stop'

# Resolução robusta da raiz do repositório (scripts/engine -> scripts -> raiz)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent (Split-Path -Parent $scriptDir)
$templateDir = Join-Path $rootDir "templates\skeleton-host"

if (-not (Test-Path $templateDir)) {
    throw "Diretório de template não encontrado em: $templateDir"
}

Write-Host ">>> Inicializando Walking Skeleton para: $ProjectName" -ForegroundColor Cyan
Write-Host ">>> Template de origem: $templateDir" -ForegroundColor Cyan
Write-Host ">>> Destino: $DestinationPath" -ForegroundColor Cyan

if (Test-Path $DestinationPath) {
    if ((Get-ChildItem $DestinationPath).Count -gt 0) {
        throw "O diretório de destino '$DestinationPath' já existe e não está vazio."
    }
} else {
    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
}

# 1. Copiar estrutura do template
Copy-Item -Path "$templateDir\*" -Destination $DestinationPath -Recurse -Force

# 2. Ajustar nomes de pastas e arquivos
$apiSrcOld = Join-Path $DestinationPath "src\App.Api"
$apiSrcNew = Join-Path $DestinationPath "src\$ProjectName.Api"
if (Test-Path $apiSrcOld) {
    Rename-Item -Path $apiSrcOld -NewName "$ProjectName.Api"
    Rename-Item -Path (Join-Path $apiSrcNew "App.Api.csproj") -NewName "$ProjectName.Api.csproj"
}

$testsOld = Join-Path $DestinationPath "tests\App.Api.Tests"
$testsNew = Join-Path $DestinationPath "tests\$ProjectName.Api.Tests"
if (Test-Path $testsOld) {
    Rename-Item -Path $testsOld -NewName "$ProjectName.Api.Tests"
    Rename-Item -Path (Join-Path $testsNew "App.Api.Tests.csproj") -NewName "$ProjectName.Api.Tests.csproj"
}

# 3. Renomear solution
$slnOld = Join-Path $DestinationPath "azure-skeleton.sln"
$slnNew = Join-Path $DestinationPath "$ProjectName.sln"
if (Test-Path $slnOld) {
    Rename-Item -Path $slnOld -NewName "$ProjectName.sln"
}

# 4. Substituir ocorrências de 'App.Api' pelo novo nome do projeto nos arquivos
Get-ChildItem -Path $DestinationPath -Recurse -Include *.cs, *.csproj, *.sln, *.json, *.yml, *.bicep | ForEach-Object {
    $content = Get-Content $_.FullName -Raw -Encoding UTF8
    $updated = $content -replace 'App\.Api', "$ProjectName.Api"
    $updated = $updated -replace 'azure-skeleton', $ProjectName
    $updated = $updated -replace 'App\.sln', "$ProjectName.sln"
    Set-Content -Path $_.FullName -Value $updated -Encoding UTF8
}

Write-Host ">>> Projeto gerado com sucesso. Validando restauração e compilação..." -ForegroundColor Green
$prevDir = Get-Location
try {
    Set-Location $DestinationPath
    $slnTarget = "$ProjectName.sln"
    
    & dotnet restore $slnTarget
    if ($LASTEXITCODE -ne 0) {
        throw "Falha crítica na restauração dos pacotes NuGet do projeto gerado (Exit Code: $LASTEXITCODE)."
    }

    & dotnet build $slnTarget --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Falha crítica na compilação do projeto gerado (Exit Code: $LASTEXITCODE)."
    }

    & dotnet test $slnTarget --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "Falha na execução dos testes de fumaça do projeto gerado (Exit Code: $LASTEXITCODE)."
    }

    Write-Host ">>> [SUCESSO] Walking Skeleton compilado e testado com 100% de integridade." -ForegroundColor Green
} finally {
    Set-Location $prevDir
}
