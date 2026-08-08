param(
    [Parameter(Mandatory = $true)][string]$Recipe,
    [string]$VintageStoryPath = ""
)

. (Join-Path $PSScriptRoot "Graphics.Common.ps1")

$recipeData = Read-GraphicsRecipe $Recipe
if ($recipeData.kind -ne "model") { throw "Build-Model.ps1 requires a recipe with kind 'model'." }
$root = Get-GraphicsProjectRoot
$outputPath = Resolve-GraphicsOutputPath ([string]$recipeData.output)

$allFaces = @("north", "east", "south", "west", "up", "down")

function New-PrimitiveElement {
    param([Parameter(Mandatory = $true)]$Spec)

    if (-not (Test-JsonProperty $Spec "type") -or $Spec.type -ne "cuboid") {
        throw "Model primitives currently support type 'cuboid'."
    }
    if (-not (Test-JsonProperty $Spec "name")) { throw "Every model primitive needs a stable name." }
    $from = Assert-NumberVector $Spec.from 3 "Primitive '$($Spec.name)' from"
    $to = Assert-NumberVector $Spec.to 3 "Primitive '$($Spec.name)' to"
    for ($index = 0; $index -lt 3; $index += 1) {
        if ([double]$to[$index] -le [double]$from[$index]) {
            throw "Primitive '$($Spec.name)' must have to coordinates greater than from coordinates."
        }
    }

    $element = [ordered]@{
        name = [string]$Spec.name
        from = @($from | ForEach-Object { [double]$_ })
        to = @($to | ForEach-Object { [double]$_ })
    }

    foreach ($property in @("rotationOrigin", "rotationX", "rotationY", "rotationZ", "shade", "gradientShade", "renderPass")) {
        if (Test-JsonProperty $Spec $property) {
            $element[$property] = $Spec.$property
        }
    }

    $faceNames = if (Test-JsonProperty $Spec "faces") { @($Spec.faces) } else { $allFaces }
    $faces = [ordered]@{}
    foreach ($faceName in $faceNames) {
        if ($allFaces -notcontains $faceName) { throw "Unknown face '$faceName' on primitive '$($Spec.name)'." }
        $texture = if (Test-JsonProperty $Spec "texture") { [string]$Spec.texture } else { "#all" }
        if ((Test-JsonProperty $Spec "faceTextures") -and (Test-JsonProperty $Spec.faceTextures $faceName)) {
            $texture = [string]$Spec.faceTextures.$faceName
        }
        $face = [ordered]@{ texture = $texture }
        if (Test-JsonProperty $Spec "uv") { $face.uv = @(Assert-NumberVector $Spec.uv 4 "Primitive '$($Spec.name)' uv") }
        if ((Test-JsonProperty $Spec "faceUvs") -and (Test-JsonProperty $Spec.faceUvs $faceName)) {
            $face.uv = @(Assert-NumberVector $Spec.faceUvs.$faceName 4 "Primitive '$($Spec.name)' $faceName uv")
        }
        $faces[$faceName] = $face
    }
    $element.faces = $faces

    if (Test-JsonProperty $Spec "children") {
        $element.children = @($Spec.children | ForEach-Object { New-PrimitiveElement $_ })
    }
    return [pscustomobject]$element
}

function Get-ModelElement {
    param(
        [Parameter(Mandatory = $true)]$Elements,
        [Parameter(Mandatory = $true)][string]$Name
    )

    foreach ($element in @($Elements)) {
        if ($element.name -ceq $Name) { return $element }
        if ((Test-JsonProperty $element "children") -and @($element.children).Count -gt 0) {
            $match = Get-ModelElement $element.children $Name
            if ($null -ne $match) { return $match }
        }
    }
    return $null
}

