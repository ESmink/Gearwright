# Gearwright

Gearwright is a Vintage Story code mod for gear-driven workshop machinery, material handling, and limited automation. Steam systems move heat rather than serving as compact power generators.

Gearwright targets Vintage Story 1.22.3. Available equipment includes fluid pipes and fittings, irrigation, shaft-driven pumps and bellows, a Small Flywheel, an Overrunning Transmission, and pneumatic item transport with accumulators, glass tubes, senders, receivers and filtering routers. The Potter's Profile Tool is a creative placeholder; the old Gravity Drain is deprecated.

See the [equipment catalogue](https://github.com/ESmink/Gearwright/wiki/Items-and-Blocks) and [pneumatic transport guide](https://github.com/ESmink/Gearwright/wiki/Pneumatic-Item-Transport) for player instructions. The sections below cover development and releases.

## Development requirements

- Vintage Story 1.22.3 with `VintagestoryAPI.dll`
- .NET 10 SDK
- PowerShell
- Python 3.11 or newer on the `python` command, with NumPy 2.x, Pillow 10-12, and PySide6 Essentials for graphics development

The scripts discover Vintage Story from `-VintageStoryPath`, `VINTAGE_STORY`, or the standard per-user installation folders.

## Build and test

```powershell
.\tools\Build-Mod.ps1
.\tools\Test-Project.ps1 -RequireBuild
.\tools\Install-Mod.ps1
.\tools\Test-ServerSmoke.ps1
```

`Build-Mod.ps1` writes the package to `dist/`. `Test-Project.ps1` checks repository structure, privacy rules, graphics recipes, save migrations, downgrade protection, and the release build. Windows compatibility tests require the Windows game or server SDK, including `Lib/e_sqlite3.dll`; the test build copies that native library into its output folder. `Install-Mod.ps1` installs the newest package and verifies its hash. `Test-ServerSmoke.ps1` boots the package with an isolated temporary data folder, waits for `WorldReady`, checks the logs, and removes the temporary world.

`Inspect-BlockBehavior.ps1 -TypeName Vintagestory.API.Common.Block -MethodName GetRetention` inspects the installed SDK's block and room hooks without storing engine code or binaries in the repository. Use `-ListMembers` to find methods before selecting one.

## Release

The release workflow runs for pushes to `main` and for `v<version>` tags. An untagged `main` commit updates the rolling `indev` prerelease and its checksum. If the matching version tag already points to the commit, the indev build is skipped.

The `indev` tag and release are replaced in place, so they always point to the newest verified development build. Tagged releases remain permanent, and their tag must match the version in `modinfo.json`.

Before releasing, update the version in `modinfo.json`, `Gearwright.csproj`, and `GearwrightModSystem.ModVersion`, update `CHANGELOG.md`, then run the full checks and commit the result. Create and push the tag:

```powershell
git tag -a v0.1.0 -m "Gearwright 0.1.0"
git push origin v0.1.0
```

GitHub Actions runs on Windows and downloads the matching official Windows Vintage Story server ZIP. It verifies that the SDK includes native SQLite, runs the project checks and compatibility contracts, builds the mod with the committed runtime graphics, verifies the zip contents, writes a SHA-256 checksum, and attaches both files to a GitHub Release. Local completion checks still rebuild graphics from the full game installation.

## Repository layout

- `code/`: gameplay and storage code
- `code/Air/README.md`: air-delivery API notes for developers
- `assets/gearwright/`: packaged runtime assets
- `tests/`: repository and save-compatibility checks
- `tools/`: build, test, installation, and graphics scripts
- `graphics/`: Python model definitions, deterministic texture recipes, review-only workflow fixtures, and ignored review output
- `.github/workflows/release.yml`: rolling indev and tag-driven GitHub release pipeline

Read [COMPATIBILITY.md](COMPATIBILITY.md) before changing persisted data or public identifiers. Full player documentation belongs in the [GitHub Wiki](https://github.com/ESmink/Gearwright/wiki), with concise operating instructions kept in the in-game handbook.

## License

Gearwright code and original assets use the [Apache License 2.0](LICENSE). Third-party sound effects are listed in the packaged [sound attribution](assets/gearwright/sounds/ATTRIBUTION.md) and remain under the Pixabay Content License. Vintage Story and its assets belong to their respective owners.
