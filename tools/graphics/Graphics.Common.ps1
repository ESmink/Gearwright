Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path (Split-Path -Parent $PSScriptRoot) "Common.ps1")

function Get-GraphicsProjectRoot {
    return (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
}

function Resolve-GearwrightPython {
    param([string]$PythonPath = "")

    $candidates = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($PythonPath)) { $candidates.Add($PythonPath) }
    if (-not [string]::IsNullOrWhiteSpace($env:GEARWRIGHT_PYTHON)) { $candidates.Add($env:GEARWRIGHT_PYTHON) }
    $pythonCommand = Get-Command python -ErrorAction SilentlyContinue
    if ($null -ne $pythonCommand) { $candidates.Add($pythonCommand.Source) }
    $pyCommand = Get-Command py -ErrorAction SilentlyContinue
    if ($null -ne $pyCommand) { $candidates.Add($pyCommand.Source + " -3") }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        try {
            if ($candidate -match '\s+-3$') {
                $parts = $candidate -split '\s+'
                & $parts[0] $parts[1] -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 11) else 1)" 2>$null
            } else {
                & $candidate -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 11) else 1)" 2>$null
            }
            if ($LASTEXITCODE -eq 0) { return $candidate }
        } catch { }
    }
    throw "Python 3.11 or newer is required. Install Python and make the 'python' command available, or pass -PythonPath."
}

function Assert-GearwrightPythonDependencies {
    param([Parameter(Mandatory = $true)][string]$Python)
    try {
        if ($Python -match '\s+-3$') {
            $parts = $Python -split '\s+'
            & $parts[0] $parts[1] -c "import numpy, PIL" 2>$null
        } else {
            & $Python -c "import numpy, PIL" 2>$null
        }
        if ($LASTEXITCODE -ne 0) { throw "missing" }
    } catch {
        throw "Python was found, but NumPy and Pillow are unavailable. Install the bounded dependencies from tools/graphics/requirements.txt, then retry. No installer was run."
    }
}

function Assert-GearwrightPythonModule {
    param(
        [Parameter(Mandatory = $true)][string]$Python,
        [Parameter(Mandatory = $true)][string]$Module,
        [Parameter(Mandatory = $true)][string]$InstallHint
    )
    try {
        if ($Python -match '\s+-3$') {
            $parts = $Python -split '\s+'
            & $parts[0] $parts[1] -c "import $Module" 2>$null
        } else {
            & $Python -c "import $Module" 2>$null
        }
        if ($LASTEXITCODE -ne 0) { throw "missing" }
    } catch {
        throw "Python is available to this process, but $Module is not. $InstallHint No installer was run."
    }
}

function Invoke-GearwrightPython {
    param(
        [Parameter(Mandatory = $true)][string]$Python,
        [object[]]$Arguments
    )
    if ($Python -match '\s+-3$') {
        $parts = $Python -split '\s+'
        & $parts[0] $parts[1] @Arguments | ForEach-Object { Write-Host $_ }
    } else {
        & $Python @Arguments | ForEach-Object { Write-Host $_ }
    }
    return [int]$LASTEXITCODE
}

function Test-JsonProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )
    if ($null -eq $Object) { return $false }
    return @($Object.PSObject.Properties | ForEach-Object { $_.Name }) -contains $Name
}

function Read-GraphicsRecipe {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $recipe = Get-Content -Raw -LiteralPath $resolved | ConvertFrom-Json
    if (-not (Test-JsonProperty $recipe "kind")) { throw "Graphics recipe is missing 'kind': $Path" }
    if (-not (Test-JsonProperty $recipe "output")) { throw "Graphics recipe is missing 'output': $Path" }
    return $recipe
}

function Resolve-GraphicsOutputPath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    if ([IO.Path]::IsPathRooted($RelativePath)) {
        throw "Graphics output paths must be project-relative, never absolute: $RelativePath"
    }

    $root = [IO.Path]::GetFullPath((Get-GraphicsProjectRoot))
    $fullPath = [IO.Path]::GetFullPath((Join-Path $root $RelativePath.Replace('/', '\')))
    $rootPrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Graphics output resolves outside the Gearwright project: $RelativePath"
    }

    $allowedRoots = @(
        (Join-Path $root "assets\gearwright"),
        (Join-Path $root "generated")
    )
    $allowed = $false
    foreach ($allowedRoot in $allowedRoots) {
        $allowedPrefix = [IO.Path]::GetFullPath($allowedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if ($fullPath.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            $allowed = $true
            break
        }
    }
    if (-not $allowed) {
        throw "Graphics outputs are limited to assets/gearwright or generated."
    }
    return $fullPath
}

function Resolve-GraphicsAssetReference {
    param(
        [Parameter(Mandatory = $true)][string]$Reference,
        [AllowNull()][AllowEmptyString()][string]$VintageStoryPath = ""
    )

    if ($Reference -notmatch '^([a-z0-9_-]+):(.+)$') {
        throw "Asset references use domain:path, for example survival:textures/block/clay/redclay.png."
    }

    $domain = $Matches[1]
    $relative = $Matches[2].Replace('/', '\')
    if ([IO.Path]::IsPathRooted($relative) -or ($relative -split '[\\/]' | Where-Object { $_ -eq '..' })) {
        throw "Asset references may not contain absolute or parent paths: $Reference"
    }

    $root = [IO.Path]::GetFullPath((Get-GraphicsProjectRoot))
    if ($domain -eq "project") {
        $candidate = [IO.Path]::GetFullPath((Join-Path $root $relative))
        $rootPrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $candidate.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Project asset reference resolves outside Gearwright: $Reference"
        }
    } elseif ($domain -eq "gearwright") {
        $candidate = Join-Path $root ("assets\gearwright\" + $relative)
    } else {
        $gamePath = Find-VintageStoryInstall $VintageStoryPath
        if ($null -eq $gamePath) {
            throw "Vintage Story was not found while resolving $Reference. Pass -VintageStoryPath or set VINTAGE_STORY."
        }
        $candidate = Join-Path $gamePath ("assets\$domain\" + $relative)
    }

    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Asset reference was not found: $Reference"
    }
    return (Resolve-Path -LiteralPath $candidate).Path
}

function Write-GraphicsJson {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $json = $Value | ConvertTo-Json -Depth 100
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, $utf8)
}

function Assert-NumberVector {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][int]$Length,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $items = @($Value)
    if ($items.Count -ne $Length) { throw "$Name must contain exactly $Length numbers." }
    foreach ($item in $items) {
        $number = 0.0
        if (-not [double]::TryParse([string]$item, [Globalization.NumberStyles]::Float,
                [Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
            throw "$Name contains a non-number."
        }
    }
    return $items
}

function Set-ObjectProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$Value
    )

    if (Test-JsonProperty $Object $Name) {
        $Object.$Name = $Value
    } else {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    }
}
