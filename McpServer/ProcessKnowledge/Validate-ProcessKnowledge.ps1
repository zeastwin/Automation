[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
$requiredHeadings = @(
    '## 可观察目标',
    '## 适用边界',
    '## 当前事实与适配',
    '## 参考阶段',
    '## 完成证据',
    '## 失败、超时与恢复',
    '## 幂等与甄别结论'
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
    foreach ($heading in $requiredHeadings) {
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

$unlisted = Get-ChildItem -LiteralPath (Join-Path $knowledgeRoot 'blocks') -Filter '*.md' -File |
    Where-Object { -not $files.Contains($_.Name) }
if ($unlisted) {
    throw 'blocks/ 包含未登记规范：' + (($unlisted.Name | Sort-Object) -join '、')
}

$sourcesPath = Join-Path $knowledgeRoot 'provenance\sources.json'
$sources = Get-Content -LiteralPath $sourcesPath -Raw | ConvertFrom-Json
$sourceIds = @($sources.sources | ForEach-Object { [string]$_.sourceId })
foreach ($block in @($catalog.blocks)) {
    foreach ($sourceRef in @($block.sourceRefs)) {
        $sourceId = ([string]$sourceRef).Split(':', 2)[0]
        if ($sourceId -notin $sourceIds) {
            throw "规范引用了未知来源：$sourceRef"
        }
    }
}

Write-Output (
    "processKnowledge=valid;usableBlocks={0};sources={1}" -f
        @($catalog.blocks).Count,
        $sourceIds.Count)
