param(
    [string]$ReviewPath = "",
    [string]$VintageStoryPath = "",
    [string]$PythonPath = ""
)

. (Join-Path $PSScriptRoot "Graphics.Common.ps1")

$root = Get-GraphicsProjectRoot
$python = Resolve-GearwrightPython -PythonPath $PythonPath
Assert-GearwrightPythonDependencies -Python $python
Assert-GearwrightPythonModule -Python $python -Module "PySide6" -InstallHint "Install the bounded reviewer dependency from tools/graphics/requirements.txt into that Python environment."
$game = Find-VintageStoryInstall $VintageStoryPath
$arguments = @(
    "-m", "gearwright_graphics.review_model",
    "--root", $root
)
if (-not [string]::IsNullOrWhiteSpace($ReviewPath)) { $arguments += @("--review", $ReviewPath) }
if ($null -ne $game) { $arguments += @("--vintage-story", $game) }

Write-Host "Starting Gearwright desktop model reviewer." -ForegroundColor Green
$reviewDescription = if ([string]::IsNullOrWhiteSpace($ReviewPath)) { "generated/slingshot-review" } else { $ReviewPath }
Write-Host "Review options are discovered from $reviewDescription. Close the window to stop." -ForegroundColor DarkGray
$env:PYTHONPATH = Join-Path $root "tools\graphics"
$exitCode = Invoke-GearwrightPython $python $arguments
exit $exitCode
