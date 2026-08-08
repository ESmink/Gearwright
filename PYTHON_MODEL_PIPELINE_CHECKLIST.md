# Python model pipeline and photoshoot replacement checklist

Implementation owner: Luna

This checklist replaces Gearwright's JSON/Blender model-authoring paths with Python model definitions and removes Blender from both authoring and photoshoot rendering. Runtime outputs remain ordinary Vintage Story 1.22.3 shape JSON files. Mod users do not need Python or any graphics tools.

## Fixed decisions

- [ ] Treat Python as a required development dependency. Do not add an installer or download Python from a project script.
- [ ] Use Python for model definitions, compilation, animation evaluation, scene composition, and photoshoot rendering.
- [ ] Keep textures under the existing deterministic texture-recipe pipeline unless a later task explicitly replaces it.
- [ ] Store each logical machine or item, including all of its runtime sub-shapes and animations, in one Python definition file.
- [ ] Generate only Vintage Story-supported JSON into `assets/gearwright/shapes/`.
- [ ] Commit runtime shape JSON so packages and ordinary mod builds do not execute Python.
- [ ] Treat model definitions as trusted, checked-in developer code. Restrict their declared inputs and outputs to project-relative paths even though the Python itself is not sandboxed.
- [ ] Remove Blender completely from the active toolchain: no `.blend` files, Blender exports, `bpy`, Blender executable discovery, compatibility helpers, or Blender-only CLI flags.
- [ ] Implement photoshoot as a deterministic CPU software renderer using Python, NumPy, and Pillow. Do not replace Blender with another external GUI modeling package or GPU-dependent renderer.
- [ ] Keep the photoshoot diagnostic rather than engine-identical. Vintage Story and in-game testing remain authoritative for atlas behavior, render passes, particles, dynamic lighting, and final animation playback.
- [ ] Preserve every public asset code and every animation code used by runtime C#.
- [ ] Keep the legacy model recipes and Blender files until the corresponding Python model passes structural and visual acceptance. Remove them only in the final migration phase.

## Intended repository layout

- [ ] Add `graphics/models/` for checked-in model definitions.
- [ ] Add one definition file per logical asset family:
  - [ ] `graphics/models/example_workshop_marker.py`
  - [ ] `graphics/models/pottery_profile_tool.py`
  - [ ] `graphics/models/fluid_pipe.py`
  - [ ] `graphics/models/sprinkler.py`
  - [ ] `graphics/models/creative_fluid_pump.py`
  - [ ] `graphics/models/passive_fluid_pump.py`
- [ ] Let one definition emit multiple runtime shapes. For example, `fluid_pipe.py` owns center, arm, cap, window, slug, and inventory shapes; `passive_fluid_pump.py` owns body, intake, outlet, mechanism, and liquid shapes.
- [ ] Add the reusable package under `tools/graphics/gearwright_graphics/`:
  - [ ] `model.py` for the authoring API and immutable model data.
  - [ ] `animation.py` for keyframes and pose evaluation.
  - [ ] `compiler.py` for validation and Vintage Story JSON emission.
  - [ ] `assets.py` for safe project/game/mod asset resolution.
  - [ ] `blocks.py` for block type, variant, shape, and texture resolution.
  - [ ] `scene.py` for review scenes, state composition, neighbors, and explosion transforms.
  - [ ] `geometry.py` for matrices, hierarchy traversal, cuboid faces, triangulation, and bounds.
  - [ ] `raster.py` for the CPU depth-buffer rasterizer.
  - [ ] `materials.py` for texture sampling, alpha, glow, and diagnostic shading.
  - [ ] `photoshoot.py` for camera, lighting, image output, contact sheets, and manifests.
  - [ ] `cli.py` for `build`, `validate`, and `photoshoot` subcommands.
- [ ] Keep `tools/vs_photoshoot.py` as a small compatibility entry point that imports and runs the new package. It must contain no rendering implementation and no Blender logic.
- [ ] Add a short PowerShell entry point such as `tools/graphics/Invoke-Photoshoot.ps1` so repository commands can use the same Python discovery rules as `Build-Graphics.ps1`.
- [ ] Add `tools/graphics/requirements.txt` with reviewed, bounded NumPy and Pillow requirements. Do not add an automatic dependency installer.
- [ ] Add Python tests under `tests/graphics/` using the standard-library `unittest` runner unless there is a concrete reason to add another test dependency.

