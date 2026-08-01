Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-GearwrightRoot {
    return (Split-Path -Parent $PSScriptRoot)
}

function Test-VintageStoryInstall {
    param([AllowNull()][AllowEmptyString()][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return Test-Path -LiteralPath (Join-Path $Path "VintagestoryAPI.dll") -PathType Leaf
}

function Find-VintageStoryInstall {
    param([AllowNull()][AllowEmptyString()][string]$RequestedPath)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) { $candidates += $RequestedPath }
    if (-not [string]::IsNullOrWhiteSpace($env:VINTAGE_STORY)) { $candidates += $env:VINTAGE_STORY }
    if (-not [string]::IsNullOrWhiteSpace($env:APPDATA)) {
        $candidates += (Join-Path $env:APPDATA "Vintagestory")
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates += (Join-Path $env:LOCALAPPDATA "Vintagestory")
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-VintageStoryInstall $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return $null
}

function Find-VintageStoryData {
    param([AllowNull()][AllowEmptyString()][string]$RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        return [IO.Path]::GetFullPath($RequestedPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($env:VINTAGE_STORY_DATA)) {
        return [IO.Path]::GetFullPath($env:VINTAGE_STORY_DATA)
    }
    if (-not [string]::IsNullOrWhiteSpace($env:APPDATA)) {
        return (Join-Path $env:APPDATA "VintagestoryData")
    }
    return $null
}

function Get-DotNetMajorVersion {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $command) { return $null }
    $versionText = (& $command.Source --version 2>$null | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($versionText)) { return $null }
    $major = 0
    if (-not [int]::TryParse(($versionText -split '\.')[0], [ref]$major)) { return $null }
    return $major
}
