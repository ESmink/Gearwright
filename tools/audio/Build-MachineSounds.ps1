param([switch]$Verify, [string]$VintageStoryPath = '')

$ErrorActionPreference = 'Stop'
$soundScript = Join-Path $PSScriptRoot 'machine_sound_assets.py'
if ($Verify) {
    & python $soundScript '--verify'
} else {
    . (Join-Path $PSScriptRoot '../Common.ps1')
    $soundGamePath = Find-VintageStoryInstall -RequestedPath $VintageStoryPath
    if (-not $soundGamePath) { throw 'Installed Vintage Story water sources are required to build irrigation audio.' }
    & python $soundScript '--build' '--game-assets' (Join-Path $soundGamePath 'assets')
}
if ($LASTEXITCODE -ne 0) { throw 'Machine sound asset generation or verification failed.' }