function Remove-ModelElement {
    param(
        [Parameter(Mandatory = $true)]$Elements,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $kept = [Collections.Generic.List[object]]::new()
    foreach ($element in @($Elements)) {
        if ($element.name -ceq $Name) { continue }
        if (Test-JsonProperty $element "children") {
            $element.children = @(Remove-ModelElement $element.children $Name)
        }
        $kept.Add($element)
    }
    return @($kept)
}

function Add-Vector {
    param($Left, $Right)
    $leftValues = Assert-NumberVector $Left 3 "existing vector"
    $rightValues = Assert-NumberVector $Right 3 "edit vector"
    return @(0..2 | ForEach-Object { [double]$leftValues[$_] + [double]$rightValues[$_] })
}

function Scale-Vector {
    param($Value, $Origin, $Factor)
    $values = Assert-NumberVector $Value 3 "existing vector"
    $origins = Assert-NumberVector $Origin 3 "scale origin"
    $factors = Assert-NumberVector $Factor 3 "scale factor"
    return @(0..2 | ForEach-Object { [double]$origins[$_] + ([double]$values[$_] - [double]$origins[$_]) * [double]$factors[$_] })
}

function Remove-DisabledModelFaces {
    param([Parameter(Mandatory = $true)]$Elements)

    foreach ($element in @($Elements)) {
        if (Test-JsonProperty $element "faces") {
            foreach ($faceProperty in @($element.faces.PSObject.Properties)) {
                if ((Test-JsonProperty $faceProperty.Value "enabled") -and $faceProperty.Value.enabled -eq $false) {
                    $element.faces.PSObject.Properties.Remove($faceProperty.Name)
                }
            }
            if (@($element.faces.PSObject.Properties).Count -eq 0) {
                $element.PSObject.Properties.Remove("faces")
            }
        }
        if ((Test-JsonProperty $element "children") -and @($element.children).Count -gt 0) {
            Remove-DisabledModelFaces $element.children
        }
    }
}

$hasSource = (Test-JsonProperty $recipeData "source") -and -not [string]::IsNullOrWhiteSpace([string]$recipeData.source)
$hasBase = (Test-JsonProperty $recipeData "base") -and -not [string]::IsNullOrWhiteSpace([string]$recipeData.base)
if ($hasSource -and $hasBase) {
    throw "Model recipes cannot declare both source and base."
}

if ($hasSource) {
    $sourceReference = [string]$recipeData.source
    if ([IO.Path]::IsPathRooted($sourceReference)) {
        throw "Model source paths must be project-relative: $sourceReference"
    }
    $sourcePath = [IO.Path]::GetFullPath((Join-Path $root $sourceReference))
    $rootPath = [IO.Path]::GetFullPath($root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $rootPrefix = $rootPath + [IO.Path]::DirectorySeparatorChar
    if (-not $sourcePath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Model source path leaves the project root: $sourceReference"
    }
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Model source was not found: $sourceReference"
    }
    $model = Get-Content -Raw -LiteralPath $sourcePath | ConvertFrom-Json
} elseif ($hasBase) {
    $basePath = Resolve-GraphicsAssetReference ([string]$recipeData.base) $VintageStoryPath
    $model = Get-Content -Raw -LiteralPath $basePath | ConvertFrom-Json
} else {
    $textureWidth = if (Test-JsonProperty $recipeData "textureWidth") { [int]$recipeData.textureWidth } else { 16 }
    $textureHeight = if (Test-JsonProperty $recipeData "textureHeight") { [int]$recipeData.textureHeight } else { 16 }
    $model = [pscustomobject][ordered]@{
        textureWidth = $textureWidth
        textureHeight = $textureHeight
        textures = [pscustomobject]@{}
        elements = @()
    }
}

if (-not (Test-JsonProperty $model "elements")) { Set-ObjectProperty $model "elements" @() }
if (-not (Test-JsonProperty $model "textures")) { Set-ObjectProperty $model "textures" ([pscustomobject]@{}) }
if (-not (Test-JsonProperty $model "animations")) { Set-ObjectProperty $model "animations" @() }

if (Test-JsonProperty $recipeData "textures") {
    foreach ($textureProperty in $recipeData.textures.PSObject.Properties) {
        Set-ObjectProperty $model.textures $textureProperty.Name $textureProperty.Value
    }
}

if (Test-JsonProperty $recipeData "parts") {
    foreach ($partReference in @($recipeData.parts)) {
        if ($partReference -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$partReference)) {
            throw "Model parts must be non-empty asset references."
        }
        $partPath = Resolve-GraphicsAssetReference ([string]$partReference) $VintageStoryPath
        $partModel = Get-Content -Raw -LiteralPath $partPath | ConvertFrom-Json
        if (-not (Test-JsonProperty $partModel "elements")) {
            throw "Model part has no elements: $partReference"
        }
        if (Test-JsonProperty $partModel "textures") {
            foreach ($textureProperty in $partModel.textures.PSObject.Properties) {
                Set-ObjectProperty $model.textures $textureProperty.Name $textureProperty.Value
            }
        }
        $model.elements = @($model.elements) + @($partModel.elements)
        if (Test-JsonProperty $partModel "animations") {
            $model.animations = @($model.animations) + @($partModel.animations)
        }
    }
}

if (Test-JsonProperty $recipeData "primitives") {
    $newElements = @($recipeData.primitives | ForEach-Object { New-PrimitiveElement $_ })
    $model.elements = @($model.elements) + $newElements
}

