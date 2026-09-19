# Agent instructions

## Scope and identity

Gearwright is a Vintage Story code mod about readable gear-driven machinery, workshop infrastructure, and steam as a heat-transfer technology. Keep gameplay code in `code/`, runtime assets in `assets/gearwright/`, tests in `tests/`, development scripts in `tools/`, full player documentation in the GitHub Wiki, and concise operating instructions in the in-game handbook.

The public identifiers `gearwright`, `gearwright:world-state`, registered class names, asset codes, and persisted field names are compatibility contracts. Do not rename or reuse them unless the maintainer explicitly authorizes a breaking change and names those identifiers or the containing subsystem as part of its scope.

## Save compatibility is mandatory by default

The maintainer may explicitly authorize a breaking change and define its scope. Within that scope, schemas, persisted fields, registered classes, and asset codes may be replaced or removed as requested. Keep unrelated saved state compatible, record the break and its affected identifiers in the changelog, and add tests that reject accidental breakage outside the authorized scope. Never infer permission for a breaking change from a large refactor or an early development version.

- Existing worlds must keep working across released versions, including pre-1.0 versions.
- Every persisted structure must carry a schema version. Add explicit, sequential, non-destructive migrations before changing its shape.
- Prefer additive changes and tolerant readers. Supply defaults for missing fields and preserve unrecognized fields when rewriting data.
- Never reinterpret a persisted field, numeric identifier, asset code, or enum value. Add a new value and migrate.
- Never silently reset malformed or newer data. Keep the original bytes untouched, disable writes for that document, log a clear error, and let the player recover or upgrade.
- Treat downgrades as read-only whenever the installed mod does not understand the stored schema.
- Add compatibility fixtures before changing serialization. Test loading the oldest supported fixture, migrating it, saving it, and loading it again.
- If a save-compatible implementation is genuinely impossible, stop and ask the maintainer. Do not ship a save break as a convenience.

## Multiplayer and simulation safety

- Prefer server-authoritative simulation. Clients may preview and request; the server validates positions, inventories, permissions, claims, recipes, and state transitions.
- Keep automation bounded, chunk-aware, rate-limited, and resumable after unload or restart.
- Commit an action atomically: reserve inputs, validate the destination, make the change, then persist the new stable state. Never duplicate or discard items during recovery.
- Use stable error/status codes and readable player-facing explanations. A blocked machine should wait with backoff rather than spin every tick.

## Player-facing writing

- Write all in-game text, handbook entries, and GitHub Wiki content in direct, plain language.
- Avoid AI writing tropes, including canned enthusiasm, inflated claims, repetitive summaries, unnecessary scene-setting, and formulaic contrasts such as "not just X, but Y."
- Write GitHub Wiki pages and handbook entries from the current code, assets, configuration, and maintainer direction.

## Environment and tool failures

- Do not use Computer Use or desktop UI automation in this project, including for Vintage Story and the model reviewer. Use repository tooling, SDK tests, logs and deterministic renders for agent verification; leave interactive in-game visual checks to the maintainer. This restriction takes precedence over any skill instructions to automate a desktop application.
- Tell the maintainer immediately when a required tool, dependency, permission, service, or environment capability is unavailable or fails.
- State what failed and what work remains affected. Do not silently switch to another tool, workflow, or workaround.
- Continue through a different method only after the maintainer explicitly asks or approves it.

## PowerShell execution for agents

- Run commands directly in the provided PowerShell session, with the repository as the working directory. Do not nest `powershell -Command`, `pwsh -Command`, `cmd /c`, or Bash around ordinary commands. For a suspected shell problem, inspect `$PSVersionTable.PSVersion`, `Get-ExecutionPolicy -List`, and `Get-Command <tool>` once; use the observed result instead of speculating about policy or PATH.
- Use literal single-quoted strings for paths and search expressions. Invoke a quoted executable or script with `&`. Use `apply_patch` for edits and checked-in scripts for substantial logic; avoid nested quoting, encoded commands, and inline Python programs. Never reuse automatic variables such as `$HOME`, `$PID`, or `$PROFILE`.
- PowerShell does not expand file wildcards for native tools. Search with `rg -n 'pattern' code/Hydraulics -g '*Pump*.cs'`, not `rg 'pattern' code/Hydraulics/*Pump*.cs`. Use `rg --files` before reading an uncertain filename. For `rg`, exit 1 means no matches; exit 2 means a command or access error.
- Check `$LASTEXITCODE` immediately after native tools. A successful trailing `Get-Content` does not prove an earlier build passed. Use `$ErrorActionPreference = 'Stop'` for scripts and throw on a failed required native command. For logs, capture the command's exit code before reading its tail and return that code afterward.
- A returned process/session ID means the command is still running. Resume that same session with the tool's polling facility; do not restart the command. Check a small log tail when useful, give progress updates, and avoid rapid polling. The interactive model reviewer intentionally stays running until its window closes; readiness is a loaded window, not process exit.
- Correct an agent-authored syntax, quoting, or filename mistake once within the same tool and workflow. This does not require permission. If the same error repeats, stop retrying, report the exact failure and affected work, and diagnose it with one small command. A real missing dependency, denied permission, or service failure still follows the environment-failure rule above; changing tools or policies requires approval.
- Do not change execution policy or add `-ExecutionPolicy Bypass` to make a command run. If graphics wrappers are blocked, the direct Python reviewer command below is already authorized. If a required build/check is blocked, report that specific blocker and request the needed permission. Filesystem approval and PowerShell execution policy are separate controls.