## Phase 1: freeze the existing behavior

- [ ] Record the current generated shape inventory, texture maps, element hierarchy, animations, and output paths before changing builders.
- [ ] Copy minimal JSON fixtures needed for migration tests into `tests/graphics/fixtures/`; do not copy game assets or absolute paths.
- [ ] Capture semantic fixtures for every current model recipe:
  - [ ] Output asset path.
  - [ ] Texture width and height.
  - [ ] Texture keys and logical asset references.
  - [ ] Element names and parent/child paths.
  - [ ] Element bounds, rotation origins, rotations, scales, face enablement, UVs, face rotations, glow, shade, gradient shade, and render pass.
  - [ ] Animation names, codes, frame counts, stop/end behavior, keyframes, and targeted elements.
- [ ] Add explicit fixtures for the passive pump's `pressure` animation:
  - [ ] Frame 0 gauge rotation is `-52` degrees.
  - [ ] Frame 29 gauge rotation is `52` degrees.
  - [ ] Frame 29 plunger `offsetY` is `0.85`.
  - [ ] Animation code remains `pressure` with 30 frames.
- [ ] Record representative current photoshoot commands and manifests for:
  - [ ] A standalone item.
  - [ ] A plain static block.
  - [ ] An active pipe with connection, window, liquid, and sprinkler state.
  - [ ] The passive pump body plus intake, outlet, animated mechanism, and liquid.
- [ ] Use existing rendered images only as human visual references. Do not make Blender pixels a permanent automated golden test.

## Phase 2: establish the Python development contract

- [ ] Require a documented minimum Python version with modern type annotations and `dataclasses`; prefer Python 3.11 or newer unless the maintained development environment requires a different version.
- [ ] Add Python discovery to the PowerShell wrappers in this order:
  - [ ] An explicit `-PythonPath` parameter.
  - [ ] A project-specific `GEARWRIGHT_PYTHON` environment variable.
  - [ ] `python` on `PATH`.
  - [ ] `py -3` on Windows.
- [ ] Never record the resolved Python executable path in tracked output or photoshoot manifests.
- [ ] Fail with one direct message when Python, NumPy, or Pillow is unavailable. The message may name the missing requirement but must not install it.
- [ ] Make every command runnable from any working directory by resolving the project root from the script location.
- [ ] Do not modify `sys.path` with an absolute developer path. Only add paths derived from the checked-in script location.
- [ ] Add `python -m unittest discover tests/graphics` to project verification.
- [ ] Decide and document float precision once. Normalize negative zero, reject NaN/infinity, and serialize numbers with stable invariant formatting.

## Phase 3: implement the model-authoring API

- [ ] Create an immutable `Vec3` type and explicit helpers for Vintage Story voxel coordinates, block coordinates, degrees, and UV rectangles.
- [ ] Use Vintage Story coordinates in authoring files. One block is 16 model units; do not expose renderer-specific coordinates in model definitions.
- [ ] Create `ModelPackage`, `Shape`, `Element`, `Face`, `Texture`, `Animation`, `Keyframe`, `Transform`, `ReviewAssembly`, and `ReviewScene` types.
- [ ] Return stable element reference objects from `shape.box()` and `shape.pivot()`. Animation and review APIs must accept these references instead of repeating element-name strings.
- [ ] Require every emitted element to have an explicit stable name.
- [ ] Support visible cuboids with:
  - [ ] `from` and `to` coordinates.
  - [ ] Parent/child hierarchy.
  - [ ] Rotation origin and X/Y/Z rotation.
  - [ ] Scale when supported by the target shape schema.
  - [ ] Shade and gradient shade.
  - [ ] Render pass.
  - [ ] Optional group metadata when Vintage Story consumes it.
