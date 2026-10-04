param([string]$VintageStoryPath = '')

. (Join-Path $PSScriptRoot '../tools/Common.ps1')

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return }
$contractSdk = Find-VintageStoryInstall -RequestedPath $VintageStoryPath
if ($null -eq $contractSdk) { throw 'A Windows Vintage Story SDK is required for the contract dependency checks.' }
$contractProject = Join-Path $PSScriptRoot 'Gearwright.Contracts/Gearwright.Contracts.csproj'

# Probe the real SDK before the lengthy graphics tests. This target does not
# compile, restore packages, or copy binaries into the repository source.
& dotnet build $contractProject -c Release --no-restore --nologo -t:CheckNativeSqlite "/p:VintageStoryPath=$contractSdk"
if ($LASTEXITCODE -ne 0) { throw 'The contract SDK is missing its required Windows native SQLite library.' }

$missingSdk = Join-Path ([IO.Path]::GetTempPath()) ('gearwright-missing-sqlite-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $missingSdk | Out-Null
try {
    # Run a normal build, rather than the guard target alone, to prove that
    # incomplete SDKs fail before runtime even when no DLL can be copied.
    $missingOutput = @(& dotnet build $contractProject -c Release --no-restore --nologo "/p:VintageStoryPath=$missingSdk" 2>&1)
    $missingExit = $LASTEXITCODE
    if ($missingExit -eq 0 -or ($missingOutput -join "`n") -notmatch 'error GWCT001:') {
        $missingOutput | Write-Host
        throw 'An SDK without native SQLite did not fail with the required GWCT001 diagnostic.'
    }
    Write-Host '[PASS] Contract builds reject an SDK without native SQLite before runtime.' -ForegroundColor Green
} finally {
    # The exact empty fixture directory created above; no recursive deletion.
    Remove-Item -LiteralPath $missingSdk
}
exit 0
