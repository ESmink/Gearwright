param(
    [string[]]$Recipe = @(),
    [string]$Model = "",
    [string]$Definition = "",
    [string]$VintageStoryPath = "",
    [string]$PythonPath = ""
)

. (Join-Path $PSScriptRoot "Graphics.Common.ps1")

$root = Get-GraphicsProjectRoot
if (-not [string]::IsNullOrWhiteSpace($Model) -and -not [string]::IsNullOrWhiteSpace($Definition)) {
    throw "Use either -Model or -Definition, not both."
}

$textureRecipes = if ($Recipe.Count -gt 0) {
    @($Recipe | ForEach-Object { Get-Item -LiteralPath $_ }) | Where-Object { $_.Name -like "*.texture.json" }
} else {
    @(Get-ChildItem (Join-Path $root "graphics\recipes") -Filter "*.texture.json" -File | Sort-Object Name)
}
foreach ($recipeFile in $textureRecipes) {
    & (Join-Path $PSScriptRoot "Build-Texture.ps1") -Recipe $recipeFile.FullName -VintageStoryPath $VintageStoryPath
    if ($LASTEXITCODE -ne 0) { throw "Texture recipe failed: $($recipeFile.Name)" }
}

$python = Resolve-GearwrightPython -PythonPath $PythonPath
Assert-GearwrightPythonDependencies -Python $python
$env:PYTHONPATH = Join-Path $root "tools\graphics"
$selector = if (-not [string]::IsNullOrWhiteSpace($Definition)) { $Definition } else { $Model }
$arguments = @("-m", "gearwright_graphics.cli", "build", "--root", $root)
if (-not [string]::IsNullOrWhiteSpace($selector)) { $arguments += @("--definition", $selector) }
$exitCode = Invoke-GearwrightPython $python $arguments
if ($exitCode -ne 0) { throw "Python model compilation failed." }

$modelCount = if ([string]::IsNullOrWhiteSpace($selector)) { @(Get-ChildItem (Join-Path $root "graphics\models") -Filter "*.py" -File).Count } else { 1 }
Write-Host ("Built {0} texture recipe(s) and {1} Python model package(s)." -f $textureRecipes.Count, $modelCount) -ForegroundColor Green