- [ ] Support non-rendering pivot elements without fake `#null` materials. Emit the minimal Vintage Story-compatible element with no enabled faces.
- [ ] Support all six faces with:
  - [ ] A texture key or alias.
  - [ ] Enabled/disabled state.
  - [ ] Explicit UV rectangle.
  - [ ] Quarter-turn face rotation.
  - [ ] Glow.
  - [ ] Reflective mode or other fields only when verified against Vintage Story 1.22.3.
- [ ] Provide predictable automatic box UVs for quick iteration, while preserving explicit per-face UV overrides for final assets.
- [ ] Add small parametric helpers rather than shape-specific magic:
  - [ ] Mirror across X, Y, or Z around an explicit plane.
  - [ ] Repeat around an axis.
  - [ ] Linear arrays.
  - [ ] Named frames made from rails/posts.
  - [ ] Collars made from four or more cuboids.
  - [ ] Copy a component with a transform and name prefix.
- [ ] Keep helpers constructive and cuboid-based. Do not add meshes, curves, boolean solids, or geometry that Vintage Story JSON cannot represent.
- [ ] Allow ordinary Python functions, loops, constants, and calculations inside a definition file so repeated geometry stays concise.
- [ ] Allow a model package to declare optional review-only assemblies and scenes without writing review metadata into runtime shape JSON.
- [ ] Give review metadata its own versioned generated manifest under `graphics/generated/model-review/`.

## Phase 4: implement animations in the same definition file

- [ ] Support shape animation fields used by Vintage Story 1.22.3:
  - [ ] Stable `name` and `code`.
  - [ ] `version` where required.
  - [ ] `quantityframes`.
  - [ ] `onActivityStopped`.
  - [ ] `onAnimationEnd`.
  - [ ] Ordered keyframes.
- [ ] Support per-element keyframe transforms:
  - [ ] `offsetX`, `offsetY`, and `offsetZ`.
  - [ ] `rotationX`, `rotationY`, and `rotationZ`.
  - [ ] Stretch fields only if confirmed to work in the target game version.
  - [ ] `rotShortestDistanceX/Y/Z`.
- [ ] Validate that animation targets belong to the same emitted shape.
- [ ] Reject duplicate animation codes, duplicate frame numbers, frames outside the declared range, and empty animations.
- [ ] Preserve omitted properties as omitted. Do not write zero values for every transform field unless Vintage Story requires them.
- [ ] Implement pose evaluation independently of JSON serialization so the photoshoot uses the same in-memory animation model as the compiler.
- [ ] Interpolate each animated property between its nearest surrounding keyframes.
- [ ] Implement shortest-angle interpolation per axis when its corresponding flag is enabled.
- [ ] Define behavior before the first keyframe and after the last keyframe, including loop and hold preview behavior.
- [ ] Test nested animated pivots, simultaneous parent and child animation, translation plus rotation, and fractional frames.

## Phase 5: compiler, validation, and deterministic output

- [ ] Load only checked-in model definitions beneath `graphics/models/` during the default all-model build.
- [ ] Give each definition a stable package identifier and explicit declared output paths.
- [ ] Reject absolute paths, parent traversal, outputs outside `assets/gearwright/` or `graphics/generated/`, and duplicate output ownership.
- [ ] Preserve declared element order and keyframe order so generated diffs follow source changes.
- [ ] Use a fixed JSON property order, two-space indentation, UTF-8 without BOM, and one final newline.
- [ ] Normalize generated texture locations to logical `domain:path` references.
- [ ] Validate texture aliases for missing targets and cycles.
- [ ] Validate all element names are unique within the lookup rules used by Vintage Story animations.
- [ ] Reject zero/negative cuboid dimensions except explicit non-rendering pivots.
- [ ] Reject invalid face names, malformed UVs, unsupported rotations, and non-finite values.
- [ ] Warn when opaque interior faces overlap exactly; allow an explicit suppression for intentional layering.
- [ ] Add a `validate` command that builds entirely in memory and writes nothing.
- [ ] Add a `build` command that supports all definitions, one package identifier, or one project-relative definition path.
- [ ] Write files only after the entire selected build validates. Avoid partially updating a multi-shape machine when a later shape fails.
- [ ] Write through a temporary file in the destination directory and atomically replace the previous generated file.
- [ ] Add a determinism test that builds the full model set twice into temporary directories and compares bytes.
- [ ] Add a stale-output check that fails when committed runtime JSON differs from freshly compiled output.

