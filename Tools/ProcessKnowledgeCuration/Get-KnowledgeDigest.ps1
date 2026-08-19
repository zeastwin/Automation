# ==========================================================================================
# 旧项目知识证据摘要器（甄别侧工具，只读）
# ==========================================================================================
# 用途：把 Transform4SNsdemo 产出的 normalized-cases 压缩为紧凑摘要，供甄别 AI 低 token 消费。
#       压缩原理：丢弃按指令类型重复的 FieldKeys 键名清单、每案例重复的 EvidenceLimits
#       样板、Source/Fingerprints 哈希细节，只保留甄别必需的语义结构。
#
# 输出两级：
#   1) 总览（默认）：每案例一行（名称/结构/禁用/自启/依赖计数）+ 结构族分组
#   2) 明细（-CaseIds 或 -All）：每案例的完整指令序列（序号/标签/类型/禁用/非默认报警）
#
# 用法：
#   .\Get-KnowledgeDigest.ps1 -KnowledgeDir 'F:\Auto\Transform4SNsdemo\runs\1HSG下料-knowledge'
#   .\Get-KnowledgeDigest.ps1 -KnowledgeDir <dir> -CaseIds @('case-ccc5a474b296882fa047')
#   .\Get-KnowledgeDigest.ps1 -KnowledgeDir <dir> -All            # 全部案例明细（大项目慎用）
#   .\Get-KnowledgeDigest.ps1 -KnowledgeDir <dir> -OutFile digest.md
#
# 甄别工作流：先读总览选候选 → 按结构族去重（同族读一个代表）→ 只对候选读明细。
# 需要核对具体历史字段值时，再回到 extracted_data.json 定点读取，不经过本脚本。
# ==========================================================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$KnowledgeDir,
    [string[]]$CaseIds,
    [switch]$All,
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'

$manifestPath = Join-Path $KnowledgeDir 'knowledge_candidates\manifest.json'
if (-not (Test-Path $manifestPath)) { throw "manifest 不存在：$manifestPath" }
$manifest = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json

function Get-AlarmNote {
    param($Op)
    if (-not $Op.AlarmHandling) { return '' }
    $h = $Op.AlarmHandling
    $parts = @()
    if ($h.LegacyAlarmTypeName -and $h.LegacyAlarmTypeName -ne '报警停止') {
        $parts += $h.LegacyAlarmTypeName
    }
    if ($h.BreakProcess -eq $true) { $parts += '中断流程' }
    if ($h.IndependentWarningLight -eq $true) { $parts += '独立警示灯' }
    if ($parts.Count -eq 0) { return '' }
    return ' {' + ($parts -join ',') + '}'
}

