param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\..\bin\Debug\Automation.exe')
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$resolvedAssembly = [IO.Path]::GetFullPath($AssemblyPath)
Assert-True (Test-Path -LiteralPath $resolvedAssembly) "找不到待测程序集：$resolvedAssembly"
[Reflection.Assembly]::LoadFrom($resolvedAssembly) | Out-Null

$proc = [Automation.Proc]::new()
$proc.head = [Automation.ProcHead]::new()
$proc.head.Id = [Guid]::NewGuid()
$proc.head.Name = '主流程'

$step0 = [Automation.Step]::new()
$step0.Id = [Guid]::NewGuid()
$step0.Name = '判断'
$delay = [Automation.Delay]::new()
$delay.Id = [Guid]::NewGuid()
$delay.Name = '准备'
$delay.DelayMs = 1
$disabled = [Automation.Delay]::new()
$disabled.Id = [Guid]::NewGuid()
$disabled.Name = '禁用等待'
$disabled.Disable = $true
$branch = [Automation.ParamGoto]::new()
$branch.Id = [Guid]::NewGuid()
$branch.Name = '条件分支'
$branch.TrueGoto = '0-0-0'
$branch.FalseGoto = '0-1-0'
$step0.Ops.Add($delay)
$step0.Ops.Add($disabled)
$step0.Ops.Add($branch)

$step1 = [Automation.Step]::new()
$step1.Id = [Guid]::NewGuid()
$step1.Name = '结束'
$end = [Automation.EndProcess]::new()
$end.Id = [Guid]::NewGuid()
$step1.Ops.Add($end)
$proc.steps.Add($step0)
$proc.steps.Add($step1)

$child = [Automation.Proc]::new()
$child.head = [Automation.ProcHead]::new()
$child.head.Id = [Guid]::NewGuid()
$child.head.Name = '子流程'
$childStep = [Automation.Step]::new()
$childStep.Id = [Guid]::NewGuid()
$childEnd = [Automation.EndProcess]::new()
$childEnd.Id = [Guid]::NewGuid()
$childStep.Ops.Add($childEnd)
$child.steps.Add($childStep)

$controlStep = [Automation.Step]::new()
$controlStep.Id = [Guid]::NewGuid()
$control = [Automation.ProcOps]::new()
$control.Id = [Guid]::NewGuid()
$control.Params[0].ProcName = '子流程'
$control.Params[0].TargetState = '运行'
$controlStep.Ops.Add($control)
$proc.steps.Insert(0, $controlStep)
$branch.TrueGoto = '0-1-0'
$branch.FalseGoto = '0-2-0'

$processes = [System.Collections.Generic.List[Automation.Proc]]::new()
$processes.Add($proc)
$processes.Add($child)

$detail = [Automation.ProcessFlowGraphService]::BuildProcess($processes, 0, $null)
Assert-True ($detail.Scope -eq 'process') '流程明细范围错误。'
Assert-True ($detail.Groups.Count -eq 3) '步骤分组数量错误。'
Assert-True ($detail.DisabledNodeCount -eq 1) '禁用指令数量错误。'
Assert-True ($null -ne ($detail.Edges | Where-Object { $_.SourceId -eq ('op:' + $delay.Id.ToString('D')) -and $_.TargetId -eq ('op:' + $branch.Id.ToString('D')) } | Select-Object -First 1)) '顺序边应跳过禁用指令。'
Assert-True ($null -ne ($detail.Edges | Where-Object { $_.SourceId -eq ('op:' + $branch.Id.ToString('D')) -and $_.Label -eq '成立' } | Select-Object -First 1)) '缺少成立分支。'
Assert-True ($null -ne ($detail.Edges | Where-Object { $_.SourceId -eq ('op:' + $branch.Id.ToString('D')) -and $_.Label -eq '不成立' } | Select-Object -First 1)) '缺少不成立分支。'
Assert-True ($null -ne ($detail.Diagnostics | Where-Object { $_.Code -eq 'CONTROL_FLOW_LOOP' } | Select-Object -First 1)) '应识别条件回环。'