## Phase 6: integrate the graphics build

- [ ] Update `tools/graphics/Build-Graphics.ps1` to build texture recipes and Python model definitions.
- [ ] Keep the existing `-Recipe` behavior for texture recipes and add a clear `-Model` or `-Definition` selector for Python definitions.
- [ ] Preserve `-VintageStoryPath` and add `-PythonPath` without hard-coded installation paths.
- [ ] Build texture outputs before model validation when models reference generated Gearwright textures.
- [ ] Let logical references establish dependencies; do not rely on filename ordering for correctness.
- [ ] Remove JSON model recipes from default discovery only after every corresponding Python definition exists.
- [ ] Update `tools/Test-Project.ps1` so graphics compilation and Python tests run in normal developer verification.
- [ ] Keep packaging unchanged: package runtime assets only, never Python definitions, tests, requirements, or photoshoot output.

## Phase 7: replace the Blender photoshoot renderer

- [ ] Split the current `tools/vs_photoshoot.py` responsibilities before deleting Blender code:
  - [ ] Preserve permissive Vintage Story JSON loading.
  - [ ] Preserve mod folder/zip and base-game asset resolution.
  - [ ] Preserve logical texture resolution and strict missing-texture behavior.
  - [ ] Move pipe-specific composition into `graphics/models/fluid_pipe.py` review scenes.
  - [ ] Replace Blender scene construction with renderer-neutral scene geometry.
- [ ] Remove `subprocess` use that launches Blender.
- [ ] Remove `find_blender`, `invoke_blender`, `--blender`, `--inside-blender`, and `--save-blend`.
- [ ] Remove all `bpy` and `mathutils` imports and all Blender coordinate conversions.
- [ ] Establish one documented Vintage Story-to-renderer coordinate transform and cover it with front/right/top orientation tests.
- [ ] Convert each enabled cuboid face into two consistently wound triangles.
- [ ] Apply element transforms through the full parent hierarchy before scene placement.
- [ ] Clip triangles crossing the near camera plane.
- [ ] Implement a NumPy depth buffer and perspective-correct UV interpolation.
- [ ] Implement nearest-neighbor texture sampling by default so Vintage Story pixel art remains crisp.
- [ ] Keep optional linear sampling only if it remains useful for diagnosis.
- [ ] Implement opaque and cutout rendering first.
- [ ] Add a separate transparent pass sorted back-to-front with opaque-depth testing for glass and liquid.
- [ ] Define and document the alpha cutoff and transparent blending approximation.
- [ ] Support per-face glow with an unlit/emissive contribution.
- [ ] Use simple deterministic diagnostic lighting: ambient plus configurable key, fill, and rim directions.
- [ ] Use flat face normals; do not smooth cuboid edges.
- [ ] Add a configurable background and optional diagnostic ground plane.
- [ ] Add simple contact shadows only after geometry, depth, UV, and transparency tests pass. Do not block the first renderer milestone on photorealistic shadows.
- [ ] Preserve PNG and JPEG output; require PNG for transparency.
- [ ] Keep rendering deterministic for a fixed Python, NumPy, and Pillow dependency set.
- [ ] Add low-resolution renderer tests for:
  - [ ] Face orientation.
  - [ ] Nearer geometry hiding farther geometry.
  - [ ] Rotated parent/child elements.
  - [ ] UV orientation and face quarter turns.
  - [ ] Alpha cutout.
  - [ ] Transparent glass in front of opaque geometry.
  - [ ] Glow.
  - [ ] Non-square output.
- [ ] Compare the new renderer manually against several existing Blender photos, but judge geometry, UVs, legibility, and framing rather than pixel equality.

## Phase 8: animation photos