function Get-CaseDigest {
    param($Case)
    $casePath = Join-Path $KnowledgeDir ('knowledge_candidates\' + $Case.CaseFile)
    if (-not (Test-Path $casePath)) { return $null }
    $j = Get-Content $casePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $sb = [System.Text.StringBuilder]::new()
    $deps = $j.Dependencies
    [void]$sb.AppendLine("### $($j.Process.ObservedName)  ($($Case.CaseId))")
    [void]$sb.AppendLine("步骤$($j.Process.StepCount) 指令$($j.Process.OperationCount) 禁用$($Case.DisabledOperationCount) 自启$($j.Process.SelfStart) | 依赖: 变量$($deps.VariableCount)/未定义$($deps.UndefinedVariableCount) IO$($deps.IoPointCount) 报警$($deps.AlarmCount) 数据结构$($deps.DataStructCount) 通讯$($deps.PlcConnectionCount)")
    foreach ($s in $j.Process.Steps) {
        $stepNote = if ($s.LegacyEnable) { '' } else { ' [步骤禁用]' }
        [void]$sb.AppendLine("-- 步骤$($s.Order) $($s.ObservedLabel)$stepNote")
        foreach ($o in $s.Operations) {
            $dis = if ($o.Disabled) { ' [禁]' } else { '' }
            $line = '{0,3} {1} <{2}>{3}{4}' -f $o.GlobalOrder, $o.ObservedLabel, $o.LegacyTypeName, $dis, (Get-AlarmNote $o)
            [void]$sb.AppendLine($line)
        }
    }
    return $sb.ToString()
}

$out = [System.Text.StringBuilder]::new()
$totalCaseBytes = 0
$totalDigestChars = 0

# ---------- 总览 ----------
[void]$out.AppendLine("# 知识证据摘要：$(Split-Path $KnowledgeDir -Leaf)")
[void]$out.AppendLine("")
[void]$out.AppendLine("## 案例总览（$($manifest.Cases.Count) 案例 / $($manifest.Families.Count) 结构族）")
[void]$out.AppendLine("")
[void]$out.AppendLine("| # | 案例名 | 步骤 | 指令 | 禁用 | 自启 | 变量/未定义 | IO | 通讯 |")
[void]$out.AppendLine("|---|--------|------|------|------|------|-------------|-----|------|")

$depCache = @{}
foreach ($c in $manifest.Cases) {
    $casePath = Join-Path $KnowledgeDir ('knowledge_candidates\' + $c.CaseFile)
    $deps = $null
    if (Test-Path $casePath) {
        $raw = Get-Content $casePath -Raw -Encoding UTF8
        $totalCaseBytes += $raw.Length
        $j = $raw | ConvertFrom-Json
        $deps = $j.Dependencies
        $depCache[$c.CaseId] = $j
    }
    $d = $deps
    $auto = if ($j.Process.SelfStart) { '是' } else { '' }
    [void]$out.AppendLine(('| {0} | {1} | {2} | {3} | {4} | {5} | {6}/{7} | {8} | {9} |' -f `
        $c.Order, $c.ObservedProcessName, $c.StepCount, $c.OperationCount, $c.DisabledOperationCount, `
        $auto, $d.VariableCount, $d.UndefinedVariableCount, $d.IoPointCount, $d.PlcConnectionCount))
}
[void]$out.AppendLine("")
[void]$out.AppendLine("## 结构族分组（同族结构相似，甄别时每族读一个代表即可）")
[void]$out.AppendLine("")
foreach ($f in $manifest.Families | Where-Object { $_.CaseCount -gt 1 }) {
    $names = $f.ObservedProcessNames -join '、'
    [void]$out.AppendLine("- $($f.FamilyId)（$($f.CaseCount)案例）: $names")
}
if (-not ($manifest.Families | Where-Object { $_.CaseCount -gt 1 })) {
    [void]$out.AppendLine("- 全部结构族均为单案例，无跨案例结构重复。")
}

# ---------- 明细 ----------
$selected = @()
if ($All) { $selected = @($manifest.Cases) }
elseif ($CaseIds) { $selected = @($manifest.Cases | Where-Object { $CaseIds -contains $_.CaseId }) }

if ($selected.Count -gt 0) {
    [void]$out.AppendLine("")
    [void]$out.AppendLine("## 案例明细（$($selected.Count) 案例）")
    foreach ($c in $selected) {
        $digest = $null
        if ($depCache.ContainsKey($c.CaseId)) {
            # 已缓存的整包解析复用
            $j = $depCache[$c.CaseId]
            $sb2 = [System.Text.StringBuilder]::new()
            $deps = $j.Dependencies
            [void]$sb2.AppendLine("### $($j.Process.ObservedName)  ($($c.CaseId))")
            [void]$sb2.AppendLine("步骤$($j.Process.StepCount) 指令$($j.Process.OperationCount) 禁用$($c.DisabledOperationCount) 自启$($j.Process.SelfStart) | 依赖: 变量$($deps.VariableCount)/未定义$($deps.UndefinedVariableCount) IO$($deps.IoPointCount) 报警$($deps.AlarmCount) 数据结构$($deps.DataStructCount) 通讯$($deps.PlcConnectionCount)")
            foreach ($s in $j.Process.Steps) {
                $stepNote = if ($s.LegacyEnable) { '' } else { ' [步骤禁用]' }
                [void]$sb2.AppendLine("-- 步骤$($s.Order) $($s.ObservedLabel)$stepNote")
                foreach ($o in $s.Operations) {
                    $dis = if ($o.Disabled) { ' [禁]' } else { '' }
                    [void]$sb2.AppendLine(('{0,3} {1} <{2}>{3}{4}' -f $o.GlobalOrder, $o.ObservedLabel, $o.LegacyTypeName, $dis, (Get-AlarmNote $o)))
                }
            }
            $digest = $sb2.ToString()
        }
        else {
            $digest = Get-CaseDigest $c
        }
        if ($digest) {
            [void]$out.AppendLine("")
            [void]$out.AppendLine($digest.TrimEnd())
            $totalDigestChars += $digest.Length
        }
    }
}

$result = $out.ToString()
if ($OutFile) {
    [System.IO.File]::WriteAllText($OutFile, $result, (New-Object System.Text.UTF8Encoding $true))
}

# ---------- 统计 ----------
$origKB = [math]::Round($totalCaseBytes / 1KB, 1)
$digestKB = [math]::Round(($result.Length * 2) / 1KB, 1)
$ratio = if ($totalCaseBytes -gt 0) { [math]::Round($digestKB / ($totalCaseBytes / 1KB) * 100, 1) } else { 0 }
$stats = "原始案例 $origKB KB -> 摘要 $digestKB KB（$ratio%），节约 $(100 - $ratio)%"
Write-Host $stats -ForegroundColor Cyan
if ($OutFile) { Write-Host "已写入：$OutFile" -ForegroundColor Green }

# 未指定输出文件时输出全文
if (-not $OutFile) { $result }
