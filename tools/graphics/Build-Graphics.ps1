param(
    [string[]]$Recipe = @(),
    [string]$VintageStoryPath = ""
)

. (Join-Path $PSScriptRoot "Graphics.Common.ps1")

$root = Get-GraphicsProjectRoot
$recipeFiles = if ($Recipe.Count -gt 0) {
    @($Recipe | ForEach-Object { Get-Item -LiteralPath $_ })
} else {
    @(Get-ChildItem (Join-Path $root "graphics\recipes") -Filter "*.json" -File | Sort-Object Name)
}
if ($recipeFiles.Count -eq 0) { throw "No graphics recipes were found." }

foreach ($recipeFile in $recipeFiles) {
    $recipeData = Read-GraphicsRecipe $recipeFile.FullName
    switch ([string]$recipeData.kind) {
        "model" {
            & (Join-Path $PSScriptRoot "Build-Model.ps1") -Recipe $recipeFile.FullName -VintageStoryPath $VintageStoryPath
        }
        "texture" {
            & (Join-Path $PSScriptRoot "Build-Texture.ps1") -Recipe $recipeFile.FullName -VintageStoryPath $VintageStoryPath
        }
        default { throw "Unknown graphics recipe kind '$($recipeData.kind)' in $($recipeFile.Name)." }
    }
}

Write-Host ("Built {0} graphics recipe(s)." -f $recipeFiles.Count) -ForegroundColor Green
