param(
    [string]$Package = "",
    [string]$VintageStoryDataPath = "",
    [string]$VintageStoryPath = ""
)

. (Join-Path $PSScriptRoot "Common.ps1")

$root = Get-GearwrightRoot
if ([string]::IsNullOrWhiteSpace($Package)) {
    $candidate = Get-ChildItem (Join-Path $root "dist") -Filter "Gearwright_*.zip" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        Write-Host "No built package was found; building one first." -ForegroundColor Yellow
        & (Join-Path $PSScriptRoot "Build-Mod.ps1") -VintageStoryPath $VintageStoryPath
        if ($LASTEXITCODE -ne 0) { throw "The package could not be built." }
        $candidate = Get-ChildItem (Join-Path $root "dist") -Filter "Gearwright_*.zip" -File |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
    }
    $packagePath = $candidate.FullName
} else {
    $packagePath = (Resolve-Path -LiteralPath $Package).Path
}

if ([IO.Path]::GetExtension($packagePath) -ne ".zip") { throw "Gearwright packages must be zip files." }
$dataPath = Find-VintageStoryData $VintageStoryDataPath
if ($null -eq $dataPath) { throw "The Vintage Story data folder could not be resolved. Set VINTAGE_STORY_DATA." }

$modsPath = [IO.Path]::GetFullPath((Join-Path $dataPath "Mods"))
$expectedRoot = [IO.Path]::GetFullPath($dataPath).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $modsPath.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to install outside the selected Vintage Story data folder."
}

New-Item -ItemType Directory -Force -Path $modsPath | Out-Null
$disabledPath = Join-Path $modsPath "Disabled"
$incomingName = Split-Path -Leaf $packagePath
$olderPackages = @(Get-ChildItem $modsPath -Filter "Gearwright_*.zip" -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne $incomingName })

if ($olderPackages.Count -gt 0) {
    New-Item -ItemType Directory -Force -Path $disabledPath | Out-Null
    foreach ($older in $olderPackages) {
        $backupName = "{0}.{1:yyyyMMdd-HHmmss}.disabled" -f $older.Name, [DateTime]::UtcNow
        Move-Item -LiteralPath $older.FullName -Destination (Join-Path $disabledPath $backupName)
        Write-Host ("Moved older package to Mods\Disabled: {0}" -f $older.Name) -ForegroundColor Yellow
    }
}

$installedPath = Join-Path $modsPath $incomingName
Copy-Item -LiteralPath $packagePath -Destination $installedPath -Force

$sourceHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
$installedHash = (Get-FileHash -LiteralPath $installedPath -Algorithm SHA256).Hash
if ($sourceHash -cne $installedHash) {
    throw "The installed Gearwright package did not match the built package."
}

Write-Host ("Installed and verified {0}. Restart Vintage Story, then run /gearwright." -f $incomingName) -ForegroundColor Green