- [ ] Add `--animation CODE` and require an unambiguous target object when a scene contains several animated shapes.
- [ ] Add `--frames` accepting a comma-separated list and inclusive ranges such as `0,5,10-20:2,29`.
- [ ] Accept fractional frames for scrubbed machine animations.
- [ ] Add an optional normalized pose syntax for state-driven animations, for example `--progress 0.0`, `0.5`, or `1.0`.
- [ ] Render each selected frame and include the animation code and frame in the filename.
- [ ] Add `--contact-sheet` to assemble selected frames into one labeled PNG for quick review.
- [ ] Apply the animation pose before explosion and scene placement.
- [ ] Record animation code, exact frame, normalized progress, and interpolation mode in the manifest.
- [ ] Add acceptance renders for the passive pump at frames 0, 14.5, and 29.
- [ ] Verify the same frames in the real game because the photoshoot does not validate runtime animation startup, blending, or C# scrubbing.

## Phase 9: exploded and interior views

- [ ] Add named review assemblies to model definitions. An assembly may contain elements from one or more emitted shapes.
- [ ] Give each assembly an authored explosion vector in block coordinates and an optional magnitude multiplier.
- [ ] Add `--explode AMOUNT`, where `0` is assembled and `1` is the authored inspection distance.
- [ ] Move an assembly as one unit while preserving its internal hierarchy and current animation pose.
- [ ] Add a radial fallback for models without authored vectors, based on top-level assembly bounds relative to the model center.
- [ ] Emit a warning when fallback explosion is used so important models receive deliberate directions.
- [ ] Add repeatable `--hide NAME`, `--only NAME`, and `--ghost NAME=OPACITY` controls.
- [ ] Add an optional `--labels` overlay that draws stable assembly or element names with leader lines in inspection images.
- [ ] Recalculate camera framing after explosion unless the user supplied an explicit camera and target.
- [ ] Record explosion amount, vectors, hidden/soloed/ghosted groups, and label state in the manifest.
- [ ] Add passive pump review groups for body shell, intake, outlet, gauge/mechanism, sight glass, and liquid.
- [ ] Add pipe review groups for center frame, each face attachment, windows, liquid, sprinkler body, and rotor.

## Phase 10: block state, surroundings, and comparisons

- [ ] Generalize photoshoot from a single shape into a scene containing named placed objects.
- [ ] Support three target inputs:
  - [ ] A direct shape file or logical shape asset.
  - [ ] A block/item type asset plus state.
  - [ ] A named review scene from a Python model definition.
- [ ] Add repeatable `--state key=value` arguments for the target.
- [ ] For ordinary content JSON, resolve variant groups, wildcard `shapeByType`, texture mappings, and rotations using behavior verified against Vintage Story 1.22.3 assets.
- [ ] Do not pretend block-entity meshes can be inferred from block JSON. Use a model-defined state composer for dynamic Gearwright machines.
- [ ] Replace the hard-coded `--pipe-state` implementation with a state composer in `graphics/models/fluid_pipe.py`.
- [ ] Preserve the useful pipe states: empty, connection, window, sprinkler, pressure, visible liquid, and rotor visibility.
- [ ] Add repeatable neighbor placement such as `--place X,Y,Z=ASSET` with optional state, rotation, alias, ghosting, or comparison-only marking.
- [ ] Interpret neighbor positions in whole block coordinates while still allowing explicit fractional offsets for review scenes.
- [ ] Allow a definition file to declare named reusable scenes such as:
  - [ ] `fluid-pipe:active-window-sprinkler`.
  - [ ] `passive-fluid-pump:side-tank-active`.
  - [ ] `passive-fluid-pump:top-tank-active`.
  - [ ] `passive-fluid-pump:exploded`.
  - [ ] `sprinkler:item-vs-installed`.
- [ ] Allow neighbors to use base-game, Gearwright, or another supplied mod's block assets.
- [ ] Resolve every scene texture through the same strict resolver used for the target.
- [ ] Add a visible one-block grid or bounding box option for scale checks.
- [ ] Add an optional comparison layout that places two named scene variants side by side with the same camera scale.
- [ ] Include every object, asset code, state, transform, and role in the manifest using logical references only.

## Phase 11: camera and shot control