$project = [Automation.ProcessFlowGraphService]::BuildProject($processes, $null)
Assert-True ($project.Scope -eq 'project') '项目总览范围错误。'
Assert-True ($null -ne ($project.Nodes | Where-Object { $_.ProcId -eq $child.head.Id.ToString('D') } | Select-Object -First 1)) '项目总览缺少子流程。'
Assert-True ($null -ne ($project.Edges | Where-Object { $_.Kind -eq 'processStart' -and $_.TargetId -eq ('proc:' + $child.head.Id.ToString('D')) } | Select-Object -First 1)) '项目总览缺少启动关系。'
$control.Disable = $true
$disabledProject = [Automation.ProcessFlowGraphService]::BuildProject($processes, $null)
Assert-True ($disabledProject.DisabledEdgeCount -eq 1) '禁用的跨流程指令必须保留为可过滤关系。'
Assert-True ($null -ne ($disabledProject.Edges | Where-Object { $_.Kind -eq 'processStart' -and $_.Disabled } | Select-Object -First 1)) '跨流程禁用状态必须落在统一边契约。'
$control.Disable = $false

$branch.FalseGoto = '0-99-99'
$invalid = [Automation.ProcessFlowGraphService]::BuildProcess($processes, 0, $null)
Assert-True ($null -ne ($invalid.Diagnostics | Where-Object { $_.Code -eq 'INVALID_GOTO_TARGET' -and $_.Severity -eq 'error' } | Select-Object -First 1)) '无效跳转必须形成错误诊断。'

$edgeProc = [Automation.Proc]::new()
$edgeProc.head = [Automation.ProcHead]::new()
$edgeProc.head.Id = [Guid]::NewGuid()
$edgeProc.head.Name = '边界流程'
$emptyStep = [Automation.Step]::new()
$emptyStep.Id = [Guid]::NewGuid()
$emptyStep.Name = '空步骤'
$emptyStep.Disable = $true
$edgeStep = [Automation.Step]::new()
$edgeStep.Id = [Guid]::NewGuid()
$edgeStep.Name = '交互与自然结束'
$popup = [Automation.PopupDialog]::new()
$popup.Id = [Guid]::NewGuid()
$popup.Name = '空分支弹框'
$popup.PopupGoto1 = ''
$alarmDelay = [Automation.Delay]::new()
$alarmDelay.Id = [Guid]::NewGuid()
$alarmDelay.Name = '报警恢复'
$alarmDelay.AlarmType = '自动处理'
$alarmDelay.Goto1 = '2-1-0'
$edgeStep.Ops.Add($popup)
$edgeStep.Ops.Add($alarmDelay)
$edgeProc.steps.Add($emptyStep)
$edgeProc.steps.Add($edgeStep)
$processes.Add($edgeProc)

$edgeGraph = [Automation.ProcessFlowGraphService]::BuildProcess($processes, 2, $null)
Assert-True ($edgeGraph.Groups.Count -eq 2) '空步骤也必须保留分组。'
Assert-True ($null -ne ($edgeGraph.Edges | Where-Object { $_.SourceId -eq ('op:' + $popup.Id.ToString('D')) -and $_.Kind -eq 'sequence' } | Select-Object -First 1)) '弹框空分支应保留顺序执行边。'
Assert-True ($edgeGraph.AlarmEdgeCount -eq 1) '自动处理必须生成报警恢复边。'
Assert-True ($null -ne ($edgeGraph.Edges | Where-Object { $_.SourceId -eq ('op:' + $alarmDelay.Id.ToString('D')) -and $_.TargetId -eq ('end:' + $edgeProc.head.Id.ToString('D')) -and $_.Kind -eq 'sequence' } | Select-Object -First 1)) '末条普通指令应自然结束。'
Assert-True ([Automation.OperationGotoReferenceCatalog]::HasBusinessGoto($popup)) '公共跳转目录必须识别弹框跳转字段。'

$unknownProc = [Automation.Proc]::new()
$unknownProc.head = [Automation.ProcHead]::new()
$unknownProc.head.Id = [Guid]::NewGuid()
$unknownProc.head.Name = '未知行为流程'
$unknownStep = [Automation.Step]::new()
$unknownStep.Id = [Guid]::NewGuid()
$unknownOp = [Automation.OperationType]::new()
$unknownOp.Id = [Guid]::NewGuid()
$unknownOp.Name = '未注册指令'
$unknownOp.OperaType = '测试未知行为'
$unknownStep.Ops.Add($unknownOp)
$unknownProc.steps.Add($unknownStep)
$processes.Add($unknownProc)
$unknownGraph = [Automation.ProcessFlowGraphService]::BuildProcess($processes, 3, $null)
Assert-True ($null -ne ($unknownGraph.Diagnostics | Where-Object { $_.Code -eq 'UNKNOWN_CONTROL_FLOW' } | Select-Object -First 1)) '未知行为必须显式诊断且不得猜测控制流。'

Write-Output '流程图模型回归通过。'
