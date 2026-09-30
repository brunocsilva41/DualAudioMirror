[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "publish/win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not [System.IO.Path]::IsPathRooted($Output)) {
    $Output = Join-Path $repoRoot $Output
}
$Output = [System.IO.Path]::GetFullPath($Output)

$project = Join-Path $repoRoot "src/DualAudioMirror/DualAudioMirror.csproj"

Write-Host "Publicando DualAudioMirror ($Configuration / $Runtime) em: $Output"

try {
    & dotnet publish $project -c $Configuration -r $Runtime --self-contained true -o $Output
}
catch {
    Write-Host "Erro: nao foi possivel executar o dotnet publish. $($_.Exception.Message)"
    exit 1
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "Erro: dotnet publish falhou com o codigo $LASTEXITCODE."
    exit 1
}

Write-Host "Publicacao concluida: $Output"
exit 0