- [ ] Preserve named views: front, front-right, right, back-right, back, back-left, left, front-left, top, bottom, and isometric.
- [ ] Add an orbit form with azimuth, elevation, distance, and target.
- [ ] Add explicit camera position and target vectors.
- [ ] Add perspective field of view or focal length control.
- [ ] Add orthographic mode and explicit orthographic scale.
- [ ] Add camera roll.
- [ ] Accept `WIDTHxHEIGHT` output dimensions instead of requiring square images.
- [ ] Keep automatic framing with configurable padding.
- [ ] Add explicit framing modes for target only, all visible scene objects, or selected aliases.
- [ ] Allow scene definitions to provide useful default shots while command-line values override them.
- [ ] Add named lighting presets such as `studio`, `flat`, and `silhouette`, with explicit light directions available for debugging.
- [ ] Record the complete resolved camera and lighting configuration in the manifest.
- [ ] Ensure manifests never contain absolute game, mod, output, Python, or user paths.

## Phase 12: migrate the current models

- [ ] Migrate `example-workshop-marker.model.json` to `example_workshop_marker.py` as the smallest pipeline proof.
- [ ] Migrate `pottery-profile-tool.model.json` and retain its exact runtime asset code and texture mapping.
- [ ] Migrate all pipe model recipes into `fluid_pipe.py`:
  - [ ] `fluid-pipe-center.model.json`.
  - [ ] `fluid-pipe-arm.model.json`.
  - [ ] `fluid-pipe-cap.model.json`.
  - [ ] `fluid-pipe-window.model.json`.
  - [ ] `fluid-slug.model.json`.
  - [ ] `fluid-pipe-inventory.model.json`.
- [ ] Replace JSON base-shape composition in the inventory pipe with shared Python construction functions.
- [ ] Migrate all sprinkler recipes into `sprinkler.py`:
  - [ ] `sprinkler-body.model.json`.
  - [ ] `sprinkler-rotor.model.json`.
  - [ ] `sprinkler-brass.model.json`.
- [ ] Migrate `creative-fluid-pump.model.json`.
- [ ] Migrate the passive pump last because it proves the full system:
  - [ ] Recreate the 40-element static body from inspected generated JSON with readable dimension constants and helper functions.
  - [ ] Recreate intake and outlet sub-shapes.
  - [ ] Recreate liquid panes.
  - [ ] Recreate non-rendering animation pivots without disabled `#null` faces.
  - [ ] Recreate the pressure animation in the same Python file.
  - [ ] Replace `passive-fluid-pump-preview.model.json` with named review scenes rather than another generated preview shape.
- [ ] Compare each compiled result semantically against the frozen fixture.
- [ ] Permit intentional cleanup, such as removing exporter noise or `-0.0`, only when the runtime meaning and appearance remain unchanged.
- [ ] Update focused contract tests to inspect compiled JSON behavior rather than searching old recipe text.
- [ ] Test the migrated assets in Vintage Story before deleting the old source for that asset family.

## Phase 13: remove legacy JSON model authoring and Blender

- [ ] Delete every migrated `graphics/recipes/*.model.json` after its Python replacement passes.
- [ ] Keep `graphics/recipes/*.texture.json` and the texture builder.
- [ ] Delete `tools/graphics/Build-Model.ps1` after no active build path calls it.
- [ ] Remove model-recipe parsing, `source`, `base`, `parts`, primitive, edit, and animation handling from PowerShell once the Python compiler owns those features.
- [ ] Delete `graphics/blender/passive-fluid-pump.blend`.
- [ ] Delete `graphics/blender/passive-fluid-pump.blend1`.
- [ ] Delete `graphics/blender/exports/passive-fluid-pump.json`.
- [ ] Delete `graphics/blender/exports/passive-fluid-pump-liquid.json`.
- [ ] Delete `graphics/blender/exports/passive-fluid-pump-mechanism.json`.
- [ ] Delete both Blender README files and remove the empty `graphics/blender/` directory.
- [ ] Delete `tools/graphics/Enable-BlenderVintageStory52Compatibility.py`.
- [ ] Remove Blender-specific tests and replace them with compiler, animation, scene, and renderer tests.
- [ ] Remove live Blender references from `README.md`, `graphics/README.md`, `AGENTS.md`, scripts, and test messages.
- [ ] Keep historical CHANGELOG statements accurate; add a new entry explaining that Blender authoring and rendering were replaced. Do not rewrite prior release history as though Blender was never used.
- [ ] Run a repository search for `Blender`, `bpy`, `mathutils`, `.blend`, `--blender`, and `inside-blender`. Only intentional historical CHANGELOG text may remain.

