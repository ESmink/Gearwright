param(
    [Parameter(Mandatory = $true)][string]$Recipe,
    [string]$VintageStoryPath = ""
)

. (Join-Path $PSScriptRoot "Graphics.Common.ps1")

$recipeData = Read-GraphicsRecipe $Recipe
if ($recipeData.kind -ne "model") { throw "Build-Model.ps1 requires a recipe with kind 'model'." }
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

if ((Test-JsonProperty $recipeData "base") -and -not [string]::IsNullOrWhiteSpace([string]$recipeData.base)) {
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

if (Test-JsonProperty $recipeData "textures") {
    foreach ($textureProperty in $recipeData.textures.PSObject.Properties) {
        Set-ObjectProperty $model.textures $textureProperty.Name $textureProperty.Value
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

Write-GraphicsJson $outputPath $model
Write-Host ("Built model: {0}" -f $recipeData.output) -ForegroundColor Green
