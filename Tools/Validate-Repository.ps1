$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repositoryRoot

$errors = [System.Collections.Generic.List[string]]::new()

function Add-ValidationError {
    param([string]$Message)
    $errors.Add($Message)
}

$projectVersionPath = Join-Path $repositoryRoot 'ProjectSettings/ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $projectVersionPath)) {
    Add-ValidationError '缺少 ProjectSettings/ProjectVersion.txt'
}
elseif (-not (Select-String -LiteralPath $projectVersionPath -Pattern '^m_EditorVersion: 2022\.3\.' -Quiet)) {
    Add-ValidationError '项目必须使用 Unity 2022.3 LTS。'
}

$buildSettingsPath = Join-Path $repositoryRoot 'ProjectSettings/EditorBuildSettings.asset'
if (-not (Select-String -LiteralPath $buildSettingsPath -Pattern 'path: Assets/Scenes/Network_Playground\.unity' -Quiet)) {
    Add-ValidationError '主场景未写入 EditorBuildSettings。'
}

$cardFiles = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'Assets/Gamedata/Cards') -Filter '*.asset')
$skillTypes = [System.Collections.Generic.HashSet[int]]::new()
foreach ($cardFile in $cardFiles) {
    $content = Get-Content -Raw -LiteralPath $cardFile.FullName
    $skillMatch = [regex]::Match($content, '(?m)^  skillType: (-?\d+)$')
    $costMatch = [regex]::Match($content, '(?m)^  energyCost: (-?\d+)$')
    $cooldownMatch = [regex]::Match($content, '(?m)^  cooldownTurns: (-?\d+)$')

    if (-not ($skillMatch.Success -and $costMatch.Success -and $cooldownMatch.Success)) {
        Add-ValidationError "$($cardFile.Name) 缺少技能、费用或冷却字段。"
        continue
    }

    $skillType = [int]$skillMatch.Groups[1].Value
    if ($skillType -le 0) { Add-ValidationError "$($cardFile.Name) 的 SkillType 无效。" }
    if (-not $skillTypes.Add($skillType)) { Add-ValidationError "重复的 SkillType：$skillType" }
    if ([int]$costMatch.Groups[1].Value -lt 0) { Add-ValidationError "$($cardFile.Name) 的费用为负数。" }
    if ([int]$cooldownMatch.Groups[1].Value -lt 0) { Add-ValidationError "$($cardFile.Name) 的冷却为负数。" }
}
if ($cardFiles.Count -eq 0) { Add-ValidationError '没有找到卡牌资产。' }

$assetFiles = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'Assets') -Recurse -File |
    Where-Object { $_.Extension -ne '.meta' }
foreach ($assetFile in $assetFiles) {
    if (-not (Test-Path -LiteralPath ($assetFile.FullName + '.meta'))) {
        Add-ValidationError "资产缺少 .meta：$($assetFile.FullName.Substring($repositoryRoot.Length + 1))"
    }
}

$trackedFiles = @(git ls-files)
foreach ($trackedFile in $trackedFiles) {
    $absolutePath = Join-Path $repositoryRoot $trackedFile
    if ((Test-Path -LiteralPath $absolutePath) -and (Get-Item -LiteralPath $absolutePath).Length -ge 95MB) {
        Add-ValidationError "Git 文件接近 GitHub 100MB 限制：$trackedFile"
    }
}

$conflictMarkers = git grep -n -I -E '^(<<<<<<<|=======|>>>>>>>)' -- . 2>$null
if ($LASTEXITCODE -eq 0) {
    foreach ($marker in $conflictMarkers) { Add-ValidationError "残留合并冲突标记：$marker" }
}
elseif ($LASTEXITCODE -ne 1) {
    Add-ValidationError '无法执行 Git 冲突标记检查。'
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "Repository validation passed: $($cardFiles.Count) cards, $($skillTypes.Count) unique skills."
