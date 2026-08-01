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
    "modinfo.json", "Gearwright.csproj",
    "tests\Gearwright.Contracts\Gearwright.Contracts.csproj", "tests\Gearwright.Contracts\Program.cs",
    "graphics\README.md", "graphics\recipes\example-workshop-marker.model.json",
    "graphics\recipes\pottery-profile-tool.model.json", "graphics\recipes\pottery-profile-tool.texture.json",
    "assets\gearwright\itemtypes\pottery-profile-tool.json",
    "assets\gearwright\shapes\item\pottery-profile-tool.json",
    "assets\gearwright\textures\item\pottery-profile-tool.png",
    "tools\Common.ps1", "tools\Build-Mod.ps1", "tools\Test-Project.ps1", "tools\Install-Mod.ps1",
    "tools\graphics\Build-Graphics.ps1", "tools\graphics\Build-Model.ps1",
    "tools\graphics\Build-Texture.ps1", "tools\graphics\Find-GameAsset.ps1",
    "tools\graphics\Graphics.Common.ps1"
)
foreach ($relative in $required) {
    Assert-Project (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf) "Required file exists: $relative"
}

$removed = @(
    "CONTRIBUTING.md", "installation.md", "docs",
    "graphics\recipes\wiki-graphics-workflow.texture.json",
    "graphics\recipes\wiki-pottery-profile-tool.texture.json"
)
foreach ($relative in $removed) {
    Assert-Project (-not (Test-Path -LiteralPath (Join-Path $root $relative))) "Obsolete access file is absent: $relative"
}
Assert-Project (@(Get-ChildItem $root -Recurse -Filter "*.cmd" -File).Count -eq 0) "No double-click command wrappers remain"

$allowedToolScripts = @(
    "Build-Mod.ps1", "Common.ps1", "Install-Mod.ps1", "Test-Project.ps1",
    "graphics\Build-Graphics.ps1", "graphics\Build-Model.ps1",
    "graphics\Build-Texture.ps1", "graphics\Find-GameAsset.ps1", "graphics\Graphics.Common.ps1"
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
Assert-Project ($codeText -match 'WorldStateStorageKey\s*=\s*"gearwright:world-state"') "The permanent world-state key is unchanged"
Assert-Project ($codeText -match 'ChatCommands\.Create\("gearwright"\)') "The status command is registered"
Assert-Project ($codeText -match 'RequiresPrivilege\(Privilege\.chat\)') "The status command is available to chat users"

$agentText = Get-Content -Raw (Join-Path $root "AGENTS.md")
Assert-Project ($agentText -match 'Prefer the deterministic tooling in `tools/graphics/` over GPT image generation') "Agent guidance prefers reproducible graphics"
Assert-Project ($agentText -match 'direct, plain language' -and $agentText -match 'Avoid AI writing tropes') "Agent guidance requires direct player-facing writing"
Assert-Project ($agentText -match 'tools/Install-Mod\.ps1' -and $agentText -match 'newest verified Gearwright package') "Agent guidance requires installing the verified build"

$readmeText = Get-Content -Raw (Join-Path $root "README.md")
Assert-Project ($readmeText -match 'https://github\.com/ESmink/Gearwright/wiki') "README links to the GitHub Wiki"
$installerText = Get-Content -Raw (Join-Path $root "tools\Install-Mod.ps1")
Assert-Project ($installerText -match 'Find-VintageStoryData' -and $installerText -match 'Get-FileHash') "The installer discovers the data folder and verifies the package"

$profileItem = Get-Content -Raw (Join-Path $root "assets\gearwright\itemtypes\pottery-profile-tool.json") | ConvertFrom-Json
Assert-Project ($profileItem.code -ceq "pottery-profile-tool") "The pottery profile tool keeps its asset code"
Assert-Project ($profileItem.shape.base -ceq "gearwright:item/pottery-profile-tool") "The pottery profile tool keeps its generated shape"

$graphicsRecipes = Get-ChildItem (Join-Path $root "graphics\recipes") -Filter "*.json" -File
foreach ($graphicsRecipe in $graphicsRecipes) {
    $recipeData = Get-Content -Raw $graphicsRecipe.FullName | ConvertFrom-Json
    $output = ([string]$recipeData.output).Replace('\', '/')
    Assert-Project (-not [IO.Path]::IsPathRooted($output)) "Graphics output is project-relative: $($graphicsRecipe.Name)"
    Assert-Project ($output.StartsWith("assets/gearwright/") -or $output.StartsWith("graphics/generated/")) "Graphics output uses an allowed directory: $($graphicsRecipe.Name)"
}

$ignoredDirectories = @(".git", "bin", "obj", "dist")
$textExtensions = @(".cs", ".csproj", ".json", ".md", ".ps1", ".html", ".css", ".js", ".txt", ".yml", ".yaml", ".xml")
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
Assert-Project ($ignoreText -match '(?m)^dist/$' -and $ignoreText -match '(?m)^\*\.dll$' -and $ignoreText -match '(?m)^\.env$') "Generated binaries and local secrets are ignored"

if ($failures.Count -gt 0) {
    Write-Host ("{0} project check(s) failed." -f $failures.Count) -ForegroundColor Red
    exit 1
}

Write-Host "All repository checks passed." -ForegroundColor Green
exit 0
