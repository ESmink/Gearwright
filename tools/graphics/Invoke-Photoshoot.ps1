param(
    [string]$PythonPath = "",
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$ArgumentList
)

. (Join-Path $PSScriptRoot "Graphics.Common.ps1")
$root = Get-GraphicsProjectRoot
$python = Resolve-GearwrightPython -PythonPath $PythonPath
Assert-GearwrightPythonDependencies -Python $python
$exitCode = Invoke-GearwrightPython $python (@((Join-Path $root "tools\vs_photoshoot.py")) + $ArgumentList)
exit $exitCode
