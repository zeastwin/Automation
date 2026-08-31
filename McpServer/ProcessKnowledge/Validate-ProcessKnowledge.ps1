﻿[CmdletBinding()]
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
