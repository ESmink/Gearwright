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

Model recipes use `kind: model`. They define texture mappings and named cuboid primitives. A recipe may also load a project-relative Blender JSON export through `source`, load a base shape, append generated shapes through `parts`, and apply `addCuboid`, `move`, `scale`, `rotate`, `setTexture`, `rename`, or `remove` edits. Blender exports retain their UVs, hierarchy, and animations while the recipe replaces local texture names with logical project or game asset references. The all-recipes build orders generated dependencies before recipes that consume them.

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

## Model photoshoots

`tools/vs_photoshoot.py` resolves a generated Vintage Story shape and its textures, then renders selected views through Blender. Use `--strict-textures` during review so unresolved materials fail instead of rendering magenta.

```powershell
python tools/vs_photoshoot.py --vintage-story <game-directory> --mod . --model assets/gearwright/shapes/item/sprinkler-brass.json --strict-textures --output graphics/generated/photoshoot/sprinkler
```

The photoshoot is a geometry and material check. Vintage Story remains authoritative for atlas blending, transparency, dynamically composed block-entity meshes, animation pivots, and particles.

The checker can compose a Gearwright pipe from the same per-face states used by its block entity, including closed face panels, hollow connections, and flush windows. Set a positive preview pressure to include visible window liquid and the sprinkler rotor:

```powershell
python tools/vs_photoshoot.py --vintage-story <game-directory> --mod . --model assets/gearwright/shapes/block/fluid-pipe-center.json --pipe-state "north=connection,east=window,down=sprinkler" --pipe-pressure 100 --strict-textures --output graphics/generated/photoshoot/pipe-active
```

Each face accepts `empty`, `connection`, or `window`; only the down face also accepts `sprinkler`. This state composition is a still image. Directional liquid scrolling and its pressure-scaled speed remain in-game checks.

The gravity-drain preview recipe assembles its Blender-authored static body, default side intake, aligned north outlet, pressure mechanism, and representative direct-flow liquid for a single reviewable still:

```powershell
python tools/vs_photoshoot.py --vintage-story <game-directory> --mod . --model graphics/generated/passive-fluid-pump-active.shape.json --strict-textures --output graphics/generated/photoshoot/gravity-drain-active
```

The Blender source is `graphics/blender/passive-fluid-pump.blend`. Its `pressure` action uses frame 0 for no pressure and frame 29 for full pressure. The matching files under `graphics/blender/exports/` are direct Vintage Story JSON exports and are the geometry and animation sources consumed by the recipes.

At runtime, C# scrubs the conventional Blender animation from the selected tank's actual input pressure. The gauge and plunger therefore remain controllable by simulation state rather than playing a fixed loop. Real liquid textures stay visible under pressure and scroll along both sight-window panes only while the drain is supplying a running network. The side intake has priority and the single intake mesh rotates upward when only a top tank is present. All animated parts rotate with the drain's placed facing.
