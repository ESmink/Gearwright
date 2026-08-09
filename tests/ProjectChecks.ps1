Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$failures = [Collections.Generic.List[string]]::new()

function Assert-Project {
    param([bool]$Condition, [string]$Message)
    if ($Condition) {
        Write-Host "[PASS] $Message" -ForegroundColor Green
    } else {
        Write-Host "[FAIL] $Message" -ForegroundColor Red
        $script:failures.Add($Message)
    }
}

$required = @(
    "AGENTS.md", "README.md", "CHANGELOG.md", "COMPATIBILITY.md", "LICENSE",
    "modinfo.json", "Gearwright.csproj", ".github\workflows\release.yml",
    "tests\Gearwright.Contracts\Gearwright.Contracts.csproj", "tests\Gearwright.Contracts\Program.cs",
    "tests\Gearwright.Contracts\fixtures\hydraulic-pipe-schema1.json",
    "tests\Gearwright.Contracts\fixtures\hydraulic-pump-schema1.json",
    "graphics\README.md", "graphics\review\README.md", "graphics\review\slingshot_workflow.py",
    "skills\vintage-story-modeling\SKILL.md", "skills\vintage-story-modeling\agents\openai.yaml",
    "graphics\models\pottery_profile_tool.py", "graphics\models\fluid_pipe.py",
    "graphics\models\sprinkler.py", "graphics\models\creative_fluid_pump.py",
    "graphics\models\passive_fluid_pump.py", "graphics\recipes\pottery-profile-tool.texture.json",
    "graphics\recipes\inspection-glass.texture.json", "graphics\recipes\inspection-shadow.texture.json",
    "tools\graphics\gearwright_graphics\model.py", "tools\graphics\gearwright_graphics\animation.py",
    "tools\graphics\gearwright_graphics\compiler.py", "tools\graphics\gearwright_graphics\assets.py",
    "tools\graphics\gearwright_graphics\blocks.py", "tools\graphics\gearwright_graphics\scene.py",
    "tools\graphics\gearwright_graphics\geometry.py", "tools\graphics\gearwright_graphics\raster.py",
    "tools\graphics\gearwright_graphics\materials.py", "tools\graphics\gearwright_graphics\photoshoot.py",
    "tools\graphics\gearwright_graphics\reviewer.py", "tools\graphics\gearwright_graphics\live_view.py",
    "tools\graphics\gearwright_graphics\review_model.py", "tools\graphics\gearwright_graphics\cli.py",
    "tools\graphics\requirements.txt", "tools\graphics\Invoke-Photoshoot.ps1",
    "tools\graphics\Review-Model.ps1", "tools\graphics\Clear-GraphicsArtifacts.ps1",
    "tests\graphics\test_pipeline.py", "tests\graphics\test_reviewer.py", "tests\graphics\test_workflow_review.py",
    "tests\graphics\fixtures\legacy-model-semantics.json",
    "assets\gearwright\itemtypes\pottery-profile-tool.json",
    "assets\gearwright\itemtypes\sprinkler-brass.json",
    "assets\gearwright\blocktypes\fluid-pipe-copper.json",
    "assets\gearwright\blocktypes\creative-fluid-pump.json",
    "assets\gearwright\blocktypes\passive-fluid-pump.json",
    "assets\gearwright\shapes\block\passive-fluid-pump-liquid.json",
    "assets\gearwright\shapes\block\passive-fluid-pump-mechanism.json",
    "assets\gearwright\shapes\block\passive-fluid-pump-intake.json",
    "assets\gearwright\shapes\block\passive-fluid-pump-outlet.json",
    "assets\gearwright\shapes\block\fluid-pipe-cap.json",
    "assets\gearwright\shapes\block\fluid-pipe-inventory.json",
    "assets\gearwright\textures\block\inspection-glass.png",
    "assets\gearwright\textures\block\inspection-shadow.png",
    "assets\gearwright\shapes\item\pottery-profile-tool.json",
    "assets\gearwright\textures\item\pottery-profile-tool.png",
    "code\Hydraulics\ScrollingLiquidSurface.cs", "code\Hydraulics\PassiveFluidPumpRenderer.cs",
    "tools\Common.ps1", "tools\Build-Mod.ps1", "tools\Test-Project.ps1", "tools\Install-Mod.ps1",
    "tools\Test-ServerSmoke.ps1",
    "tools\graphics\Build-Graphics.ps1",
    "tools\graphics\Build-Texture.ps1", "tools\graphics\Find-GameAsset.ps1",
    "tools\graphics\Graphics.Common.ps1",
    "tools\vs_photoshoot.py"
)
foreach ($relative in $required) {
    Assert-Project (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf) "Required file exists: $relative"
}

