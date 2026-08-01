# Gearwright

Gearwright is a Vintage Story code mod for gear-driven workshop machinery, material handling, and limited automation. Steam systems move heat rather than serving as compact power generators.

Gearwright targets Vintage Story 1.22.3. Version 0.1.0 contains the `/gearwright` status command and a creative-only Potter's Profile Tool. No survival machinery is implemented yet.

## Development requirements

- Vintage Story 1.22.3 with `VintagestoryAPI.dll`
- .NET 10 SDK
- PowerShell

The scripts discover Vintage Story from `-VintageStoryPath`, `VINTAGE_STORY`, or the standard per-user installation folders.

## Build and test

```powershell
.\tools\Build-Mod.ps1
.\tools\Test-Project.ps1 -RequireBuild
.\tools\Install-Mod.ps1
```

`Build-Mod.ps1` writes the package to `dist/`. `Test-Project.ps1` checks repository structure, privacy rules, graphics recipes, save migrations, downgrade protection, and the release build. `Install-Mod.ps1` installs the newest package and verifies its hash.

## Release

The release workflow runs when a `v<version>` tag is pushed. The tag must match the version in `modinfo.json`.

Before releasing, update the version in `modinfo.json`, `Gearwright.csproj`, and `GearwrightModSystem.ModVersion`, update `CHANGELOG.md`, then run the full checks and commit the result. Create and push the tag:

```powershell
git tag -a v0.1.0 -m "Gearwright 0.1.0"
git push origin v0.1.0
```

GitHub Actions downloads the matching official Vintage Story server package, runs the project checks and compatibility contracts, builds the mod with the committed runtime graphics, verifies the zip contents, writes a SHA-256 checksum, and attaches both files to a GitHub Release. Local completion checks still rebuild graphics from the full game installation.

## Repository layout

- `code/`: gameplay and storage code
- `assets/gearwright/`: packaged runtime assets
- `tests/`: repository and save-compatibility checks
- `tools/`: build, test, installation, and graphics scripts
- `graphics/`: model and texture recipes
- `.github/workflows/release.yml`: tag-driven GitHub release pipeline

Read [COMPATIBILITY.md](COMPATIBILITY.md) before changing persisted data or public identifiers. Player documentation belongs in the [GitHub Wiki](https://github.com/ESmink/Gearwright/wiki).

## License

Gearwright code and original assets use the [Apache License 2.0](LICENSE). Vintage Story and its assets belong to their respective owners.
