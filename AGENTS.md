# Agent instructions

## Scope and identity

Gearwright is a Vintage Story code mod about readable gear-driven machinery, workshop infrastructure, and steam as a heat-transfer technology. Keep gameplay code in `code/`, runtime assets in `assets/gearwright/`, tests in `tests/`, development scripts in `tools/`, full player documentation in the GitHub Wiki, and concise operating instructions in the in-game handbook.

The public identifiers `gearwright`, `gearwright:world-state`, registered class names, asset codes, and persisted field names are compatibility contracts. Do not rename or reuse them.

## Save compatibility is mandatory

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

## Repository privacy and hygiene

- Never commit absolute local paths, usernames, machine names, tokens, save files, logs, crash reports, local game DLLs, or generated build output.
- Discover installations from parameters, environment variables, or standard environment-relative locations. Do not add a developer-specific fallback path.
- Before delivery, run `tools/Test-Project.ps1`; its privacy scan must pass.
- Inspect `git diff --check` and the staged file list before committing. Treat every new binary or archive as suspicious unless intentionally reviewed.
- Do not copy code or assets from VintageGolem. It may be consulted for engineering lessons and Vintage Story API patterns only.

## Graphics workflow

- Prefer the deterministic tooling in `tools/graphics/` over GPT image generation. Create Vintage Story models from named cuboid primitives and build textures by cropping, tiling, tinting, rotating, and stitching inspected game or project assets.
- Store reusable work as logical, project-relative recipes in `graphics/recipes/`. Never put an absolute installation path or username in a recipe, model, texture, or provenance note.
- Search installed assets with `Find-GameAsset.ps1`, inspect every source before using it, and preserve Vintage Story's pixel density, palette, material cues, and visual language.
- Do not edit the game installation or commit unmodified game atlases. Make a meaningful new composition and keep logical source references in the recipe for reviewable provenance.
- Build and inspect generated output before delivery. Validate models in the Vintage Story model creator or in-game when their proportions, UVs, or rotations matter.
- Use GPT image generation only when primitives and existing assets cannot realistically produce the requested graphic, and document that reason before using it.

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
