---
name: vintage-story-modeling
description: Author, animate, review, migrate, and validate Vintage Story cuboid models in Gearwright. Use for Python model definitions, compiled shape JSON, animation candidates, texture recipes, managed approval packages, photoshoot renders, the interactive reviewer, graphics ownership audits, or model-pipeline cleanup.
---

# Vintage Story Modeling

Use Gearwright's deterministic Python pipeline and keep the maintainer in the loop. Treat approval as a hard boundary between review candidates and runtime assets.

## Non-negotiable rules

- Read `AGENTS.md` and `graphics/README.md` before editing.
- Use `python` for graphics commands. A sandbox or agent PATH mismatch is not evidence that Python is absent. Use an explicitly configured interpreter for that session when necessary, but never commit its path or add a developer-specific fallback.
- Preserve public asset codes, animation codes, element names used by code, and accepted runtime semantics.
- Never hand-edit compiled shape JSON.
- Never place Python source, dependencies, or caches under `generated/`.
- Do not emit a candidate into `assets/gearwright/`, gameplay code, or a mod package before maintainer approval.
- When a review package is ready, launch the interactive reviewer for the maintainer by running the documented Python command yourself. Do not require the maintainer to copy or run it. If launching a visible GUI needs approval, request that approval through the available tool; provide a manual command only after the launch attempt fails.
- Treat the photoshoot and OpenGL reviewer as diagnostics. Validate engine-sensitive UV, render-pass, particle, lighting, and playback behavior in Vintage Story.

## File ownership

- `graphics/models/<family>.py`: approved, reusable runtime definitions.
- `graphics/review/<feature>.py`: programmatic candidate or workflow-fixture source.
- `graphics/recipes/`: deterministic texture recipes with logical asset references.
- `assets/gearwright/shapes/`: checked-in compiler output packaged with the mod.
- `generated/<feature>-review/current/`: one managed human-decision package.
- `generated/model-review/` and `generated/photoshoot/`: disposable manifests and still renders.

Every review generator must own one fixed `current/` path, replace it on rerun, and write an ownership manifest containing its checked-in source, managed path, candidates, animations, and decision status. Do not create revision directories on every agent turn.

## Workflow

### 1. Audit before changing

- Read adjacent definitions and tests.
- Run `python -m gearwright_graphics.cli inventory --root .` and confirm every runtime shape has exactly one owner.
- When migrating an existing model, freeze accepted semantics first: texture maps and sizes, element order and hierarchy, bounds, origins, rotations, face UVs, render passes, and complete animation metadata.
- Find installed texture sources with `tools/graphics/Find-GameAsset.ps1` and inspect every selected source. Never edit or copy an unmodified game atlas.

### 2. Build a managed candidate package

- Put programmatic candidate source in `graphics/review/<feature>.py`; emit only to `generated/<feature>-review/current/`.
- Offer materially different candidates when design is uncertain. Describe what each option tests and what the maintainer should compare.
- Use identical cameras, framing, lighting, background, and sizes for comparative stills.
- For animations, provide rest, a meaningful intermediate pose, and the final pose. State actual travel or rotation values when scale matters.
- Use the photoshoot for comparable evidence and the persistent OpenGL reviewer for orbiting, exact side views, depth/occlusion checks, and animation scrubbing.
- Ask the maintainer to choose or revise an option, then pause. An agent's aesthetic judgment is not approval.

### 3. Close the decision cleanly

- Record the approved candidate ID and exact copied review reference when available.
- Rebuild the managed package with only the approved candidate; remove rejected candidates, stale renders, one-off scripts, and abandoned revisions.
- For a workflow fixture, record `runtimePromotion: false` and stop.
- For a real feature, move or refactor the approved definition into `graphics/models/`, retain stable identifiers, compile its runtime JSON, and add focused semantic tests.
- Remove the full generated review package after the decision is durably represented and it is no longer useful, unless the maintainer asks to retain it.

### 4. Validate and deliver

Run from the repository root:

```powershell
python -m gearwright_graphics.cli validate --root .
python -m gearwright_graphics.cli inventory --root .
python -m unittest discover tests/graphics
.\tools\graphics\Build-Graphics.ps1 -PythonPath python
```

Then run `tools/Test-Project.ps1 -RequireBuild`, `tools/Install-Mod.ps1`, and `git diff --check`. Inspect tracked and staged files, confirm generated output is ignored, and ensure no local paths, usernames, DLLs, archives, caches, or unrelated binaries entered the change.

## Review checks

- Compare front, back, both sides, top, and an oblique view.
- Check which surface is in front at every overlap; inspect transparent and cutout parts separately.
- Check proportions against the block grid and neighboring attachment points.
- Scrub animations slowly for pivot drift, detached pieces, reversed travel, stretching, clipping, and discontinuities at loop or release frames.
- Verify bands, belts, rods, pipes, and other linked parts remain anchored throughout motion.
- Use nearest-neighbor textures and inspected Vintage Story material cues; placeholder or missing textures invalidate visual approval.

## Reviewer commands

Start a named package:

```powershell
.\tools\graphics\Review-Model.ps1 -PythonPath python -ReviewPath generated/<feature>-review/current
```

If PowerShell execution is disabled:

```powershell
$env:PYTHONPATH = "tools/graphics"
python -m gearwright_graphics.review_model --review generated/<feature>-review/current
```

The reviewer provides candidate/state selection, orbit/pan/zoom, exact side views, animation selection, frame scrubbing, playback, fit/reset controls, and Copy review reference. It keeps one OpenGL viewport alive; camera movement must never launch the photoshoot renderer or create PNG caches.

If PySide6 is missing, report `python -m pip install -r tools/graphics/requirements.txt`; do not install it without user authorization and never target the install into `generated/`.

Use `tools/graphics/Clear-GraphicsArtifacts.ps1` for scoped cleanup. Default cache cleanup must preserve human decision packages and photoshoots; broader scopes require intentional selection.
