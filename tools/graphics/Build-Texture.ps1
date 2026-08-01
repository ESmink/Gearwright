param(
    [Parameter(Mandatory = $true)][string]$Recipe,
    [string]$VintageStoryPath = ""
)

. (Join-Path $PSScriptRoot "Graphics.Common.ps1")

$recipeData = Read-GraphicsRecipe $Recipe
if ($recipeData.kind -ne "texture") { throw "Build-Texture.ps1 requires a recipe with kind 'texture'." }
$outputPath = Resolve-GraphicsOutputPath ([string]$recipeData.output)
if ([IO.Path]::GetExtension($outputPath) -ine ".png") { throw "Texture recipes currently output PNG files." }

Add-Type -AssemblyName System.Drawing

function Convert-HexColor {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [string]$Name = "color"
    )

    if ($Value -notmatch '^#(?:(?<rgb>[0-9a-fA-F]{6})|(?<argb>[0-9a-fA-F]{8}))$') {
        throw "$Name must use #RRGGBB or #AARRGGBB."
    }
    if ($Matches.ContainsKey("rgb") -and -not [string]::IsNullOrWhiteSpace($Matches["rgb"])) {
        return [Drawing.Color]::FromArgb(255,
            [Convert]::ToInt32($Matches["rgb"].Substring(0, 2), 16),
            [Convert]::ToInt32($Matches["rgb"].Substring(2, 2), 16),
            [Convert]::ToInt32($Matches["rgb"].Substring(4, 2), 16))
    }
    return [Drawing.Color]::FromArgb(
        [Convert]::ToInt32($Matches["argb"].Substring(0, 2), 16),
        [Convert]::ToInt32($Matches["argb"].Substring(2, 2), 16),
        [Convert]::ToInt32($Matches["argb"].Substring(4, 2), 16),
        [Convert]::ToInt32($Matches["argb"].Substring(6, 2), 16))
}

function Get-IntegerRectangle {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )
    $items = @(Assert-NumberVector $Value 4 $Name)
    $rectangle = [Drawing.Rectangle]::new([int]$items[0], [int]$items[1], [int]$items[2], [int]$items[3])
    if ($rectangle.Width -le 0 -or $rectangle.Height -le 0) { throw "$Name needs positive width and height." }
    return $rectangle
}

function New-ImageAttributes {
    param(
        [Drawing.Color]$Tint,
        [double]$Opacity
    )
    if ($Opacity -lt 0 -or $Opacity -gt 1) { throw "Layer opacity must be between 0 and 1." }
    $matrix = [Drawing.Imaging.ColorMatrix]::new()
    $matrix.Matrix00 = $Tint.R / 255.0
    $matrix.Matrix11 = $Tint.G / 255.0
    $matrix.Matrix22 = $Tint.B / 255.0
    $matrix.Matrix33 = $Opacity * ($Tint.A / 255.0)
    $matrix.Matrix44 = 1.0
    $attributes = [Drawing.Imaging.ImageAttributes]::new()
    $attributes.SetColorMatrix($matrix, [Drawing.Imaging.ColorMatrixFlag]::Default, [Drawing.Imaging.ColorAdjustType]::Bitmap)
    $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
    return $attributes
}

function Draw-TextureImage {
    param(
        [Parameter(Mandatory = $true)][Drawing.Graphics]$Graphics,
        [Parameter(Mandatory = $true)][Drawing.Bitmap]$Image,
        [Parameter(Mandatory = $true)][Drawing.Rectangle]$Destination,
        [Parameter(Mandatory = $true)][Drawing.Imaging.ImageAttributes]$Attributes
    )
    $Graphics.DrawImage($Image, $Destination, 0, 0, $Image.Width, $Image.Height,
        [Drawing.GraphicsUnit]::Pixel, $Attributes)
}

$width = [int]$recipeData.width
$height = [int]$recipeData.height
if ($width -lt 1 -or $height -lt 1 -or $width -gt 4096 -or $height -gt 4096) {
    throw "Texture dimensions must be between 1 and 4096 pixels."
}