$removed = @(
    "CONTRIBUTING.md", "installation.md", "docs",
    "graphics\recipes\wiki-graphics-workflow.texture.json",
    "graphics\recipes\wiki-pottery-profile-tool.texture.json",
    "PYTHON_MODEL_PIPELINE_CHECKLIST.md",
    "tools\graphics\Build-Model.ps1",
    "tools\graphics\Enable-LegacyGraphicsCompatibility.py"
)
foreach ($relative in $removed) {
    Assert-Project (-not (Test-Path -LiteralPath (Join-Path $root $relative))) "Obsolete access file is absent: $relative"
}
Assert-Project (@(Get-ChildItem $root -Recurse -Filter "*.cmd" -File).Count -eq 0) "No double-click command wrappers remain"

$allowedToolScripts = @(
    "Build-Mod.ps1", "Common.ps1", "Install-Mod.ps1", "Test-Project.ps1", "Test-ServerSmoke.ps1",
    "graphics\Build-Graphics.ps1",
    "graphics\Build-Texture.ps1", "graphics\Find-GameAsset.ps1", "graphics\Graphics.Common.ps1",
    "graphics\Invoke-Photoshoot.ps1", "graphics\Review-Model.ps1",
    "graphics\Clear-GraphicsArtifacts.ps1"
)
$toolScripts = Get-ChildItem (Join-Path $root "tools") -Recurse -Filter "*.ps1" -File | ForEach-Object {
    $_.FullName.Substring((Join-Path $root "tools").Length + 1)
}
$unexpectedToolScripts = @($toolScripts | Where-Object { $allowedToolScripts -notcontains $_ })
Assert-Project ($unexpectedToolScripts.Count -eq 0) "Only development and build scripts remain"
foreach ($unexpected in $unexpectedToolScripts) { Write-Host "       $unexpected" -ForegroundColor Red }

