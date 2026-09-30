param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'

$section = $null

if (Test-Path -LiteralPath $changelogPath) {
    $lines = @(Get-Content -LiteralPath $changelogPath)
    $pattern = '^## \[' + [regex]::Escape($Version) + '\]'
    $start = -1

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $pattern) {
            $start = $i
            break
        }
    }

    if ($start -ge 0) {
        $end = $lines.Count
        for ($i = $start + 1; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^## \[') {
                $end = $i
                break
            }
        }
        $section = ($lines[$start..($end - 1)] -join "`n").Trim()
    }
}

if ($section) {
    Write-Output $section
}
else {
    Write-Output "Ver CHANGELOG: https://github.com/brunocsilva41/DualAudioMirror/releases/tag/v$Version"
}

exit 0
