param(
    [string]$VintageStoryPath = "",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

. (Join-Path $PSScriptRoot "Common.ps1")

$root = Get-GearwrightRoot
$gamePath = Find-VintageStoryInstall $VintageStoryPath
if ($null -eq $gamePath) {
    throw "Vintage Story was not found. Pass -VintageStoryPath or set VINTAGE_STORY."
}

$dotnetMajor = Get-DotNetMajorVersion
if ($null -eq $dotnetMajor -or $dotnetMajor -lt 10) {
    throw "The .NET 10 SDK is required."
}

Write-Host "Building and packaging Gearwright..." -ForegroundColor Cyan
& dotnet build (Join-Path $root "Gearwright.csproj") -t:PackageMod -c $Configuration "/p:VintageStoryPath=$gamePath"
if ($LASTEXITCODE -ne 0) { throw "Gearwright did not build. The compiler message above has the useful detail." }

$package = Get-ChildItem (Join-Path $root "dist") -Filter "Gearwright_*.zip" -File |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if ($null -eq $package) { throw "The build finished without creating a Gearwright package." }

Write-Host ("Package ready: dist\{0}" -f $package.Name) -ForegroundColor Green