## Phase 14: documentation for Luna and later agents

- [ ] Rewrite `graphics/README.md` around this workflow:
  - [ ] Find and inspect source textures.
  - [ ] Edit one Python model definition.
  - [ ] Build one model or all graphics.
  - [ ] Validate without writing.
  - [ ] Render named review scenes.
  - [ ] Render animation frames and contact sheets.
  - [ ] Use explosion, hide/only/ghost, neighbors, comparison, and custom camera controls.
  - [ ] Perform final in-game verification.
- [ ] Add a short, complete example model showing a box, hierarchy, explicit UV, loop-generated details, animation, explode group, and review scene.
- [ ] Document which JSON fields the compiler supports and which it deliberately rejects.
- [ ] Document the difference between model units, block units, UV pixels, animation offsets, and renderer coordinates.
- [ ] Document that generated shape JSON is reviewable output and must not be hand-edited.
- [ ] Document Python and dependency requirements without adding a setup wizard or installer.
- [ ] Update the root `README.md` repository layout to say `graphics/` contains Python model definitions, texture recipes, and ignored review output.
- [ ] Update `AGENTS.md` so future graphics work uses Python definitions, photoshoot, and in-game testing rather than Blender or direct model JSON editing.

## Phase 15: required verification and acceptance gates

- [ ] Run all Python unit tests.
- [ ] Run compiler validation for every model definition without writing.
- [ ] Build all textures and models twice; verify the second build changes no bytes.
- [ ] Run the stale-generated-output check.
- [ ] Render at least these review cases with strict textures:
  - [ ] Potter's Profile Tool from front-right and top.
  - [ ] Creative fluid pump beside a normal full block for scale.
  - [ ] Pipe with a closed face, connection, window, visible liquid, and downward sprinkler.
  - [ ] Pipe normal and exploded views.
  - [ ] Passive pump with side intake and comparison tank.
  - [ ] Passive pump with top intake and comparison tank.
  - [ ] Passive pump animation at frames 0, 14.5, and 29.
  - [ ] Passive pump exploded at frame 14.5 with the shell ghosted.
- [ ] Inspect each render for face orientation, missing faces, z-order errors, UV rotation, transparency, hierarchy pivots, and camera clipping.
- [ ] Verify the actual game loads every generated shape without errors.
- [ ] Verify pressure still scrubs the passive pump mechanism correctly in game.
- [ ] Verify dynamically composed pipe parts, liquid, and sprinkler still match the photoshoot scenes closely enough for diagnostic use.
- [ ] Run `tools/Test-Project.ps1 -RequireBuild`.
- [ ] Run `git diff --check`.
- [ ] Inspect `git status --short` and review every new binary. The expected implementation should not add model binaries.
- [ ] Run the privacy scan included by `tools/Test-Project.ps1` and confirm manifests contain no absolute paths.
- [ ] Run `tools/Install-Mod.ps1` and confirm the installed package matches the newest verified build byte for byte.

## Definition of done

- [ ] A developer can change geometry and animation by editing one Python file for the logical model family.
- [ ] `Build-Graphics.ps1` deterministically converts those definitions to committed Vintage Story JSON.
- [ ] Photoshoot runs with normal Python plus declared Python libraries and has no Blender dependency.
- [ ] Photoshoot can render selected animation frames, exploded/interior views, block state, neighboring blocks, comparison layouts, and custom cameras.
- [ ] The passive pump and pipe review cases work without hard-coded composition inside the generic photoshoot implementation.
- [ ] No active model depends on JSON authoring recipes or Blender exports.
- [ ] No `.blend` file, Blender helper, Blender renderer code, or Blender documentation remains in the active project.
- [ ] Runtime asset identifiers and animation codes are unchanged.
- [ ] All required repository checks, build, package, and active-mod installation steps pass.