## Repository privacy and hygiene

- Never commit absolute local paths, usernames, machine names, tokens, save files, logs, crash reports, local game DLLs, or generated build output.
- Discover installations from parameters, environment variables, or standard environment-relative locations. Do not add a developer-specific fallback path.
- Before delivery, run `tools/Test-Project.ps1`; its privacy scan must pass.
- Inspect `git diff --check` and the staged file list before committing. Treat every new binary or archive as suspicious unless intentionally reviewed.
- Do not copy code or assets from VintageGolem. It may be consulted for engineering lessons and Vintage Story API patterns only.

## Graphics workflow

- If PowerShell execution is disabled, launch the desktop reviewer directly with `python -m gearwright_graphics.review_model` after setting `PYTHONPATH`; do not add a shell wrapper.
- Use the `python` command for all graphics development commands. It must be available to agents; wrappers accept `-PythonPath` or `GEARWRIGHT_PYTHON` only as discovery fallbacks and never install Python or dependencies.
- Reusable Codex guidance for this workflow is in `skills/vintage-story-modeling/SKILL.md`; read it when authoring or reviewing Vintage Story models and animations.
- Prefer the deterministic tooling in `tools/graphics/` over GPT image generation. Edit checked-in Python definitions in `graphics/models/` to create Vintage Story cuboids, animations, and review scenes. Keep textures in `graphics/recipes/` and build them by cropping, tiling, tinting, rotating, and stitching inspected game or project assets.
- Run `python -m unittest discover tests/graphics` before delivery. Use `Build-Graphics.ps1` for the same model compiler used by packages, and use `tools/vs_photoshoot.py` for CPU review renders.
- Use `python -m gearwright_graphics.cli inventory --root .` to audit runtime shape ownership. Keep reusable definitions in `graphics/models/`; keep human decision packages under `generated/<feature>-review/`; use `Clear-GraphicsArtifacts.ps1` for scoped cache, photoshoot, or review cleanup.
- Keep named review-only workflow fixtures in `graphics/review/` with one declared managed path under `generated/`. They must never emit into runtime assets or create a new revision directory on every run.
- Never hand-edit generated shape JSON or put an absolute installation path or username in a model, texture, manifest, or provenance note.
- Search installed assets with `Find-GameAsset.ps1`, inspect every source before using it, and preserve Vintage Story's pixel density, palette, material cues, and visual language.
- Do not edit the game installation or commit unmodified game atlases. Make a meaningful new composition and keep logical source references in the recipe for reviewable provenance.
- Build and inspect generated output before delivery. Validate models in the Vintage Story model creator or in-game when their proportions, UVs, or rotations matter; the photoshoot is diagnostic, not engine-identical.
- Use GPT image generation only when primitives and existing assets cannot realistically produce the requested graphic, and document that reason before using it.
- Approval-first model workflow: before implementing a new model or animation in `graphics/models/` or runtime assets, create a few clearly labeled candidates under the ignored root `generated/<feature>-review/`, render them with the photoshoot tool, and ask the maintainer to choose or revise one. Do not implement the feature in the mod until the maintainer approves a candidate.
- When presenting design candidates, explicitly describe the meaningful differences between the options and what each option is intended to test; do not rely on images alone.

## Development conventions

- Target the Vintage Story version declared in `modinfo.json`.
- Keep the mod ID and assembly name stable. Package runtime content only; documentation and tooling stay outside the mod archive.
- Add or update both the focused GitHub Wiki page and the relevant in-game handbook entries with every player-visible change.
- Keep project tooling developer-facing. Do not add double-click wrappers, source-download workflows, contribution-sync helpers, or tool installers.
- Prefer a focused, checked-in PowerShell tool in `tools/` whenever verification would otherwise require a long inline shell command. Invoke the tool with short parameters so the workflow is reviewable and repeatable.
- Do not add an in-repository documentation site. The GitHub Wiki is the canonical home for full player documentation; the in-game handbook should carry the concise instructions needed while playing.
- Release tags use `v<version>` and must match `modinfo.json`. The release workflow must build against the declared Vintage Story version and use the repository `GITHUB_TOKEN`; do not add a personal release token.
- A change is not complete until the project checks pass and, when the game SDK is available, the mod builds, packages, and the newest package is installed in the active Vintage Story Mods folder with `tools/Install-Mod.ps1`.

## Active development installation

- Before delivering a completed change, run `tools/Test-Project.ps1 -RequireBuild`, then run `tools/Install-Mod.ps1` so the active Mods folder contains the newest verified Gearwright package.
- Let the installer resolve the data folder from `-VintageStoryDataPath`, `VINTAGE_STORY_DATA`, or the standard `%APPDATA%`-relative location. Never hard-code, print into tracked output, or commit the resolved absolute game or data path.
- Keep only one enabled `Gearwright_*.zip`. The installer moves older versions into the recoverable `Mods\Disabled` folder and verifies that the installed archive matches the build byte for byte.
- If the SDK or active data folder cannot be discovered safely, report that installation was skipped and why; do not add a developer-specific path to make it work.
