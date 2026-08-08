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

function Convert-ToAssetReference {
    param([string]$Output)
    $normalized = $Output.Replace('\', '/')
    if ($normalized -match '^assets/([^/]+)/(.+)$') {
        return ($matches[1] + ':' + $matches[2]).ToLowerInvariant()
    }
    return $null
}

$entries = @($recipeFiles | ForEach-Object {
    $data = Read-GraphicsRecipe $_.FullName
    [pscustomobject]@{
        File = $_
        Data = $data
        OutputReference = Convert-ToAssetReference ([string]$data.output)
    }
})

$owners = @{}
foreach ($entry in $entries) {
    if ($null -ne $entry.OutputReference) { $owners[$entry.OutputReference] = $entry.File.FullName }
}

$built = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$remaining = [Collections.Generic.List[object]]::new()
foreach ($entry in $entries) { $remaining.Add($entry) }
$orderedEntries = [Collections.Generic.List[object]]::new()

while ($remaining.Count -gt 0) {
    $ready = [Collections.Generic.List[object]]::new()
    foreach ($entry in @($remaining)) {
        $references = [Collections.Generic.List[string]]::new()
        if ((Test-JsonProperty $entry.Data "base") -and -not [string]::IsNullOrWhiteSpace([string]$entry.Data.base)) {
            $references.Add(([string]$entry.Data.base).ToLowerInvariant())
        }
        if (Test-JsonProperty $entry.Data "parts") {
            foreach ($part in @($entry.Data.parts)) { $references.Add(([string]$part).ToLowerInvariant()) }
        }

        $waiting = $false
        foreach ($reference in $references) {
            if ($owners.ContainsKey($reference) -and -not $built.Contains($reference)) {
                $waiting = $true
                break
            }
        }
        if (-not $waiting) { $ready.Add($entry) }
    }

    if ($ready.Count -eq 0) {
        $names = @($remaining | ForEach-Object { $_.File.Name }) -join ', '
        throw "Graphics recipe dependency cycle: $names"
    }

    foreach ($entry in @($ready | Sort-Object { $_.File.Name })) {
        $orderedEntries.Add($entry)
        [void]$remaining.Remove($entry)
        if ($null -ne $entry.OutputReference) { [void]$built.Add($entry.OutputReference) }
    }
}

foreach ($entry in $orderedEntries) {
    $recipeFile = $entry.File
    $recipeData = $entry.Data
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
