[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param(
    [ValidateSet("Caches", "Photoshoots", "Reviews", "All")]
    [string]$Scope = "Caches"
)

. (Join-Path $PSScriptRoot "Graphics.Common.ps1")

$root = [IO.Path]::GetFullPath((Get-GraphicsProjectRoot))
$generated = Join-Path $root "generated"
$targets = [Collections.Generic.List[string]]::new()

if ($Scope -in @("Caches", "All")) {
    $targets.Add((Join-Path $generated ".model-reviewer"))
    $targets.Add((Join-Path $generated ".photoshoot-textures"))
    foreach ($cache in Get-ChildItem -LiteralPath $root -Directory -Filter "__pycache__" -Recurse -ErrorAction SilentlyContinue) {
        if (-not $cache.FullName.StartsWith($generated + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            $targets.Add($cache.FullName)
        }
    }
}
if ($Scope -in @("Photoshoots", "All")) {
    $targets.Add((Join-Path $generated "photoshoot"))
}
if ($Scope -in @("Reviews", "All")) {
    $targets.Add((Join-Path $generated "model-review"))
    if (Test-Path -LiteralPath $generated -PathType Container) {
        foreach ($review in Get-ChildItem -LiteralPath $generated -Directory -Filter "*-review") {
            $targets.Add($review.FullName)
        }
    }
}

$rootPrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($target in $targets | Select-Object -Unique) {
    $full = [IO.Path]::GetFullPath($target)
    if (-not $full.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or $full -eq $root) {
        throw "Refusing to remove a graphics artifact outside the project: $full"
    }
    if (-not (Test-Path -LiteralPath $full)) { continue }
    $relative = $full.Substring($rootPrefix.Length).Replace('\', '/')
    if ($PSCmdlet.ShouldProcess($relative, "Remove reproducible graphics artifact")) {
        Remove-Item -LiteralPath $full -Recurse -Force
        Write-Host "Removed $relative" -ForegroundColor DarkGray
    }
}
