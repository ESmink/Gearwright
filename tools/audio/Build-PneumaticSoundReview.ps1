param(
    [string]$VintageStoryPath = '',
    [switch]$Inspect
)

. (Join-Path (Split-Path -Parent $PSScriptRoot) 'Common.ps1')

$gamePath = Find-VintageStoryInstall $VintageStoryPath
if ($null -eq $gamePath) {
    throw 'Vintage Story was not found. Set VINTAGE_STORY or pass -VintageStoryPath.'
}
if ($null -eq (Get-Command python -ErrorAction SilentlyContinue)) {
    throw 'The python command is required. No dependencies were installed.'
}
$arguments = @((Join-Path $PSScriptRoot 'pneumatic_sound_review.py'), '--game-path', $gamePath)
if ($Inspect) { $arguments += '--inspect' }
& python @arguments
if ($LASTEXITCODE -ne 0) { throw 'The pneumatic sound review failed.' }
