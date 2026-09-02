[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Test-Json 仅在 pwsh 6+ 提供；检测到缺失时给出可行动指引，避免晦涩报错。
if (-not (Get-Command Test-Json -ErrorAction SilentlyContinue)) {
    throw '当前 PowerShell 缺少 Test-Json 命令，请改用 PowerShell 7+ 运行：pwsh -NoProfile -ExecutionPolicy Bypass -File 此脚本路径。'
}

$knowledgeRoot = $PSScriptRoot
$schema = Get-Content -LiteralPath (Join-Path $knowledgeRoot 'schema.json') -Raw
$catalogPath = Join-Path $knowledgeRoot 'catalog.json'
$catalogRaw = Get-Content -LiteralPath $catalogPath -Raw
if (-not ($catalogRaw | Test-Json -Schema $schema)) {
    throw '可用流程规范目录不符合 schema.json。'
}

$catalog = $catalogRaw | ConvertFrom-Json
$patternIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$files = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$blockContents = @{}
$requiredHeadings = @(
    '## 可观察目标',
    '## 适用边界',
    '## 当前事实与适配',
    '## 参考阶段',
    '## 完成证据',
    '## 失败、超时与恢复',
    '## 反模式',
    '## 幂等与甄别结论'
)
# 设备框架块使用独立结构（回答"针对某种设备怎么搭流程框架"），topics 固定 composition。
$requiredFrameHeadings = @(
    '## 设备画像',
    '## 功能单元构成',
    '## 单元间衔接',
    '## 框架变化点',
    '## 搭建顺序',
    '## 完成证据',
    '## 关联块清单',
    '## 反模式',
    '## 幂等与甄别结论'
)
$requiredFrameRelatedPatternIds = @(
    'variables.design',
    'data-struct.design',
    'custom-function.code-process-collaboration',
    'observability.design'
)
$forbiddenTerms = @(
    'candidate',
    'needs-review',
    'deprecated',
    '穴位1-',
    'IOIndex',
    'jumpPos',
    'warmType',
    'F:\'
)

foreach ($block in @($catalog.blocks)) {
    if (-not $patternIds.Add([string]$block.patternId)) {
        throw "patternId 重复：$($block.patternId)"
    }
    if (-not $files.Add([string]$block.file)) {
        throw "规范文件重复引用：$($block.file)"
    }

    $contentPath = Join-Path $knowledgeRoot (Join-Path 'blocks' $block.file)
    if (-not (Test-Path -LiteralPath $contentPath -PathType Leaf)) {
        throw "规范文件不存在：$contentPath"
    }
    $content = Get-Content -LiteralPath $contentPath -Raw
    $blockContents[[string]$block.patternId] = $content
    if ([string]$block.patternId -like 'device-frame.*') {
        $blockTopics = @($block.topics)
        if ($blockTopics.Count -ne 1 -or [string]$blockTopics[0] -ne 'composition') {
            throw "设备框架 topics 必须且只能是 composition：$($block.patternId)"
        }
    }
    $headings = if ([string]$block.patternId -like 'device-frame.*') {
        $requiredFrameHeadings
    } else {
        $requiredHeadings
    }
    foreach ($heading in $headings) {
        if (-not $content.Contains($heading, [System.StringComparison]::Ordinal)) {
            throw "规范缺少必要章节 $heading：$($block.patternId)"
        }
    }
    foreach ($term in $forbiddenTerms) {
        if ($content.Contains($term, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "规范包含中间状态或项目参数 $term：$($block.patternId)"
        }
    }
}

foreach ($block in @($catalog.blocks | Where-Object { [string]$_.patternId -like 'device-frame.*' })) {
    $content = [string]$blockContents[[string]$block.patternId]
    $section = [regex]::Match(
        $content,
        '(?ms)^## 关联块清单\s*(?<body>.*?)(?=^## |\z)')
    if (-not $section.Success) {
        throw "设备框架缺少可解析的关联块清单：$($block.patternId)"
    }
    $relatedPatternIds = @(
        [regex]::Matches(
            $section.Groups['body'].Value,
            '[a-z][a-z0-9]*(?:[.\-][a-z0-9]+)+') |
            ForEach-Object { $_.Value } |
            Select-Object -Unique
    )
    foreach ($relatedPatternId in $relatedPatternIds) {
        if (-not $patternIds.Contains([string]$relatedPatternId)) {
            throw "设备框架引用了未知知识块：$($block.patternId) -> $relatedPatternId"
        }
    }
    foreach ($requiredPatternId in $requiredFrameRelatedPatternIds) {
        if ($requiredPatternId -notin $relatedPatternIds) {
            throw "设备框架缺少必需设计块：$($block.patternId) -> $requiredPatternId"
        }
    }
}

$unlisted = Get-ChildItem -LiteralPath (Join-Path $knowledgeRoot 'blocks') -Filter '*.md' -File |
    Where-Object { -not $files.Contains($_.Name) }
if ($unlisted) {
    throw 'blocks/ 包含未登记规范：' + (($unlisted.Name | Sort-Object) -join '、')
}

$sourcesPath = Join-Path $knowledgeRoot 'provenance\sources.json'
$sources = Get-Content -LiteralPath $sourcesPath -Raw | ConvertFrom-Json
$sourceIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$sourcesById = @{}
$manifestsBySourceId = @{}
$automationRoot = Split-Path -Parent (Split-Path -Parent $knowledgeRoot)
$workspaceRoot = Split-Path -Parent $automationRoot
$runsRoot = [IO.Path]::GetFullPath((Join-Path $workspaceRoot 'Transform4SNsdemo\runs')) +
    [IO.Path]::DirectorySeparatorChar
if ([string]$sources.documentType -ne 'Automation.ProcessKnowledgeSources' -or
    [int]$sources.schemaVersion -ne 1) {
    throw '来源目录类型或版本无效。'
}
foreach ($source in @($sources.sources)) {
    $sourceId = [string]$source.sourceId
    $packageLocator = [string]$source.packageLocator
    $expectedManifestSha256 = ([string]$source.manifestSha256).ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($sourceId) -or
        [string]::IsNullOrWhiteSpace([string]$source.displayName) -or
        [string]::IsNullOrWhiteSpace($packageLocator) -or
        [string]::IsNullOrWhiteSpace([string]$source.reviewConclusion) -or
        $expectedManifestSha256 -notmatch '^[a-f0-9]{64}$' -or
        -not $sourceIds.Add($sourceId)) {
        throw "来源目录包含缺失字段、无效哈希或重复 sourceId：$sourceId"
    }

    $packagePath = [IO.Path]::GetFullPath((Join-Path $workspaceRoot $packageLocator))
    if (-not $packagePath.StartsWith($runsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "来源证据包路径越界：$sourceId -> $packagePath"
    }
    $manifestPath = Join-Path $packagePath 'knowledge_candidates\manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "来源证据包 manifest 不存在：$sourceId -> $manifestPath"
    }
    $actualManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualManifestSha256 -ne $expectedManifestSha256) {
        throw "来源证据包 manifest 哈希不一致：$sourceId"
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([string]$manifest.DocumentType -ne 'Automation.LegacyProcessKnowledgeCandidates' -or
        [int]$manifest.SchemaVersion -ne 1) {
        throw "来源证据包 manifest 类型或版本无效：$sourceId"
    }
    $sourcesById[$sourceId] = $source
    $manifestsBySourceId[$sourceId] = [pscustomobject]@{
        PackagePath = $packagePath
        Manifest = $manifest
    }
}

$validatedCaseRefs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($block in @($catalog.blocks)) {
    foreach ($sourceRef in @($block.sourceRefs)) {
        $parts = ([string]$sourceRef).Split(':', 2)
        $sourceId = $parts[0]
        $caseId = if ($parts.Count -eq 2) { $parts[1] } else { '' }
        if (-not $sourcesById.ContainsKey($sourceId)) {
            throw "规范引用了未知来源：$sourceRef"
        }
        if ([string]::IsNullOrWhiteSpace($caseId)) {
            throw "规范来源引用缺少 caseId：$sourceRef"
        }
        if (-not $validatedCaseRefs.Add([string]$sourceRef)) {
            continue
        }
        $manifestInfo = $manifestsBySourceId[$sourceId]
        $case = @($manifestInfo.Manifest.Cases) |
            Where-Object { [string]$_.CaseId -eq $caseId } |
            Select-Object -First 1
        if ($null -eq $case) {
            throw "规范引用了来源 manifest 中不存在的案例：$sourceRef"
        }
        $candidateRoot = [IO.Path]::GetFullPath(
            (Join-Path $manifestInfo.PackagePath 'knowledge_candidates')) +
            [IO.Path]::DirectorySeparatorChar
        $caseFilePath = [IO.Path]::GetFullPath((Join-Path $candidateRoot ([string]$case.CaseFile)))
        if (-not $caseFilePath.StartsWith($candidateRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $caseFilePath -PathType Leaf)) {
            throw "规范引用的标准化案例文件不存在或路径越界：$sourceRef"
        }
        $expectedCaseSha256 = ([string]$case.CaseFileSha256).ToLowerInvariant()
        $actualCaseSha256 = (Get-FileHash -LiteralPath $caseFilePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($expectedCaseSha256 -notmatch '^[a-f0-9]{64}$' -or
            $actualCaseSha256 -ne $expectedCaseSha256) {
            throw "规范引用的标准化案例哈希不一致：$sourceRef"
        }
    }
}

Write-Output (
    "processKnowledge=valid;usableBlocks={0};sources={1}" -f
        @($catalog.blocks).Count,
        $sourceIds.Count)
