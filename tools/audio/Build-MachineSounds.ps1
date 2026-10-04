param([switch]$Verify)

$ErrorActionPreference = 'Stop'
$soundScript = Join-Path $PSScriptRoot 'machine_sound_assets.py'
$soundAction = if ($Verify) { '--verify' } else { '--build' }
& python $soundScript $soundAction
if ($LASTEXITCODE -ne 0) { throw 'Machine sound asset generation or verification failed.' }
