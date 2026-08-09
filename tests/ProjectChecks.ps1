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
    "tests\Gearwright.Contracts\fixtures\hydraulic-pipe-schema3.json",
    "tests\Gearwright.Contracts\fixtures\hydraulic-pump-schema1.json",
    "graphics\README.md", "graphics\review\README.md", "graphics\review\slingshot_workflow.py",
    "skills\vintage-story-modeling\SKILL.md", "skills\vintage-story-modeling\agents\openai.yaml",
    "graphics\models\pottery_profile_tool.py", "graphics\models\fluid_pipe.py",
    "graphics\models\sprinkler.py", "graphics\models\creative_fluid_pump.py",
    "graphics\models\passive_fluid_pump.py", "graphics\recipes\pottery-profile-tool.texture.json",
    "graphics\recipes\inspection-glass.texture.json", "graphics\recipes\inspection-shadow.texture.json",
    "graphics\recipes\steam.texture.json",
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
    "assets\gearwright\itemtypes\fluid-pipe-intake-copper.json",
    "assets\gearwright\blocktypes\fluid-pipe-copper.json",
    "assets\gearwright\blocktypes\creative-fluid-pump.json",
    "assets\gearwright\blocktypes\passive-fluid-pump.json",
    "assets\gearwright\shapes\block\passive-fluid-pump-liquid.json",
    "assets\gearwright\shapes\block\passive-fluid-pump-mechanism.json",
    "assets\gearwright\shapes\block\passive-fluid-pump-intake.json",
    "assets\gearwright\shapes\block\passive-fluid-pump-outlet.json",
    "assets\gearwright\shapes\block\fluid-pipe-cap.json",
    "assets\gearwright\shapes\block\fluid-pipe-inventory.json",
    "assets\gearwright\shapes\block\fluid-pipe-intake.json",
    "assets\gearwright\textures\block\inspection-glass.png",
    "assets\gearwright\textures\block\inspection-shadow.png",
    "assets\gearwright\textures\block\steam.png",
    "assets\gearwright\shapes\item\pottery-profile-tool.json",
    "assets\gearwright\shapes\item\fluid-pipe-intake-copper.json",
    "assets\gearwright\textures\item\pottery-profile-tool.png",
    "code\Hydraulics\ScrollingLiquidSurface.cs", "code\Hydraulics\PassiveFluidPumpRenderer.cs",
    "code\Hydraulics\PipeContent.cs", "code\Hydraulics\PipeContentMesh.cs",
    "code\Hydraulics\PipeFlowSolver.cs",
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
Assert-Project ($agentText -match 'maintainer explicitly authorizes a breaking change' -and $agentText -match 'Never infer permission') "Agent guidance permits only explicitly scoped maintainer-authorized breaks"
$modelingSkillText = Get-Content -Raw (Join-Path $root "skills\vintage-story-modeling\SKILL.md")
Assert-Project ($modelingSkillText -match 'one fixed `current/` path' -and $modelingSkillText -match 'runtimePromotion: false') "The modeling skill closes review decisions without accumulating revisions or promoting fixtures"
Assert-Project ($modelingSkillText -match 'camera movement must never launch the photoshoot renderer or create PNG caches') "The modeling skill protects the persistent interactive reviewer"
Assert-Project ($modelingSkillText -match 'launch the interactive reviewer for the maintainer' -and $modelingSkillText -match 'running the documented Python command yourself') "The modeling skill requires the agent to launch the Python reviewer for the maintainer"

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
$pipeAsset = Get-Content -Raw (Join-Path $root "assets\gearwright\blocktypes\fluid-pipe-copper.json") | ConvertFrom-Json
Assert-Project ($pipeAsset.textures.shadow.base -ceq "gearwright:block/inspection-shadow") "The installed pipe intake resolves its inner shadow texture through the pipe block"
Assert-Project (
    $null -eq $pipeAsset.sounds.PSObject.Properties["place"] -and
    $null -eq $pipeAsset.sounds.PSObject.Properties["break"] -and
    $pipeAsset.sounds.hit -ceq "game:block/heavymetal-hit2"
) "Pipe assets leave placement and removal to the reliable server-side cues"

