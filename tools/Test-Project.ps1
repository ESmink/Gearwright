param(
    [switch]$RequireBuild,
    [switch]$SkipGraphicsBuild,
    [string]$VintageStoryPath = ""
)

. (Join-Path $PSScriptRoot "Common.ps1")

$root = Get-GearwrightRoot
& (Join-Path $root "tests\ProjectChecks.ps1")
if ($LASTEXITCODE -ne 0) { throw "The project checks failed." }

$gamePath = Find-VintageStoryInstall $VintageStoryPath
if ($null -ne $gamePath) {
    if ($SkipGraphicsBuild) {
        Write-Host "[SKIP] Graphics build: using the committed runtime assets." -ForegroundColor Yellow
    } else {
        Write-Host "Building reproducible graphics recipes..." -ForegroundColor Cyan
        & (Join-Path $root "tools\graphics\Build-Graphics.ps1") -VintageStoryPath $gamePath
        if ($LASTEXITCODE -ne 0) { throw "The graphics recipes failed." }
    }

    Write-Host "Running save-compatibility contracts..." -ForegroundColor Cyan
    & dotnet run --project (Join-Path $root "tests\Gearwright.Contracts\Gearwright.Contracts.csproj") -c Release "/p:VintageStoryPath=$gamePath"
    if ($LASTEXITCODE -ne 0) { throw "The save-compatibility contracts failed." }

    & (Join-Path $PSScriptRoot "Build-Mod.ps1") -VintageStoryPath $gamePath
    if ($LASTEXITCODE -ne 0) { throw "The build check failed." }
} elseif ($RequireBuild) {
    throw "A build was required, but Vintage Story was not found."
} else {
    Write-Host "[SKIP] Build: Vintage Story SDK not found; repository checks still passed." -ForegroundColor Yellow
}

Write-Host "Gearwright checks completed." -ForegroundColor Green