$canvas = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [Drawing.Graphics]::FromImage($canvas)
try {
    $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceOver
    $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::None

    $background = if (Test-JsonProperty $recipeData "background") {
        Convert-HexColor ([string]$recipeData.background) "background"
    } else {
        [Drawing.Color]::Transparent
    }
    $graphics.Clear($background)

    foreach ($layer in @($recipeData.layers)) {
        if (-not (Test-JsonProperty $layer "destination")) { throw "Every texture layer needs a destination rectangle." }
        $destination = Get-IntegerRectangle $layer.destination "layer destination"

        if (Test-JsonProperty $layer "fill") {
            $brush = [Drawing.SolidBrush]::new((Convert-HexColor ([string]$layer.fill) "layer fill"))
            try { $graphics.FillRectangle($brush, $destination) } finally { $brush.Dispose() }
            continue
        }
        if (-not (Test-JsonProperty $layer "source")) { throw "A texture layer needs either fill or source." }

        $sourcePath = Resolve-GraphicsAssetReference ([string]$layer.source) $VintageStoryPath
        $source = [Drawing.Bitmap]::FromFile($sourcePath)
        $working = $null
        $attributes = $null
        try {
            $sourceRectangle = if (Test-JsonProperty $layer "sourceRect") {
                Get-IntegerRectangle $layer.sourceRect "layer sourceRect"
            } else {
                [Drawing.Rectangle]::new(0, 0, $source.Width, $source.Height)
            }
            if ($sourceRectangle.X -lt 0 -or $sourceRectangle.Y -lt 0 -or
                $sourceRectangle.Right -gt $source.Width -or $sourceRectangle.Bottom -gt $source.Height) {
                throw "A layer sourceRect exceeds its source image."
            }

            $working = [Drawing.Bitmap]::new($sourceRectangle.Width, $sourceRectangle.Height,
                [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $cropGraphics = [Drawing.Graphics]::FromImage($working)
            try {
                $cropGraphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                $cropGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
                $cropGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
                $cropGraphics.DrawImage($source, [Drawing.Rectangle]::new(0, 0, $working.Width, $working.Height),
                    $sourceRectangle, [Drawing.GraphicsUnit]::Pixel)
            } finally {
                $cropGraphics.Dispose()
            }

            $rotation = if (Test-JsonProperty $layer "rotate") { [int]$layer.rotate } else { 0 }
            switch ($rotation) {
                0 { }
                90 { $working.RotateFlip([Drawing.RotateFlipType]::Rotate90FlipNone) }
                180 { $working.RotateFlip([Drawing.RotateFlipType]::Rotate180FlipNone) }
                270 { $working.RotateFlip([Drawing.RotateFlipType]::Rotate270FlipNone) }
                default { throw "Texture layer rotation must be 0, 90, 180, or 270 degrees." }
            }
            if ((Test-JsonProperty $layer "flipX") -and [bool]$layer.flipX) {
                $working.RotateFlip([Drawing.RotateFlipType]::RotateNoneFlipX)
            }
            if ((Test-JsonProperty $layer "flipY") -and [bool]$layer.flipY) {
                $working.RotateFlip([Drawing.RotateFlipType]::RotateNoneFlipY)
            }

            $tint = if (Test-JsonProperty $layer "tint") { Convert-HexColor ([string]$layer.tint) "layer tint" } else { [Drawing.Color]::White }
            $opacity = if (Test-JsonProperty $layer "opacity") { [double]$layer.opacity } else { 1.0 }
            $attributes = New-ImageAttributes $tint $opacity
            $mode = if (Test-JsonProperty $layer "mode") { [string]$layer.mode } else { "stretch" }

            if ($mode -eq "tile") {
                $tileWidth = $working.Width
                $tileHeight = $working.Height
                if (Test-JsonProperty $layer "tileSize") {
                    $tileSize = @(Assert-NumberVector $layer.tileSize 2 "layer tileSize")
                    $tileWidth = [int]$tileSize[0]
                    $tileHeight = [int]$tileSize[1]
                }
                if ($tileWidth -le 0 -or $tileHeight -le 0) { throw "Tile dimensions must be positive." }
                $savedState = $graphics.Save()
                try {
                    $graphics.SetClip($destination)
                    for ($y = $destination.Y; $y -lt $destination.Bottom; $y += $tileHeight) {
                        for ($x = $destination.X; $x -lt $destination.Right; $x += $tileWidth) {
                            Draw-TextureImage $graphics $working ([Drawing.Rectangle]::new($x, $y, $tileWidth, $tileHeight)) $attributes
                        }
                    }
                } finally {
                    $graphics.Restore($savedState)
                }
            } elseif ($mode -eq "stretch" -or $mode -eq "stamp") {
                Draw-TextureImage $graphics $working $destination $attributes
            } else {
                throw "Unknown texture layer mode '$mode'. Use stretch, stamp, or tile."
            }
        } finally {
            if ($null -ne $attributes) { $attributes.Dispose() }
            if ($null -ne $working) { $working.Dispose() }
            $source.Dispose()
        }
    }

    $directory = Split-Path -Parent $outputPath
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $canvas.Save($outputPath, [Drawing.Imaging.ImageFormat]::Png)
} finally {
    $graphics.Dispose()
    $canvas.Dispose()
}

Write-Host ("Built texture: {0}" -f $recipeData.output) -ForegroundColor Green
