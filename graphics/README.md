# Graphics workflow

Gearwright model authoring uses Python 3.11 or newer. Use the `python` command; it must be available to agents. NumPy 2.x and Pillow 10-12 are required for the CPU photoshoot, and PySide6 Essentials is required for the interactive reviewer. The repository does not install Python or its dependencies.

The PowerShell wrappers discover Python in this order: `-PythonPath`, `GEARWRIGHT_PYTHON`, `python` on `PATH`, then `py -3` on Windows. A missing interpreter or library produces one direct error. Set up dependencies outside the repository from `tools/graphics/requirements.txt`.

## Daily loop

Find and inspect source textures before authoring:

```powershell
.\tools\graphics\Find-GameAsset.ps1 -Type Texture -Query "wood/planks/oak"
```

Edit one logical family in `graphics/models/`. The current definitions are `pottery_profile_tool.py`, `fluid_pipe.py`, `sprinkler.py`, `creative_fluid_pump.py`, and `passive_fluid_pump.py`. A definition may emit several runtime shapes and named review scenes.

Validate the definitions, inspect the exact owner of every runtime shape, and run the focused tests:

```powershell
python -m gearwright_graphics.cli validate --root .
python -m gearwright_graphics.cli inventory --root .
python -m unittest discover tests/graphics
```

Build textures first and then every model definition:

```powershell
.\tools\graphics\Build-Graphics.ps1 -PythonPath python
```

Build one family with `-Model fluid_pipe` or one project-relative definition with `-Definition graphics/models/fluid_pipe.py`. `-Recipe` remains available for individual texture recipes. Generated shape JSON under `assets/gearwright/shapes/` is reviewable build output; do not hand-edit it. A full build fails if two definitions claim the same output; full validation fails if a runtime shape has no definition owner. Ordinary mod packaging never executes Python.

## Authoring contract

Definitions use Vintage Story model units: 16 model units equal one block. `Vec3` values in definitions are model units. UV rectangles are texture pixels. Animation offsets are model units. Renderer camera coordinates are block units and never appear in model definitions.

The authoring API supports named cuboids, parent/child hierarchies, non-rendering pivots, explicit or automatic box UVs, six faces, quarter-turn UV rotation, glow, shade, gradient shade, render passes, scales, constructive arrays, collars, frames, and copied components. Only cuboid geometry that Vintage Story shape JSON can represent is accepted.

The compiler supports per-texture sizes; elements, children, bounds, rotations, origins, and scales; face UVs, rotation, glow, and reflective mode; and complete Vintage Story animation metadata. It rejects meshes, curves, booleans, non-finite values, invalid faces, unsupported rotations, missing animation targets, unsafe paths, and duplicate names, codes, or output owners.

## Human review loop

New model and animation work starts under `generated/<feature>-review/`. Keep a short `README.md` there that names each candidate and explains the meaningful differences. Generate comparable stills with the photoshoot, then use the desktop reviewer to orbit the shapes and scrub animations before choosing one. Only an approved candidate moves into `graphics/models/` and runtime assets.

Install the reviewed dependencies in the Python environment or virtual environment used for graphics work:

```powershell
python -m pip install -r tools/graphics/requirements.txt
```

Launch the newest slingshot review package, or name another package explicitly:

```powershell
.\tools\graphics\Review-Model.ps1 -PythonPath python
.\tools\graphics\Review-Model.ps1 -PythonPath python -ReviewPath generated/<feature>-review
```

If PowerShell script execution is disabled, start the same app directly:

```powershell
$env:PYTHONPATH = "tools/graphics"
python -m gearwright_graphics.review_model --review generated/<feature>-review
```

The reviewer keeps one OpenGL viewport alive. It loads a shape once, updates animation poses in memory, uses a depth buffer for opaque and cutout geometry, and draws sorted transparent faces afterward. Left-drag orbits, middle- or right-drag pans, the wheel zooms, the view buttons provide exact sides, and Fit model restores framing. Copy review reference puts the selected package, candidate, state, animation frame, and camera on the clipboard so approval points to an exact view. The candidate list refreshes when shape files change.

The OpenGL preview is closer to the game's depth, culling, nearest-texture sampling, and flat directional lighting than the photoshoot. Vintage Story remains authoritative for atlas behavior, particles, dynamic lighting, and runtime animation startup.

## Photoshoot

`tools/vs_photoshoot.py` is the compatibility entry point for the deterministic NumPy/Pillow still renderer. Use strict textures for acceptance; every unresolved game texture must be fixed or supplied through an explicit logical override.

```powershell
python tools/vs_photoshoot.py --vintage-story <game-directory> --mod . --model assets/gearwright/shapes/item/sprinkler-brass.json --strict-textures --views front-right,top --size 640x480 --output generated/photoshoot/sprinkler
python tools/vs_photoshoot.py --vintage-story <game-directory> --mod . --scene passive_fluid_pump:side-tank-active --animation pressure --frames 0,14.5,29 --contact-sheet --output generated/photoshoot/passive-pressure
```

Useful controls include named views, `--camera-position`, `--camera-target`, `--orthographic`, `--orthographic-scale`, `--lighting`, `--labels`, `--only`, `--hide`, `--ghost`, `--explode`, and contact sheets. Manifests record logical assets and review settings without absolute game, mod, output, Python, or user paths. The photoshoot remains useful for comparable fixed evidence; it is not the interactive approval surface.

## Output ownership and cleanup

- `graphics/models/` contains the checked-in source of runtime shape JSON.
- `graphics/review/` contains checked-in, explicitly non-runtime workflow fixtures with fixed managed output paths.
- `assets/gearwright/shapes/` contains checked-in compiler output packaged with the mod.
- `generated/model-review/` contains disposable compiler manifests.
- `generated/<feature>-review/` contains human-review candidates and stays until a decision is recorded.
- `generated/photoshoot/` contains disposable still renders.

List exact runtime ownership with `python -m gearwright_graphics.cli inventory --root .`. Remove only caches with `.\tools\graphics\Clear-GraphicsArtifacts.ps1`; pass `-Scope Photoshoots`, `-Scope Reviews`, or `-Scope All` only when those human-facing artifacts are intentionally disposable. Python packages belong in the selected Python environment, never under `generated/`.

## Delivery

Texture recipes remain under `graphics/recipes/` and use logical `domain:path` references. Package contents include runtime assets only; definitions, tests, requirements, manifests, and review images are development files.

Run `tools/Test-Project.ps1 -RequireBuild` and then `tools/Install-Mod.ps1` before delivery. Perform final in-game checks when proportions, UVs, rotations, particles, or runtime behavior matter.
