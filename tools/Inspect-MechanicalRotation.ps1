param(
    [string]$VintageStoryPath,
    [ValidateSet('GenericMechBlockRenderer', 'MechanicalNetwork', 'BEBehaviorMPBase', 'MechanicalPowerMod')]
    [string]$TypeName = 'GenericMechBlockRenderer',
    [string[]]$MethodName = @('UpdateLightAndTransformMatrix'),
    [switch]$ListMembers
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
$rotationSdk = Find-VintageStoryInstall -RequestedPath $VintageStoryPath
if (-not $rotationSdk) { throw 'Vintage Story SDK not found; mechanical rotation inspection is unavailable.' }
[Reflection.Assembly]::LoadFrom((Join-Path $rotationSdk 'VintagestoryAPI.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $rotationSdk 'VintagestoryLib.dll')) | Out-Null
$rotationAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $rotationSdk 'Mods/VSSurvivalMod.dll'))

# Read the installed engine's actual transform convention without distributing
# its binaries or retaining a decompiled copy. Only these diagnostic methods print.
$opcodes = @{}
foreach ($field in [Reflection.Emit.OpCodes].GetFields([Reflection.BindingFlags]'Public,Static')) {
    $opcode = $field.GetValue($null)
    $opcodes[[int]$opcode.Value -band 0xffff] = $opcode
}
$rotationType = $rotationAssembly.GetType("Vintagestory.GameContent.Mechanics.$TypeName", $true)
if ($ListMembers) {
    $rotationType.GetMembers([Reflection.BindingFlags]'Instance,Static,Public,NonPublic,DeclaredOnly') |
        ForEach-Object { $_.ToString() }
    return
}
$methods = $rotationType.GetMethods([Reflection.BindingFlags]'Instance,Public,NonPublic,DeclaredOnly') |
    Where-Object { $MethodName -contains $_.Name }
if (-not $methods) { throw "No requested diagnostic method found on $TypeName." }
foreach ($method in $methods) {
    $method.ToString()
    $body = $method.GetMethodBody()
    if ($null -eq $body) { continue }
    $bytes = $body.GetILAsByteArray()
    $offset = 0
    while ($offset -lt $bytes.Length) {
        $start = $offset
        $value = [int]$bytes[$offset++]
        if ($value -eq 0xfe) { $value = 0xfe00 + [int]$bytes[$offset++] }
        $opcode = $opcodes[$value]
        $operand = ''
        $size = switch ($opcode.OperandType.ToString()) {
            'InlineNone' { 0 }
            'ShortInlineI' { $operand = [int]$bytes[$offset]; 1 }
            'ShortInlineVar' { $operand = [int]$bytes[$offset]; 1 }
            'InlineVar' { $operand = [BitConverter]::ToUInt16($bytes, $offset); 2 }
            'ShortInlineR' { $operand = [BitConverter]::ToSingle($bytes, $offset); 4 }
            'InlineR' { $operand = [BitConverter]::ToDouble($bytes, $offset); 8 }
            'InlineI8' { $operand = [BitConverter]::ToInt64($bytes, $offset); 8 }
            'InlineI' { $operand = [BitConverter]::ToInt32($bytes, $offset); 4 }
            'ShortInlineBrTarget' { $operand = '{0:X4}' -f ($offset + 1 + [sbyte]::Parse($bytes[$offset].ToString('X2'), [Globalization.NumberStyles]::HexNumber)); 1 }
            'InlineBrTarget' { $operand = '{0:X4}' -f ($offset + 4 + [BitConverter]::ToInt32($bytes, $offset)); 4 }
            'InlineSwitch' { 4 + 4 * [BitConverter]::ToInt32($bytes, $offset) }
            default {
                $token = [BitConverter]::ToInt32($bytes, $offset)
                $operand = if ($opcode.OperandType.ToString() -eq 'InlineString') {
                    $method.Module.ResolveString($token)
                } else { $method.Module.ResolveMember($token).ToString() }
                4
            }
        }
        '{0:X4}: {1} {2}' -f $start, $opcode.Name, $operand
        $offset += $size
    }
}
