param(
    [Parameter(Mandatory = $true)][string]$Query,
    [ValidateSet("Texture", "Shape", "Any")][string]$Type = "Any",
    [string]$Domain = "",
    [int]$Limit = 50,
    [string]$VintageStoryPath = ""
)

. (Join-Path $PSScriptRoot "Graphics.Common.ps1")

$gamePath = Find-VintageStoryInstall $VintageStoryPath
if ($null -eq $gamePath) { throw "Vintage Story was not found. Pass -VintageStoryPath or set VINTAGE_STORY." }
$assetsRoot = Join-Path $gamePath "assets"
$assetsPrefix = [IO.Path]::GetFullPath($assetsRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$queryText = $Query.Replace('\', '/').ToLowerInvariant()

$matches = [Collections.Generic.List[object]]::new()
foreach ($file in [IO.Directory]::EnumerateFiles($assetsRoot, "*", [IO.SearchOption]::AllDirectories)) {
    $relative = [IO.Path]::GetFullPath($file).Substring($assetsPrefix.Length).Replace('\', '/')
    $parts = $relative.Split('/', 2)
    if ($parts.Count -ne 2) { continue }
    $assetDomain = $parts[0]
    $assetPath = $parts[1]
    if (-not [string]::IsNullOrWhiteSpace($Domain) -and $assetDomain -ine $Domain) { continue }
    $isTexture = $assetPath.StartsWith("textures/", [StringComparison]::OrdinalIgnoreCase) -and $assetPath.EndsWith(".png", [StringComparison]::OrdinalIgnoreCase)
    $isShape = $assetPath.StartsWith("shapes/", [StringComparison]::OrdinalIgnoreCase) -and $assetPath.EndsWith(".json", [StringComparison]::OrdinalIgnoreCase)
    if ($Type -eq "Texture" -and -not $isTexture) { continue }
    if ($Type -eq "Shape" -and -not $isShape) { continue }
    if ($Type -eq "Any" -and -not ($isTexture -or $isShape)) { continue }
    $reference = "$assetDomain`:$assetPath"
    if (-not $reference.ToLowerInvariant().Contains($queryText)) { continue }
    $matches.Add([pscustomobject]@{
        Reference = $reference
        Kind = if ($isTexture) { "Texture" } else { "Shape" }
    })
    if ($matches.Count -ge $Limit) { break }
}

if ($matches.Count -eq 0) {
    Write-Host "No matching game assets were found." -ForegroundColor Yellow
    exit 1
}
$matches | Format-Table -AutoSize
