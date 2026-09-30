[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repoRoot "Directory.Build.props"
$issPath = Join-Path $repoRoot "installer/DualAudioMirror.iss"
$publishDir = Join-Path $repoRoot "publish/win-x64"
$artifactsDir = Join-Path $repoRoot "artifacts"

function Fail([string]$Message) {
    Write-Host "Erro: $Message"
    exit 1
}

if (-not (Test-Path -LiteralPath $propsPath)) {
    Fail "Directory.Build.props nao encontrado em $propsPath"
}

$propsContent = Get-Content -LiteralPath $propsPath -Raw
$versionMatch = [regex]::Match($propsContent, '<Version>(.*?)</Version>')
if (-not $versionMatch.Success) {
    Fail "nao foi possivel ler <Version> do Directory.Build.props"
}
$version = $versionMatch.Groups[1].Value.Trim()

if (-not $SkipBuild) {
    try {
        & (Join-Path $PSScriptRoot "build.ps1")
    }
    catch {
        Fail "falha ao executar o build.ps1: $($_.Exception.Message)"
    }
    if ($LASTEXITCODE -ne 0) {
        Fail "build.ps1 terminou com o codigo $LASTEXITCODE"
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $publishDir "DualAudioMirror.exe"))) {
    Fail "executavel publicado nao encontrado em $publishDir (rode scripts/build.ps1)"
}

$iscc = $null
foreach ($candidate in @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )) {
    if (Test-Path -LiteralPath $candidate) {
        $iscc = $candidate
        break
    }
}
if (-not $iscc) {
    $isccCommand = Get-Command "iscc" -ErrorAction SilentlyContinue
    if ($isccCommand) {
        $iscc = $isccCommand.Source
    }
}
if (-not $iscc) {
    Fail "ISCC.exe do Inno Setup 6 nao encontrado. Instale o Inno Setup 6 e tente novamente."
}

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

Write-Host "Compilando o instalador (versao $version)..."
& $iscc "/DMyAppVersion=$version" "/DPublishDir=$publishDir" "/DArtifactsDir=$artifactsDir" $issPath
if ($LASTEXITCODE -ne 0) {
    Fail "ISCC terminou com o codigo $LASTEXITCODE"
}

$setupName = "DualAudioMirror-Setup-$version.exe"
$setupPath = Join-Path $artifactsDir $setupName
if (-not (Test-Path -LiteralPath $setupPath)) {
    Fail "instalador gerado nao encontrado em $setupPath"
}

$zipName = "DualAudioMirror-$version-portable.zip"
$zipPath = Join-Path $artifactsDir $zipName
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

$hashFiles = @()
$hashFiles += Get-Item -LiteralPath $setupPath
$hashFiles += Get-Item -LiteralPath $zipPath

$sumLines = @()
foreach ($file in $hashFiles) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLower()
    $sumLines += "$hash  $($file.Name)"
}
$sumsPath = Join-Path $artifactsDir "SHA256SUMS.txt"
Set-Content -LiteralPath $sumsPath -Value $sumLines -Encoding ascii

Write-Host ""
Write-Host "Arquivos gerados em $artifactsDir :"
foreach ($file in ($hashFiles + (Get-Item -LiteralPath $sumsPath))) {
    Write-Host "  $($file.Name)"
}
Write-Host ""
Write-Host "Concluido: DualAudioMirror $version"
exit 0