if (Test-JsonProperty $recipeData "edits") {
    foreach ($edit in @($recipeData.edits)) {
        if (-not (Test-JsonProperty $edit "op")) { throw "Every model edit needs an op." }
        if ($edit.op -eq "addCuboid") {
            $element = New-PrimitiveElement $edit
            if ((Test-JsonProperty $edit "parent") -and -not [string]::IsNullOrWhiteSpace([string]$edit.parent)) {
                $parent = Get-ModelElement $model.elements ([string]$edit.parent)
                if ($null -eq $parent) { throw "Model edit parent was not found: $($edit.parent)" }
                if (-not (Test-JsonProperty $parent "children")) { Set-ObjectProperty $parent "children" @() }
                $parent.children = @($parent.children) + @($element)
            } else {
                $model.elements = @($model.elements) + @($element)
            }
            continue
        }

        if (-not (Test-JsonProperty $edit "name")) { throw "Model edit '$($edit.op)' needs an element name." }
        $name = [string]$edit.name
        if ($edit.op -eq "remove") {
            if ($null -eq (Get-ModelElement $model.elements $name)) { throw "Model element was not found: $name" }
            $model.elements = @(Remove-ModelElement $model.elements $name)
            continue
        }

        $target = Get-ModelElement $model.elements $name
        if ($null -eq $target) { throw "Model element was not found: $name" }
        switch ($edit.op) {
            "move" {
                $target.from = @(Add-Vector $target.from $edit.by)
                $target.to = @(Add-Vector $target.to $edit.by)
                if (Test-JsonProperty $target "rotationOrigin") { $target.rotationOrigin = @(Add-Vector $target.rotationOrigin $edit.by) }
            }
            "scale" {
                $target.from = @(Scale-Vector $target.from $edit.origin $edit.factor)
                $target.to = @(Scale-Vector $target.to $edit.origin $edit.factor)
                if (Test-JsonProperty $target "rotationOrigin") { $target.rotationOrigin = @(Scale-Vector $target.rotationOrigin $edit.origin $edit.factor) }
            }
            "rename" {
                Set-ObjectProperty $target "name" ([string]$edit.to)
            }
            "rotate" {
                if (Test-JsonProperty $edit "origin") { Set-ObjectProperty $target "rotationOrigin" @(Assert-NumberVector $edit.origin 3 "rotation origin") }
                foreach ($axis in @("X", "Y", "Z")) {
                    $sourceName = "rotation$axis"
                    if (Test-JsonProperty $edit $sourceName) { Set-ObjectProperty $target $sourceName ([double]$edit.$sourceName) }
                }
            }
            "setTexture" {
                $faceNames = if (Test-JsonProperty $edit "faces") { @($edit.faces) } else { $allFaces }
                foreach ($faceName in $faceNames) {
                    if (-not (Test-JsonProperty $target.faces $faceName)) {
                        Set-ObjectProperty $target.faces $faceName ([pscustomobject]@{ texture = [string]$edit.texture })
                    } else {
                        Set-ObjectProperty $target.faces.$faceName "texture" ([string]$edit.texture)
                    }
                }
            }
            default { throw "Unknown model edit operation: $($edit.op)" }
        }
    }
}

if (Test-JsonProperty $recipeData "animations") {
    foreach ($animation in @($recipeData.animations)) {
        if (-not (Test-JsonProperty $animation "name") -or [string]::IsNullOrWhiteSpace([string]$animation.name)) {
            throw "Every model animation needs a name."
        }
        if (-not (Test-JsonProperty $animation "code") -or [string]::IsNullOrWhiteSpace([string]$animation.code)) {
            throw "Every model animation needs a stable code."
        }
        if (-not (Test-JsonProperty $animation "quantityframes") -or [int]$animation.quantityframes -lt 2) {
            throw "Animation '$($animation.code)' needs at least two frames."
        }
        if (-not (Test-JsonProperty $animation "keyframes") -or @($animation.keyframes).Count -lt 1) {
            throw "Animation '$($animation.code)' needs keyframes."
        }

        foreach ($keyframe in @($animation.keyframes)) {
            if (-not (Test-JsonProperty $keyframe "frame")) {
                throw "Animation '$($animation.code)' has a keyframe without a frame number."
            }
            $frame = [int]$keyframe.frame
            if ($frame -lt 0 -or $frame -ge [int]$animation.quantityframes) {
                throw "Animation '$($animation.code)' frame $frame is outside its frame range."
            }
            if (-not (Test-JsonProperty $keyframe "elements")) { continue }
            foreach ($elementProperty in $keyframe.elements.PSObject.Properties) {
                if ($null -eq (Get-ModelElement $model.elements $elementProperty.Name)) {
                    throw "Animation '$($animation.code)' targets missing element '$($elementProperty.Name)'."
                }
            }
        }

        $duplicate = @($model.animations | Where-Object { $_.code -ceq $animation.code }).Count -gt 0
        if ($duplicate) { throw "Duplicate model animation code '$($animation.code)'." }
        $model.animations = @($model.animations) + @($animation)
    }
}

if (@($model.animations).Count -eq 0) {
    $model.PSObject.Properties.Remove("animations")
}

if ((Test-JsonProperty $recipeData "stripDisabledFaces") -and $recipeData.stripDisabledFaces) {
    Remove-DisabledModelFaces $model.elements
}

Write-GraphicsJson $outputPath $model
Write-Host ("Built model: {0}" -f $recipeData.output) -ForegroundColor Green