$handbookAssets = @(
    "assets\gearwright\itemtypes\pottery-profile-tool.json",
    "assets\gearwright\itemtypes\sprinkler-brass.json",
    "assets\gearwright\itemtypes\fluid-pipe-intake-copper.json",
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
    "handbook-text-passive-fluid-pump", "handbook-text-fluid-pipe-intake-copper"
)) {
    Assert-Project ($languageText -match [regex]::Escape('"' + $key + '"')) "Handbook text exists: $key"
}

$creativePumpDialogText = Get-Content -Raw (Join-Path $root "code\Hydraulics\GuiDialogCreativeFluidPump.cs")
Assert-Project (
    $creativePumpDialogText -match 'background\.BothSizing\s*=\s*ElementSizing\.FitToChildren' -and
    $creativePumpDialogText -match 'background\.WithChildren\('
) "Creative pump dialog gives its autosized background fixed child bounds"

$pipeBlockText = Get-Content -Raw (Join-Path $root "code\Hydraulics\BlockFluidPipe.cs")
$pipeEntityText = Get-Content -Raw (Join-Path $root "code\Hydraulics\BlockEntityFluidPipe.cs")
Assert-Project ($pipeBlockText -match 'game", "glass-plain' -and $pipeBlockText -notmatch 'glasspane') "Pipe inspection windows consume plain glass blocks"
Assert-Project (
    $pipeBlockText -match 'changed && !togglingPort && !removing'
) "Removing an attachment or toggling a port does not consume the held item"
Assert-Project (
    $pipeBlockText -match 'GetSelectionBoxes' -and
    $pipeBlockText -match 'GetCollisionBoxes' -and
    $pipeBlockText -match 'pipe\.IsPortEnabled\(face\)' -and
    $pipeBlockText -match 'pipe\.HasSprinkler' -and
    $pipeBlockText -match 'HydraulicFaceAddon\.PipeNozzle'
) "Pipe hitboxes follow connected arms, the installed sprinkler, and the installed nozzle"
Assert-Project ($pipeBlockText -match 'HydraulicCodes\.PipeNozzleItem' -and $pipeBlockText -match 'TryInstallAddon') "The standalone copper nozzle item installs as a pipe attachment"
Assert-Project (
    $pipeEntityText -match 'AttachmentInstallSound' -and
    $pipeEntityText -match 'game:sounds/block/metaldoor-place' -and
    $pipeEntityText -match 'AttachmentRemoveSound' -and
    $pipeEntityText -match 'game:sounds/block/chute' -and
    $pipeEntityText -match 'PlaySoundAt' -and
    $pipeEntityText -match 'Pos, 0, null, randomizePitch: false'
) "Attachment sounds are distinct and include the player who performed the server-side action"
Assert-Project (
    $pipeBlockText -match 'PipePlaceSound' -and
    $pipeBlockText -match 'game:sounds/block/plate' -and
    $pipeBlockText -match 'PlaySoundAt' -and
    $pipeEntityText -match 'PipeRemoveSound' -and
    $pipeEntityText -match 'game:sounds/block/heavymetal-hit' -and
    $pipeEntityText -match 'PipeRemoveSound, Pos, 0, null'
) "Pipe placement and removal emit distinct sounds to the initiating player"
Assert-Project (
    $pipeEntityText -match 'port-' -and
    $pipeEntityText -match 'IsPortEnabled' -and
    $pipeEntityText -match 'TryTogglePort' -and
    $pipeEntityText -match 'ConfigurePlacedPort' -and
    $pipeBlockText -match 'InitialPortFace' -and
    $pipeBlockText -match 'Controls\.ShiftKey' -and
    $pipeBlockText -match 'wrench-'
) "Pipe faces are explicit persisted ports selected on placement and toggleable with a wrench"
Assert-Project ($pipeBlockText -match 'fluid-pipe-inventory\.json') "The pipe uses its glazed elbow inventory model"
$pipeRendererText = Get-Content -Raw (Join-Path $root "code\Hydraulics\HydraulicPipeRenderer.cs")
$scrollingSurfaceText = Get-Content -Raw (Join-Path $root "code\Hydraulics\ScrollingLiquidSurface.cs")
Assert-Project ($pipeRendererText -match 'ContainerTextureSource' -and $pipeRendererText -match 'PipeContent\.SteamTexture' -and $pipeRendererText -notmatch '\.RuntimeBake\(') "Pipe rendering resolves both Vintage Story liquids and Gearwright steam"
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
$pipeContentMeshText = Get-Content -Raw (Join-Path $root "code\Hydraulics\PipeContentMesh.cs")
Assert-Project (
    $pipeRendererText -match 'pipe\.FillFraction' -and
    $pipeRendererText -match '0\.04f \+ 0\.56f' -and
    $pipeRendererText -match 'TextureScrollCyclesPerSecond' -and
    $pipeRendererText -match 'ContentScrollSpeedMultiplier = 20f' -and
    $pipeRendererText -match 'contentTexturePhase' -and
    $pipeContentMeshText -match 'FindLiquidSurface' -and
    $pipeContentMeshText -match 'VolumeBelow' -and
    $pipeContentMeshText -match 'pipe\.IsPortEnabled' -and
    $pipeContentMeshText -match 'UvStatic = false' -and
    $pipeContentMeshText -match 'TryOrientAlongFlow' -and
    $pipeContentMeshText -match 'WriteUvQuad' -and
    $pipeContentMeshText -match 'UpdateMesh'
) "Pipe interiors rise with fullness and scroll their atlas texture along pressure-driven flow"
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
$hydraulicsRecipesText = Get-Content -Raw (Join-Path $root "assets\gearwright\recipes\grid\hydraulics.json")
Assert-Project ($null -eq $gravityDrain.PSObject.Properties["creativeinventory"] -and $hydraulicsRecipesText -notmatch 'passive-fluid-pump') "The deprecated gravity drain is absent from crafting and creative inventory"
$gravityDrainCode = Get-Content -Raw (Join-Path $root "code\Hydraulics\BlockEntityPassiveFluidPump.cs")
Assert-Project (
    $gravityDrainCode -match 'GetOffer\(\)' -and
    $gravityDrainCode -match '"deprecated"' -and
    $gravityDrainCode -notmatch 'ConsumeLitres'
) "Placed legacy gravity drains remain identifiable but cannot supply the new pipe solver"
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
) "The deprecated gravity-drain model fixture retains its pressure animation semantics"
$pressureFixture = Get-Content -Raw (Join-Path $root "tests\graphics\fixtures\passive-pump-pressure.json") | ConvertFrom-Json
Assert-Project ($pressureFixture.animation -ceq "pressure" -and $pressureFixture.quantityframes -eq 30 -and $pressureFixture.frames.Count -eq 3) "Pressure animation review fixture covers idle, fractional, and full frames"
$graphicsBuilderText = Get-Content -Raw (Join-Path $root "tools\graphics\Build-Graphics.ps1")
Assert-Project (
    $graphicsBuilderText -match 'Resolve-GearwrightPython' -and
    $graphicsBuilderText -match 'gearwright_graphics\.cli' -and
    $graphicsBuilderText -match 'PythonPath' -and
    $graphicsBuilderText -notmatch '\$LASTEXITCODE' -and
    (Get-Content -Raw (Join-Path $root "assets\gearwright\shapes\block\passive-fluid-pump-mechanism.json")) -notmatch '#null'
) "Python graphics definitions compile runtime shapes without null materials or unset process-exit state"
Assert-Project (-not (Get-ChildItem (Join-Path $root "assets\gearwright") -Recurse -File | Where-Object { $_.Name -match 'slingshot' })) "The slingshot workflow fixture never enters runtime assets"
$networkText = Get-Content -Raw (Join-Path $root "code\Hydraulics\HydraulicNetworkSystem.cs")
$hydraulicMathText = Get-Content -Raw (Join-Path $root "code\Hydraulics\HydraulicMath.cs")
$flowSolverText = Get-Content -Raw (Join-Path $root "code\Hydraulics\PipeFlowSolver.cs")
Assert-Project (
    $networkText -match 'SimulationIntervalMilliseconds = 200' -and
    $networkText -match 'PlanPipeFlows' -and
    $networkText -match 'ApplyPipeFlows' -and
    $networkText -match 'previousPressures' -and
    $flowSolverText -match 'donorTotals' -and
    $flowSolverText -match 'receiverTotals' -and
    $flowSolverText -match 'ScaleTransfers' -and
    $hydraulicMathText -match 'PipeCapacityLitres = 10' -and
    $hydraulicMathText -match 'WaterHeadKPaPerBlock = 9\.80665' -and
    $hydraulicMathText -match 'GasGaugePressure'
) "The 5 Hz pipe solver plans from previous state, scales both ends, and models volume, water head, and compressible gas"
Assert-Project (
    $networkText -match 'HydraulicFaceAddon\.PipeNozzle' -and
    $networkText -match 'ILiquidSource' -and
    $networkText -match 'ILiquidSink' -and
    $networkText -match 'EnumBlockMaterial\.Air' -and
    $networkText -match 'WouldPlacementJoinDifferentContents' -and
    $networkText -match 'HasUpwardAirOutlet' -and
    $networkText -match 'LiquidOverflowLitres' -and
    $networkText -match 'GasVentableStandardLitres' -and
    $networkText -match 'IsAtmosphericOutlet' -and
    $networkText -match 'ProcessAirOutlet' -and
    $networkText -match 'RecordFlow\(intent\.To, rate, intent\.DirectionFrom' -and
    $networkText -match 'signedFlow > 0 \? face : face\.Opposite' -and
    $pipeRendererText -match 'SpawnNozzleParticles' -and
    $pipeRendererText -match 'IsPortOpenToAir' -and
    $pipeRendererText -match 'IntakeConeLength = \(1\.05f - NozzleMouthOffset\) \* 0\.5f' -and
    $pipeRendererText -match 'IntakeTargetOffset = 0\.62f' -and
    $pipeRendererText -match 'SpawnNozzleIntakeParticles' -and
    $pipeRendererText -match 'axial \* 0\.55f' -and
    $pipeRendererText -match 'lifetime, 0f' -and
    $pipeRendererText -match 'LiquidParticleAlpha = 104' -and
    $pipeRendererText -match 'GasParticleAlpha = 72' -and
    $pipeRendererText -match 'ParticleLightenFraction = 0\.65f' -and
    $pipeRendererText -match 'BrightenedParticleColor' -and
    $pipeRendererText -match 'MaximumNozzleOutputSpeedMultiplier = 4f' -and
    $pipeRendererText -match 'HydraulicMath\.NozzleInventoryRateLitresPerSecond \* 4' -and
    $pipeRendererText -match 'position\.AvgColor'
) "Nozzles use bright translucent content colors, fourfold peak jet power, and zero-gravity cone intake particles"
$hydraulicStateText = Get-Content -Raw (Join-Path $root "code\Hydraulics\HydraulicStateSchema.cs")
Assert-Project (
    $hydraulicStateText -match 'CurrentVersion\s*=\s*6' -and
    $hydraulicStateText -match 'schema is 1 or 2 or 3 or 4 or 5' -and
    $hydraulicStateText -match 'explicitly authorized breaking pipe rework' -and
    $hydraulicStateText -match 'explicit persisted port'
) "Hydraulic state reaches explicit-port schema 6 through sequential migrations"

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
