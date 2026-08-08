param(
    [string]$Package = "",
    [string]$VintageStoryPath = "",
    [ValidateRange(10, 300)][int]$TimeoutSeconds = 90,
    [ValidateRange(0, 65535)][int]$Port = 0
)

. (Join-Path $PSScriptRoot "Common.ps1")

function Get-FreeLoopbackPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    } finally {
        $listener.Stop()
    }
}

function Stop-SmokeServer {
    param([AllowNull()][Diagnostics.Process]$Process)

    if ($null -eq $Process -or $Process.HasExited) { return }
    $Process.Kill()
    $Process.WaitForExit(10000) | Out-Null
}

function Get-SmokeLogText {
    param([string]$LogsPath)

    if (-not (Test-Path -LiteralPath $LogsPath -PathType Container)) { return "" }
    return (@(Get-ChildItem -LiteralPath $LogsPath -File -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -ErrorAction SilentlyContinue }) -join "`n")
}

$root = Get-GearwrightRoot
$gamePath = Find-VintageStoryInstall $VintageStoryPath
if ($null -eq $gamePath) { throw "Vintage Story could not be found." }

$serverPath = Join-Path $gamePath "VintagestoryServer.exe"
if (-not (Test-Path -LiteralPath $serverPath -PathType Leaf)) {
    throw "The Vintage Story server executable was not found in the selected installation."
}

if ([string]::IsNullOrWhiteSpace($Package)) {
    $packageFile = Get-ChildItem (Join-Path $root "dist") -Filter "Gearwright_*.zip" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $packageFile) { throw "No built Gearwright package was found in dist." }
    $packagePath = $packageFile.FullName
} else {
    $packagePath = (Resolve-Path -LiteralPath $Package).Path
}
if ([IO.Path]::GetExtension($packagePath) -cne ".zip") { throw "Gearwright packages must be zip files." }

if ($Port -eq 0) { $Port = Get-FreeLoopbackPort }

$objPath = [IO.Path]::GetFullPath((Join-Path $root "obj"))
$smokePath = [IO.Path]::GetFullPath((Join-Path $objPath ("smoke-" + [Guid]::NewGuid().ToString("N"))))
$expectedPrefix = $objPath.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $smokePath.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not ([IO.Path]::GetFileName($smokePath)).StartsWith("smoke-", [StringComparison]::Ordinal)) {
    throw "Refusing to use an unexpected smoke-test path."
}

$modsPath = Join-Path $smokePath "Mods"
$logsPath = Join-Path $smokePath "Logs"
$serverProcess = $null

try {
    New-Item -ItemType Directory -Path $modsPath -Force | Out-Null
    New-Item -ItemType Directory -Path $logsPath -Force | Out-Null
    Copy-Item -LiteralPath $packagePath -Destination $modsPath

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $serverPath
    $startInfo.WorkingDirectory = $gamePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $false
    $startInfo.RedirectStandardError = $false
    $startInfo.Arguments = "--dataPath `"$smokePath`" --logPath `"$logsPath`" --ip 127.0.0.1 --port $Port"

    $serverProcess = [Diagnostics.Process]::new()
    $serverProcess.StartInfo = $startInfo
    if (-not $serverProcess.Start()) { throw "The isolated Vintage Story server did not start." }

    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not $serverProcess.HasExited -and -not $ready) {
        Start-Sleep -Milliseconds 500
        foreach ($logFile in @(Get-ChildItem -LiteralPath $logsPath -File -ErrorAction SilentlyContinue)) {
            if (Select-String -LiteralPath $logFile.FullName -Pattern "Entering runphase WorldReady" -Quiet) {
                $ready = $true
                break
            }
        }
    }

    Stop-SmokeServer $serverProcess
    $logText = Get-SmokeLogText $logsPath
    $loaded = $logText -match "Gearwright"
    $errors = @($logText -split "`r?`n" | Where-Object {
        $_ -match "(?i)\b(error|exception|crash|fatal)\b" -and $_ -notmatch "(?i)0 errors|no errors"
    })

    if (-not $ready -or -not $loaded -or $errors.Count -gt 0) {
        $diagnostic = @($logText -split "`r?`n" | Where-Object {
            $_ -match "(?i)Gearwright|WorldReady|error|exception|crash|fatal|runphase"
        } | Select-Object -Last 40)
        $summary = "ready=$ready; loaded=$loaded; errors=$($errors.Count)"
        if ($diagnostic.Count -gt 0) { $summary += "`n" + ($diagnostic -join "`n") }
        throw "The isolated server smoke test failed: $summary"
    }

    Write-Host "Isolated server reached WorldReady with Gearwright loaded and no logged errors." -ForegroundColor Green
} finally {
    Stop-SmokeServer $serverProcess
    if (Test-Path -LiteralPath $smokePath) {
        Remove-Item -LiteralPath $smokePath -Recurse -Force
    }
}
