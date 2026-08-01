# Graphics workflow

Gearwright builds models from cuboids and textures from inspected game or project assets. Recipes are deterministic and use logical asset references instead of installation paths.

Run all recipes:

```powershell
.\tools\graphics\Build-Graphics.ps1
```

Run one recipe:

```powershell
.\tools\graphics\Build-Graphics.ps1 -Recipe .\graphics\recipes\pottery-profile-tool.model.json
```

Find source assets:

```powershell
.\tools\graphics\Find-GameAsset.ps1 -Type Texture -Query "wood/planks/oak"
.\tools\graphics\Find-GameAsset.ps1 -Type Shape -Query "gear"
```

The finder returns references such as `survival:textures/block/clay/redclay.png`. Recipes may also use `game:`, `gearwright:`, and `project:` references.

## Output rules

Recipe outputs are limited to:

- `assets/gearwright` for runtime assets;
- `graphics/generated` for disposable previews.

Absolute paths, parent traversal, and missing source assets are rejected. The scripts do not modify the Vintage Story installation.

## Models

Model recipes use `kind: model`. They define texture mappings and named cuboid primitives. A recipe may also load a base shape and apply `addCuboid`, `move`, `scale`, `rotate`, `setTexture`, `rename`, or `remove` edits.

Inspect finished models in the Vintage Story model creator or in game. Valid JSON does not verify proportions, UVs, or rotations.

## Textures

Texture recipes use `kind: texture`. Each ordered layer is a solid fill or a logical source image. Layers support cropping, stretching, stamping, tiling, quarter-turn rotation, flipping, tint, and opacity. Rendering uses nearest-neighbor sampling.

## Asset use

- Inspect each source before using it.
- Match the game's palette, pixel density, and material cues.
- Make a new composition instead of copying a complete atlas.
- Keep logical source references in the recipe.
- Use generated imagery only when primitives and existing assets cannot produce the required result.

The current runtime example is the Potter's Profile Tool. Its model and texture recipes produce the files packaged under `assets/gearwright`.