$modInfo = Get-Content -Raw (Join-Path $root "modinfo.json") | ConvertFrom-Json
$projectText = Get-Content -Raw (Join-Path $root "Gearwright.csproj")
$codeText = Get-Content -Raw (Join-Path $root "code\GearwrightModSystem.cs")
Assert-Project ($modInfo.modid -ceq "gearwright") "The permanent mod ID is gearwright"
Assert-Project ($modInfo.authors.Count -eq 1 -and $modInfo.authors[0] -ceq "kingedwin") "Public mod author is kingedwin"
Assert-Project ($projectText -match "<Version>$([regex]::Escape($modInfo.version))</Version>") "Project and mod versions match"
Assert-Project ($codeText -match "ModVersion\s*=\s*`"$([regex]::Escape($modInfo.version))`"") "Code and mod versions match"
Assert-Project ($codeText -match 'WorldStateStorageKey\s*=\s*"gearwright:world-state"') "The permanent world-state key is unchanged"
Assert-Project ($codeText -match 'ChatCommands\.Create\("gearwright"\)') "The status command is registered"
Assert-Project ($codeText -match 'RequiresPrivilege\(Privilege\.chat\)') "The status command is available to chat users"

$agentText = Get-Content -Raw (Join-Path $root "AGENTS.md")
Assert-Project ($agentText -match 'Prefer the deterministic tooling in `tools/graphics/` over GPT image generation') "Agent guidance prefers reproducible graphics"
Assert-Project ($agentText -match 'direct, plain language' -and $agentText -match 'Avoid AI writing tropes') "Agent guidance requires direct player-facing writing"
Assert-Project ($agentText -match 'tools/Install-Mod\.ps1' -and $agentText -match 'newest verified Gearwright package') "Agent guidance requires installing the verified build"
Assert-Project ($agentText -match 'both the focused GitHub Wiki page and the relevant in-game handbook entries') "Agent guidance requires current wiki and handbook documentation"
Assert-Project ($agentText -match 'checked-in PowerShell tool in `tools/`' -and $agentText -match 'reviewable and repeatable') "Agent guidance requires reviewable scripts instead of long inline commands"
$modelingSkillText = Get-Content -Raw (Join-Path $root "skills\vintage-story-modeling\SKILL.md")
Assert-Project ($modelingSkillText -match 'one fixed `current/` path' -and $modelingSkillText -match 'runtimePromotion: false') "The modeling skill closes review decisions without accumulating revisions or promoting fixtures"
Assert-Project ($modelingSkillText -match 'camera movement must never launch the photoshoot renderer or create PNG caches') "The modeling skill protects the persistent interactive reviewer"

$readmeText = Get-Content -Raw (Join-Path $root "README.md")
Assert-Project ($readmeText -match 'https://github\.com/ESmink/Gearwright/wiki') "README links to the GitHub Wiki"
$installerText = Get-Content -Raw (Join-Path $root "tools\Install-Mod.ps1")
Assert-Project ($installerText -match 'Find-VintageStoryData' -and $installerText -match 'Get-FileHash') "The installer discovers the data folder and verifies the package"
$smokeTestText = Get-Content -Raw (Join-Path $root "tools\Test-ServerSmoke.ps1")
Assert-Project ($smokeTestText -match 'Find-VintageStoryInstall' -and $smokeTestText -match 'Entering runphase WorldReady' -and $smokeTestText -match 'smoke-') "The server smoke test uses a discovered install and an isolated temporary world"
$releaseWorkflowText = Get-Content -Raw (Join-Path $root ".github\workflows\release.yml")
Assert-Project ($releaseWorkflowText -match 'tags:\s*\r?\n\s*- "v\*"' -and $releaseWorkflowText -match 'contents: write') "Release workflow runs on version tags with release permission"
Assert-Project ($releaseWorkflowText -match 'branches:\s*\r?\n\s*- "main"' -and $releaseWorkflowText -match 'git rev-list -n 1 \$versionTag' -and $releaseWorkflowText -match 'releaseTag = "indev"') "Release workflow publishes untagged main commits as indev builds"
Assert-Project ($releaseWorkflowText -match 'vs_server_linux-x64_\$gameVersion\.tar\.gz' -and $releaseWorkflowText -match 'Test-Project\.ps1 -RequireBuild -SkipGraphicsBuild') "Release workflow builds against the declared Vintage Story version"
Assert-Project ($releaseWorkflowText -match 'gh release create' -and $releaseWorkflowText -match '--verify-tag' -and $releaseWorkflowText -match 'Get-FileHash -Algorithm SHA256') "Release workflow verifies and publishes the package"
Assert-Project ($releaseWorkflowText -match 'git fetch --force --tags origin' -and $releaseWorkflowText -match 'git push --force origin' -and $releaseWorkflowText -match 'gh release upload' -and $releaseWorkflowText -match '--clobber' -and $releaseWorkflowText -match '--prerelease' -and $releaseWorkflowText -match '--latest=false') "Release workflow rechecks tags and replaces the rolling indev prerelease"
Assert-Project ($releaseWorkflowText.Contains('GH_TOKEN: ${{ github.token }}') -and $releaseWorkflowText -notmatch 'secrets\.') "Release workflow uses the repository token"

$profileItem = Get-Content -Raw (Join-Path $root "assets\gearwright\itemtypes\pottery-profile-tool.json") | ConvertFrom-Json
Assert-Project ($profileItem.code -ceq "pottery-profile-tool") "The pottery profile tool keeps its asset code"
Assert-Project ($profileItem.shape.base -ceq "gearwright:item/pottery-profile-tool") "The pottery profile tool keeps its generated shape"

$handbookAssets = @(
    "assets\gearwright\itemtypes\pottery-profile-tool.json",
    "assets\gearwright\itemtypes\sprinkler-brass.json",
    "assets\gearwright\blocktypes\fluid-pipe-copper.json",
    "assets\gearwright\blocktypes\creative-fluid-pump.json",
    "assets\gearwright\blocktypes\passive-fluid-pump.json"
)
foreach ($relative in $handbookAssets) {
    $asset = Get-Content -Raw (Join-Path $root $relative) | ConvertFrom-Json
    Assert-Project ($null -ne $asset.attributes.handbook -and $asset.attributes.handbook.include) "Player asset has a handbook entry: $relative"
}
$languageText = Get-Content -Raw (Join-Path $root "assets\gearwright\lang\en.json")
foreach ($key in @(
    "handbook-text-pottery-profile-tool", "handbook-text-fluid-pipe-copper",
    "handbook-text-sprinkler-brass", "handbook-text-creative-fluid-pump",
    "handbook-text-passive-fluid-pump"
)) {
    Assert-Project ($languageText -match [regex]::Escape('"' + $key + '"')) "Handbook text exists: $key"
}

$creativePumpDialogText = Get-Content -Raw (Join-Path $root "code\Hydraulics\GuiDialogCreativeFluidPump.cs")
Assert-Project (
    $creativePumpDialogText -match 'background\.BothSizing\s*=\s*ElementSizing\.FitToChildren' -and
    $creativePumpDialogText -match 'background\.WithChildren\('
) "Creative pump dialog gives its autosized background fixed child bounds"

$pipeBlockText = Get-Content -Raw (Join-Path $root "code\Hydraulics\BlockFluidPipe.cs")
Assert-Project ($pipeBlockText -match 'game", "glass-plain' -and $pipeBlockText -notmatch 'glasspane') "Pipe inspection windows consume plain glass blocks"
Assert-Project ($pipeBlockText -match 'changed && !removing') "Removing a pipe attachment does not consume the returned item"
Assert-Project (
    $pipeBlockText -match 'GetSelectionBoxes' -and
    $pipeBlockText -match 'GetCollisionBoxes' -and
    $pipeBlockText -match 'pipe\.IsConnected\(face\)' -and
    $pipeBlockText -match 'pipe\.HasSprinkler'
) "Pipe hitboxes follow connected arms and the installed sprinkler"
Assert-Project ($pipeBlockText -match 'fluid-pipe-inventory\.json') "The pipe uses its glazed elbow inventory model"
$pipeRendererText = Get-Content -Raw (Join-Path $root "code\Hydraulics\HydraulicPipeRenderer.cs")
$scrollingSurfaceText = Get-Content -Raw (Join-Path $root "code\Hydraulics\ScrollingLiquidSurface.cs")
Assert-Project ($pipeRendererText -match 'ContainerTextureSource' -and $pipeRendererText -notmatch '\.RuntimeBake\(') "Pipe liquid rendering uses Vintage Story's container texture source"
$pipeCenterModelText = Get-Content -Raw (Join-Path $root "graphics\models\fluid_pipe.py")
$pipeWindowModelText = $pipeCenterModelText
Assert-Project ($pipeCenterModelText -match 'frame-x-' -and $pipeCenterModelText -notmatch 'hub-band') "The pipe center is a flush copper frame without overlapping hub bands"
Assert-Project ($pipeCenterModelText -match 'half-coupling-' -and $pipeCenterModelText -notmatch 'inner-collar') "Pipe connections form one coupling from two flush half-collars"
Assert-Project (
    $pipeWindowModelText -match '9\.5, 9\.5, 6\.5' -and
    $pipeWindowModelText -match 'gearwright:block/inspection-glass' -and
    $pipeWindowModelText -match 'faces=\("north",\)' -and
    $pipeWindowModelText -notmatch 'window-neck'
) "Inspection glass is visible, outward-facing, flush, and matches the inside pipe-wall depth"
Assert-Project (
    $pipeRendererText -match 'CurrentFlowDirection' -and
    $pipeRendererText -match 'GetWindowScroll' -and
    $pipeRendererText -match '47\.96f' -and
    $pipeRendererText -match '!scroll\.Reverse' -and
    $pipeRendererText -match 'UpdateSpeedVariation' -and
    $scrollingSurfaceText -match 'UpdateMesh' -and
    $scrollingSurfaceText -match 'WriteSurface'
) "Visible liquid scrolls stationary wrapped UVs in the corrected direction at a strongly pressure-scaled speed"
$sprinklerModelText = Get-Content -Raw (Join-Path $root "graphics\models\sprinkler.py")
Assert-Project (
    $sprinklerModelText -match 'rotor-hub' -and
    $sprinklerModelText -match 'arm-north' -and
    $sprinklerModelText -notmatch 'arm-ns|arm-ew' -and
    $pipeRendererText -match '60 \+ 240 \* performance' -and
    $pipeRendererText -match 'SpawnJet' -and
    $pipeRendererText -match 'EnumParticleModel\.Cube'
) "The Python sprinkler definition separates its rotor hub and arms from the stationary pin"
$gravityDrain = Get-Content -Raw (Join-Path $root "assets\gearwright\blocktypes\passive-fluid-pump.json") | ConvertFrom-Json
Assert-Project ($gravityDrain.sidesolid.up -eq $true) "The gravity drain supports a tank on its top face"
$gravityDrainCode = Get-Content -Raw (Join-Path $root "code\Hydraulics\BlockEntityPassiveFluidPump.cs")
Assert-Project (
    $gravityDrainCode -match 'CanConnect\(BlockFacing face\) => face == facing' -and
    $gravityDrainCode -match 'HasTank\(facing\.Opposite\)' -and
    $gravityDrainCode -match 'HasTank\(BlockFacing\.UP\)' -and
    $gravityDrainCode -match 'TrySource\(FindIntakeFace\(\)\)'
) "Gravity drains give their single drawn side intake priority before rotating it upward"
Assert-Project ($gravityDrainCode -match 'passive-fluid-pump\.json' -and $gravityDrainCode -match 'return true;') "The gravity drain explicitly rotates and contributes its base block mesh"
$gravityDrainModel = Get-Content -Raw (Join-Path $root "graphics\models\passive_fluid_pump.py")
$gravityDrainShapeText = Get-Content -Raw (Join-Path $root "assets\gearwright\shapes\block\passive-fluid-pump.json")
Assert-Project (
    $gravityDrainModel -match 'def _body' -and
    $gravityDrainModel -match 'gearwright:block/inspection-glass' -and
    $gravityDrainShapeText -match '"name":\s+"flat-deck"' -and
    $gravityDrainShapeText -match '"name":\s+"flow-bed"' -and
    $gravityDrainShapeText -match '"name":\s+"flow-cap"' -and
    $gravityDrainShapeText -match '"name":\s+"sight-glass-front"' -and
    $gravityDrainShapeText -match '"name":\s+"gauge-face"' -and
    $gravityDrainShapeText -match '"name":\s+"gauge-mark-6"' -and
    $gravityDrainShapeText -match '"name":\s+"pressure-guide-cap"' -and
    $gravityDrainShapeText -notmatch 'basin|paddle|wheel|rocker'
) "The Python gravity-drain definition keeps its flat tank support around a clear direct-flow window, gauge, and pressure guide"
Assert-Project ($gravityDrainModel -match 'intake-tube-bottom' -and $gravityDrainModel -match 'intake-shadow' -and $gravityDrainModel -match '18\.5' -and $gravityDrainModel -match 'outlet-half-coupling-') "The Python definition retains the rotatable intake and hollow outlet"
$gravityRendererText = Get-Content -Raw (Join-Path $root "code\Hydraulics\PassiveFluidPumpRenderer.cs")
$gravityMechanism = Get-Content -Raw (Join-Path $root "assets\gearwright\shapes\block\passive-fluid-pump-mechanism.json") | ConvertFrom-Json
$pressureAnimation = @($gravityMechanism.animations | Where-Object { $_.code -ceq "pressure" })[0]
$idlePressureFrame = @($pressureAnimation.keyframes | Where-Object { $_.frame -eq 0 })[0]
$fullPressureFrame = @($pressureAnimation.keyframes | Where-Object { $_.frame -eq 29 })[0]
Assert-Project (
    $gravityRendererText -match 'PumpOffer localOffer' -and
    $gravityRendererText -match 'hasInputPressure = localOffer\.Pressure > 0' -and
    $gravityRendererText -match 'NetworkStatusCode == "running"' -and
    $gravityRendererText -match 'AnimationUtil' -and
    $gravityRendererText -match 'passive-fluid-pump-mechanism\.json' -and
    $gravityRendererText -match 'pressureAnimation\.CurrentFrame' -and
    $gravityRendererText -match 'ScrollingLiquidSurface' -and
    $gravityRendererText -match 'horizontal: true, reverse: false' -and
    $gravityRendererText -match 'horizontal: true, reverse: true' -and
    $gravityRendererText -notmatch 'PressureBasinSurface|wheel|rocker|paddle' -and
    $pressureAnimation.quantityframes -eq 30 -and
    [double]$idlePressureFrame.elements.'b_gauge-needle'.rotationX -eq -52 -and
    [double]$fullPressureFrame.elements.'b_gauge-needle'.rotationX -eq 52 -and
    [double]$fullPressureFrame.elements.'b_pressure-plunger'.offsetY -eq 0.85
) "Input pressure still scrubs the gauge and plunger while active flow uses the existing liquid renderer"
$pressureFixture = Get-Content -Raw (Join-Path $root "tests\graphics\fixtures\passive-pump-pressure.json") | ConvertFrom-Json
Assert-Project ($pressureFixture.animation -ceq "pressure" -and $pressureFixture.quantityframes -eq 30 -and $pressureFixture.frames.Count -eq 3) "Pressure animation review fixture covers idle, fractional, and full frames"
$graphicsBuilderText = Get-Content -Raw (Join-Path $root "tools\graphics\Build-Graphics.ps1")
Assert-Project (
    $graphicsBuilderText -match 'Resolve-GearwrightPython' -and
    $graphicsBuilderText -match 'gearwright_graphics\.cli' -and
    $graphicsBuilderText -match 'PythonPath' -and
    (Get-Content -Raw (Join-Path $root "assets\gearwright\shapes\block\passive-fluid-pump-mechanism.json")) -notmatch '#null'
) "Python graphics definitions compile runtime shapes without null materials"
Assert-Project (-not (Get-ChildItem (Join-Path $root "assets\gearwright") -Recurse -File | Where-Object { $_.Name -match 'slingshot' })) "The slingshot workflow fixture never enters runtime assets"
$hydraulicStateText = Get-Content -Raw (Join-Path $root "code\Hydraulics\HydraulicStateSchema.cs")
Assert-Project ($hydraulicStateText -match 'CurrentVersion\s*=\s*3' -and $hydraulicStateText -match 'schema is 1 or 2') "Hydraulic state migrates sequentially through schemas 1, 2, and 3"

$graphicsRecipes = Get-ChildItem (Join-Path $root "graphics\recipes") -Filter "*.json" -File
foreach ($graphicsRecipe in $graphicsRecipes) {
    $recipeData = Get-Content -Raw $graphicsRecipe.FullName | ConvertFrom-Json
    $output = ([string]$recipeData.output).Replace('\', '/')
    Assert-Project (-not [IO.Path]::IsPathRooted($output)) "Graphics output is project-relative: $($graphicsRecipe.Name)"
    Assert-Project ($output.StartsWith("assets/gearwright/") -or $output.StartsWith("generated/")) "Graphics output uses an allowed directory: $($graphicsRecipe.Name)"
}

$ignoredDirectories = @(".git", "bin", "obj", "dist", "generated")
$textExtensions = @(".cs", ".csproj", ".json", ".md", ".ps1", ".py", ".html", ".css", ".js", ".txt", ".yml", ".yaml", ".xml")
$textFiles = Get-ChildItem $root -Recurse -File | Where-Object {
    $relative = $_.FullName.Substring($root.Length).TrimStart('\', '/')
    $segments = $relative -split '[\\/]'
    $generated = @($segments | Where-Object { $ignoredDirectories -contains $_ }).Count -gt 0
    -not $generated -and $textExtensions -contains $_.Extension.ToLowerInvariant()
}

$privateNeedles = [Collections.Generic.List[string]]::new()
foreach ($candidate in @($env:USERPROFILE, $env:OneDrive, $root)) {
    if (-not [string]::IsNullOrWhiteSpace($candidate) -and $candidate.Length -gt 3) {
        $privateNeedles.Add($candidate)
        $privateNeedles.Add($candidate.Replace('\', '/'))
    }
}

$privacyProblems = [Collections.Generic.List[string]]::new()
foreach ($file in $textFiles) {
    $content = Get-Content -Raw -LiteralPath $file.FullName
    foreach ($needle in ($privateNeedles | Select-Object -Unique)) {
        if ($content.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $privacyProblems.Add("$($file.FullName.Substring($root.Length + 1)) contains a machine-specific path")
        }
    }
    if ($content -match '(?i)(?<![a-z])[a-z]:[\\/]' -or
        $content -match '(?i)/home/[^<>\[\]{}\s/]+/' -or
        $content -match '(?i)\b[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}\b' -or
        $content -match '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----') {
        $privacyProblems.Add("$($file.FullName.Substring($root.Length + 1)) contains a private-looking literal")
    }
}
Assert-Project ($privacyProblems.Count -eq 0) "Repository text contains no local paths or private keys"
foreach ($problem in $privacyProblems) { Write-Host "       $problem" -ForegroundColor Red }

$ignoreText = Get-Content -Raw (Join-Path $root ".gitignore")
Assert-Project (
    $ignoreText -match '(?m)^dist/$' -and
    $ignoreText -match '(?m)^\*\.dll$' -and
    $ignoreText -match '(?m)^\.env$' -and
    $ignoreText -match '(?m)^__pycache__/$' -and
    $ignoreText -match '(?m)^\*\.py\[cod\]$'
) "Generated binaries, Python caches, and local secrets are ignored"

if ($failures.Count -gt 0) {
    Write-Host ("{0} project check(s) failed." -f $failures.Count) -ForegroundColor Red
    exit 1
}

Write-Host "All repository checks passed." -ForegroundColor Green
exit 0
