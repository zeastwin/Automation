param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\bin\Debug\Automation.exe')
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function New-TestProcess {
    param([int]$DelayMs)

    $proc = [Automation.Proc]::new()
    $proc.head = [Automation.ProcHead]::new()
    $proc.head.Name = '核心运行回归流程'

    $step = [Automation.Step]::new()
    $step.Id = [Guid]::NewGuid()
    $step.Name = '等待并结束'

    $delay = [Automation.Delay]::new()
    $delay.Id = [Guid]::NewGuid()
    $delay.DelayMs = $DelayMs
    $step.Ops.Add($delay)

    $end = [Automation.EndProcess]::new()
    $end.Id = [Guid]::NewGuid()
    $step.Ops.Add($end)
    $proc.steps.Add($step)
    return $proc
}

function New-TestEngine {
    param(
        [Automation.Proc]$Process,
        [Automation.ValueConfigStore]$ValueStore = $null
    )

    $processes = [System.Collections.Generic.List[Automation.Proc]]::new()
    $processes.Add($Process)
    $context = [Automation.EngineContext]::new()
    $context.Procs = $processes
    $context.ValueStore = $ValueStore
    return [Automation.ProcessEngine]::new($context)
}

function New-BusyLoopProcess {
    $proc = [Automation.Proc]::new()
    $proc.head = [Automation.ProcHead]::new()
    $proc.head.Name = '无等待循环性能回归流程'

    $step = [Automation.Step]::new()
    $step.Id = [Guid]::NewGuid()
    $step.Name = '持续跳转'

    $goto = [Automation.Goto]::new()
    $goto.Id = [Guid]::NewGuid()
    $goto.DefaultGoto = '0-0-0'
    $step.Ops.Add($goto)
    $proc.steps.Add($step)
    return $proc
}

function New-VariableBindingProcess {
    $proc = [Automation.Proc]::new()
    $proc.head = [Automation.ProcHead]::new()
    $proc.head.Name = '变量预绑定回归流程'

    $step = [Automation.Step]::new()
    $step.Id = [Guid]::NewGuid()
    $step.Name = '自增判断并复制'

    $modify = [Automation.ModifyValue]::new()
    $modify.Id = [Guid]::NewGuid()
    $modify.ModifyType = '叠加'
    $modify.ValueSourceIndex = '10'
    $modify.ChangeValue = '1'
    $modify.OutputValueIndex = '10'
    $step.Ops.Add($modify)

    $paramGoto = [Automation.ParamGoto]::new()
    $paramGoto.Id = [Guid]::NewGuid()
    $paramGoto.TrueGoto = '0-0-0'
    $paramGoto.FalseGoto = '0-0-2'
    $paramGoto.Params[0].ValueIndex = '10'
    $paramGoto.Params[0].JudgeMode = '值在区间左'
    $paramGoto.Params[0].Down = 10
    $paramGoto.Params[0].IncludeBoundary = $true
    $step.Ops.Add($paramGoto)

    $getValue = [Automation.GetValue]::new()
    $getValue.Id = [Guid]::NewGuid()
    $getValue.Params[0].ValueSourceIndex = '10'
    $getValue.Params[0].ValueSaveIndex = '11'
    $step.Ops.Add($getValue)

    $end = [Automation.EndProcess]::new()
    $end.Id = [Guid]::NewGuid()
    $step.Ops.Add($end)

    $proc.steps.Add($step)
    return $proc
}

function Wait-ProcessState {
    param(
        [Automation.ProcessEngine]$Engine,
        [Automation.ProcRunState]$State,
        [int]$TimeoutMs = 5000
    )

    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Engine.GetSnapshot(0).State -eq $State) {
            return
        }
        Start-Sleep -Milliseconds 20
    }
    throw "等待流程状态超时：$State"
}

$resolvedAssembly = [IO.Path]::GetFullPath($AssemblyPath)
Assert-True (Test-Path -LiteralPath $resolvedAssembly) "找不到待测程序集：$resolvedAssembly"
[Reflection.Assembly]::LoadFrom($resolvedAssembly) | Out-Null

$proc = New-TestProcess -DelayMs 800
$engine = New-TestEngine -Process $proc
try {
    Assert-True ($engine.StartProc($proc, 0)) '首次启动应成功。'
    Wait-ProcessState -Engine $engine -State ([Automation.ProcRunState]::Running)
    Assert-True (-not $engine.StartProc($proc, 0)) '流程未结束时重复启动必须被拒绝。'
    Assert-True ($engine.GetSnapshot(0).State -eq [Automation.ProcRunState]::Running) '重复启动不得停止或替换当前实例。'
    Wait-ProcessState -Engine $engine -State ([Automation.ProcRunState]::Stopped)
    Assert-True ($engine.GetSnapshot(0).TerminationReason -eq [Automation.ProcTerminationReason]::Completed) '自然结束必须保留 Completed 终止原因。'
}
finally {
    $engine.Dispose()
}

$oldVersion = New-TestProcess -DelayMs 700
$oldVersion.head.Name = '运行版本'
$newVersion = New-TestProcess -DelayMs 0
$newVersion.head.Id = $oldVersion.head.Id
$newVersion.head.Name = '下一运行版本'
$publishEngine = New-TestEngine -Process $oldVersion
try {
    Assert-True ($publishEngine.StartProc($oldVersion, 0)) '发布回归流程首次启动应成功。'
    Wait-ProcessState -Engine $publishEngine -State ([Automation.ProcRunState]::Running)
    $publishError = $null
    Assert-True ($publishEngine.PublishProc(0, $newVersion, [ref]$publishError)) "运行中发布失败：$publishError"
    $runningSnapshot = $publishEngine.GetSnapshot(0)
    Assert-True ($runningSnapshot.State -eq [Automation.ProcRunState]::Running) '运行中发布不得终止当前实例。'
    Assert-True ($runningSnapshot.PublishedRevision -gt $runningSnapshot.AppliedRevision) '运行中保存不得替换当前运行实例。'
    Wait-ProcessState -Engine $publishEngine -State ([Automation.ProcRunState]::Stopped)
    $stoppedSnapshot = $publishEngine.GetSnapshot(0)
    Assert-True ($stoppedSnapshot.PublishedRevision -eq $stoppedSnapshot.AppliedRevision) '流程结束后内部版本状态应完成收敛。'
    Assert-True ($stoppedSnapshot.ProcName -eq '下一运行版本') '后续运行应使用保存后的流程配置。'
}
finally {
    $publishEngine.Dispose()
}

$maintenanceProc = New-TestProcess -DelayMs 100
$maintenanceEngine = New-TestEngine -Process $maintenanceProc
$lease = $null
$maintenanceError = $null
try {
    Assert-True ([Automation.SF]::TryBeginMaintenance('核心回归测试', [ref]$lease, [ref]$maintenanceError)) "进入维护状态失败：$maintenanceError"
    Assert-True (-not $maintenanceEngine.StartProc($maintenanceProc, 0)) '配置维护期间必须拒绝启动。'
    $lease.Dispose()
    $lease = $null
    Assert-True ($maintenanceEngine.StartProc($maintenanceProc, 0)) '退出维护状态后应允许正常启动。'
    Wait-ProcessState -Engine $maintenanceEngine -State ([Automation.ProcRunState]::Stopped)
}
finally {
    if ($null -ne $lease) {
        $lease.Dispose()
    }
    $maintenanceEngine.Dispose()
}

$backgroundProc = New-TestProcess -DelayMs 0
$backgroundEngine = New-TestEngine -Process $backgroundProc
$agent = $null
try {
    $assembly = [Automation.ProcessEngine].Assembly
    $controlType = $assembly.GetType('Automation.ProcessControl', $true)
    $agentType = $assembly.GetType('Automation.ProcAgent', $true)
    $control = [Activator]::CreateInstance($controlType, $true)
    $agent = [Activator]::CreateInstance(
        $agentType,
        [Reflection.BindingFlags]'Instance,Public,NonPublic',
        $null,
        @($backgroundEngine, 0),
        $null)
    $handle = [Automation.ProcHandle]::new()
    $handle.procNum = 0
    $handle.stepNum = 0
    $handle.opsNum = 0
    $handle.procName = $backgroundProc.head.Name
    $handle.procId = $backgroundProc.head.Id
    $handle.Proc = $backgroundProc
    $controlProperty = [Automation.ProcHandle].GetProperty(
        'Control',
        [Reflection.BindingFlags]'Instance,Public,NonPublic')
    $controlProperty.SetValue($handle, $control)
    $stateProperty = [Automation.ProcHandle].GetProperty(
        'State',
        [Reflection.BindingFlags]'Instance,Public,NonPublic')
    $stateProperty.SetValue($handle, [Automation.ProcRunState]::Running)
    $controlType.GetMethod(
        'SetRunning',
        [Reflection.BindingFlags]'Instance,Public,NonPublic').Invoke($control, @())

    $nextHandle = [Automation.ProcHandle]::new()
    $nextHandle.procNum = 0
    $acquireMotion = [Automation.ProcessEngine].GetMethod(
        'TryAcquireMotionResource',
        [Reflection.BindingFlags]'Instance,NonPublic')
    $releaseMotion = [Automation.ProcessEngine].GetMethod(
        'StopOwnedMotion',
        [Reflection.BindingFlags]'Instance,NonPublic')
    $firstAcquireArgs = @($handle, [ushort]0, [ushort]1, $null)
    Assert-True ($acquireMotion.Invoke($backgroundEngine, $firstAcquireArgs)) '旧运行实例应能取得轴资源。'
    $nextAcquireArgs = @($nextHandle, [ushort]0, [ushort]1, $null)
    Assert-True (-not $acquireMotion.Invoke($backgroundEngine, $nextAcquireArgs)) '同一流程的新实例不得继承旧实例占用的轴资源。'
    $releaseMotion.Invoke($backgroundEngine, @($handle, $true))
    $nextAcquireArgs = @($nextHandle, [ushort]0, [ushort]1, $null)
    Assert-True ($acquireMotion.Invoke($backgroundEngine, $nextAcquireArgs)) '旧实例释放后，新实例应能取得轴资源。'
    $releaseMotion.Invoke($backgroundEngine, @($nextHandle, $true))

    $acquireCoordinate = [Automation.ProcessEngine].GetMethod(
        'TryAcquireCoordinateSystem',
        [Reflection.BindingFlags]'Instance,NonPublic')
    $firstCoordinateArgs = @($handle, [ushort]0, [ushort]0, $null)
    Assert-True ($acquireCoordinate.Invoke($backgroundEngine, $firstCoordinateArgs)) '旧运行实例应能取得坐标系资源。'
    $nextCoordinateArgs = @($nextHandle, [ushort]0, [ushort]0, $null)
    Assert-True (-not $acquireCoordinate.Invoke($backgroundEngine, $nextCoordinateArgs)) '坐标系资源不得被另一个运行实例并发占用。'
    $releaseMotion.Invoke($backgroundEngine, @($handle, $true))
    $nextCoordinateArgs = @($nextHandle, [ushort]0, [ushort]0, $null)
    Assert-True ($acquireCoordinate.Invoke($backgroundEngine, $nextCoordinateArgs)) '旧实例释放后，新实例应能取得坐标系资源。'
    $releaseMotion.Invoke($backgroundEngine, @($nextHandle, $true))

    $backgroundTask = [Threading.Tasks.Task]::Delay(10000, $handle.CancellationToken)
    $handle.RunningTasks.Add($backgroundTask)
    $runWorker = $agentType.GetMethod(
        'RunWorker',
        [Reflection.BindingFlags]'Instance,NonPublic')
    $runWorker.Invoke($agent, @($backgroundProc, $handle, $control))
    Assert-True $backgroundTask.IsCompleted '流程结束时必须取消并收敛后台任务。'
    Assert-True (-not [Automation.SF]::SecurityLocked) '已正常取消的后台任务不应触发安全锁定。'
    Assert-True ($handle.TerminationReason -eq [Automation.ProcTerminationReason]::Completed) '后台任务收尾不得改变自然结束原因。'
}
finally {
    if ($null -ne $agent) {
        $agentType.GetMethod('Dispose').Invoke($agent, @())
    }
    $backgroundEngine.Dispose()
}

$valueStore = [Automation.ValueConfigStore]::new()
Assert-True ($valueStore.TrySetValue(10, '高速数值变量', 'double', '1.25', '回归测试')) '数值变量创建失败。'
Assert-True ($valueStore.setValueByIndex(10, 2.5)) '数值变量运行值写入失败。'
Assert-True ([Math]::Abs($valueStore.get_D_ValueByIndex(10) - 2.5) -lt 0.000001) '数值变量原子缓存读取结果错误。'
Assert-True ($valueStore.TrySetValue(11, '预绑定复制变量', 'double', '0', '回归测试')) '预绑定复制变量创建失败。'
$privateOwner = New-TestProcess -DelayMs 0
$privateOwner.head.Name = '私有变量所属流程'
$otherProcess = New-TestProcess -DelayMs 0
$otherProcess.head.Name = '其他流程'
Assert-True ($valueStore.TrySetValue(
    12, '流程私有状态', 'double', '3', '回归测试', '测试创建', 'process', $privateOwner.head.Id)) '私有变量创建失败。'
$privateValue = $null
Assert-True ($valueStore.TryGetValueByNameForProcess(
    '流程私有状态', $privateOwner.head.Id, [ref]$privateValue)) '所属流程应可读取自己的私有变量。'
$privateValue = $null
Assert-True (-not $valueStore.TryGetValueByNameForProcess(
    '流程私有状态', $otherProcess.head.Id, [ref]$privateValue)) '其他流程不得读取私有变量。'
$privateValue = $null
Assert-True ($valueStore.TryGetValueByName('流程私有状态', [ref]$privateValue)) '外部管理通道应可按唯一名称读取私有变量。'
$staticPrivateRef = [Automation.ValueRef]::new()
$staticRefError = $null
Assert-True ([Automation.ValueRef]::TryCreate(
    '12', $null, $null, $null, $false, '静态私有变量', [ref]$staticPrivateRef, [ref]$staticRefError)) "静态私有变量引用创建失败：$staticRefError"
$boundPrivateRef = [Automation.ValueRef]::new()
Assert-True ($staticPrivateRef.TryBindStatic(
    $valueStore, $privateOwner.head.Id, '静态私有变量', [ref]$boundPrivateRef, [ref]$staticRefError)) "所属流程静态预绑定失败：$staticRefError"
Assert-True (-not $staticPrivateRef.TryBindStatic(
    $valueStore, $otherProcess.head.Id, '静态私有变量', [ref]$boundPrivateRef, [ref]$staticRefError)) '其他流程的静态私有变量引用必须在启动预绑定时失败。'
$privateId = $privateValue.Id
Assert-True ($valueStore.setValueByIndex(12, 8)) '私有变量运行值设置失败。'
Assert-True ($valueStore.TrySetValue(
    12, '流程私有状态重命名', 'double', '99', '配置修改', '测试配置修改', 'process', $privateOwner.head.Id)) '私有变量配置修改失败。'
$privateValue = $null
Assert-True ($valueStore.TryGetValueByIndex(12, [ref]$privateValue)) '私有变量配置修改后读取失败。'
Assert-True ($privateValue.Id -eq $privateId) '配置修改必须保持变量稳定ID。'
Assert-True ([Math]::Abs($privateValue.GetDValue() - 99) -lt 0.000001) '单一当前值模型应立即应用变量值修改。'

$metadataDraft = $valueStore.BuildSaveData()
$metadataValue = $metadataDraft['流程私有状态重命名']
$metadataDraft.Remove('流程私有状态重命名') | Out-Null
$metadataValue.Name = '流程私有状态元数据调整'
$metadataValue.Index = 13
$metadataValue.Scope = 'public'
$metadataValue.OwnerProcId = $null
$metadataValue.Value = '-1'
$metadataDraft[$metadataValue.Name] = $metadataValue
$valueStore.ReplaceConfiguration($metadataDraft)
$metadataCurrent = $null
Assert-True ($valueStore.TryGetValueByName('流程私有状态元数据调整', [ref]$metadataCurrent)) '元数据调整后变量读取失败。'
Assert-True ($metadataCurrent.Id -eq $privateId) '元数据调整必须保持变量稳定ID。'
Assert-True ([Math]::Abs($metadataCurrent.GetDValue() - 99) -lt 0.000001) '改名、换作用域和换槽位不得覆盖变量当前值。'
$metadataDraft = $valueStore.BuildSaveData()
$metadataValue = $metadataDraft['流程私有状态元数据调整']
$metadataDraft.Remove('流程私有状态元数据调整') | Out-Null
$metadataValue.Name = '流程私有状态重命名'
$metadataValue.Index = 12
$metadataValue.Scope = 'process'
$metadataValue.OwnerProcId = $privateOwner.head.Id
$metadataDraft[$metadataValue.Name] = $metadataValue
$valueStore.ReplaceConfiguration($metadataDraft)

$ownerScopeProc = New-VariableBindingProcess
$ownerScopeProc.head.Id = $privateOwner.head.Id
$ownerScopeProc.head.Name = '私有变量所属流程'
$ownerScopeProc.steps[0].Ops[0].ValueSourceIndex = '12'
$ownerScopeProc.steps[0].Ops[0].OutputValueIndex = '12'
$ownerScopeProc.steps[0].Ops[1].Params[0].ValueIndex = '12'
$ownerScopeProc.steps[0].Ops[2].Params[0].ValueSourceIndex = '12'
$scopeProcesses = [System.Collections.Generic.List[Automation.Proc]]::new()
$scopeProcesses.Add($ownerScopeProc)
$ownerAnalysis = [Automation.ProcessReadinessService]::Analyze(
    0, $ownerScopeProc, $scopeProcesses, $null, $valueStore)
Assert-True $ownerAnalysis.Runnable '所属流程引用自己的私有变量应保持 ready。'
$ownerScopeEngine = New-TestEngine -Process $ownerScopeProc -ValueStore $valueStore
try {
    Assert-True ($ownerScopeEngine.StartProc($ownerScopeProc, 0)) '所属流程应能运行自己的私有变量引用。'
    Wait-ProcessState -Engine $ownerScopeEngine -State ([Automation.ProcRunState]::Stopped)
    $privateAfterFirstRun = $valueStore.get_D_ValueByIndex(12)
    Assert-True ($ownerScopeEngine.StartProc($ownerScopeProc, 0)) '流程停止后应允许再次启动。'
    Wait-ProcessState -Engine $ownerScopeEngine -State ([Automation.ProcRunState]::Stopped)
    Assert-True ([Math]::Abs($valueStore.get_D_ValueByIndex(12) - ($privateAfterFirstRun + 1)) -lt 0.000001) '流程再次启动不得重置私有变量当前值。'
}
finally {
    $ownerScopeEngine.Dispose()
}

$otherScopeProc = New-VariableBindingProcess
$otherScopeProc.head.Id = $otherProcess.head.Id
$otherScopeProc.head.Name = '其他流程'
$otherScopeProc.steps[0].Ops[0].ValueSourceIndex = '12'
$otherScopeProc.steps[0].Ops[0].OutputValueIndex = '12'
$otherScopeProc.steps[0].Ops[1].Params[0].ValueIndex = '12'
$otherScopeProc.steps[0].Ops[2].Params[0].ValueSourceIndex = '12'
$scopeProcesses = [System.Collections.Generic.List[Automation.Proc]]::new()
$scopeProcesses.Add($otherScopeProc)
$otherAnalysis = [Automation.ProcessReadinessService]::Analyze(
    0, $otherScopeProc, $scopeProcesses, $null, $valueStore)
Assert-True (-not $otherAnalysis.Runnable) '引用其他流程私有变量的流程必须禁止启动。'
Assert-True ($otherAnalysis.ReadinessStatus -eq 'incomplete') '其他流程私有变量引用应保存为 incomplete。'
Assert-True (($otherAnalysis.RunBlockers -join "`n").Contains('其他流程的私有变量')) '启动阻塞应明确指出私有变量不可访问。'
$otherScopeEngine = New-TestEngine -Process $otherScopeProc -ValueStore $valueStore
try {
    Assert-True (-not $otherScopeEngine.StartProc($otherScopeProc, 0)) '运行闸门必须拒绝其他流程私有变量引用。'
}
finally {
    $otherScopeEngine.Dispose()
}

Assert-True ($valueStore.TrySetValue(14, '动态目标索引', 'double', '12', '动态作用域回归')) '动态目标索引变量创建失败。'
$dynamicProc = [Automation.Proc]::new()
$dynamicProc.head = [Automation.ProcHead]::new()
$dynamicProc.head.Id = $otherProcess.head.Id
$dynamicProc.head.Name = '动态作用域流程'
$dynamicStep = [Automation.Step]::new()
$dynamicStep.Id = [Guid]::NewGuid()
$dynamicStep.Name = '动态读取'
$dynamicGet = [Automation.GetValue]::new()
$dynamicGet.Id = [Guid]::NewGuid()
$dynamicGet.Params[0].ValueSourceIndex2Index = '14'
$dynamicGet.Params[0].ValueSaveIndex = '11'
$dynamicStep.Ops.Add($dynamicGet)
$dynamicEnd = [Automation.EndProcess]::new()
$dynamicEnd.Id = [Guid]::NewGuid()
$dynamicStep.Ops.Add($dynamicEnd)
$dynamicProc.steps.Add($dynamicStep)
$dynamicProcesses = [System.Collections.Generic.List[Automation.Proc]]::new()
$dynamicProcesses.Add($dynamicProc)
$dynamicAnalysis = [Automation.ProcessReadinessService]::Analyze(
    0, $dynamicProc, $dynamicProcesses, $null, $valueStore)
Assert-True (-not $dynamicAnalysis.Runnable) '二级索引当前指向其他流程私有变量时必须阻止启动。'
$dynamicRef = [Automation.ValueRef]::new()
$dynamicError = $null
Assert-True ([Automation.ValueRef]::TryCreate(
    $null, '14', $null, $null, $false, '动态变量', [ref]$dynamicRef, [ref]$dynamicError)) "动态变量引用创建失败：$dynamicError"
$dynamicValue = $null
Assert-True (-not $dynamicRef.TryResolveValue(
    $valueStore, '动态变量', $otherProcess.head.Id, [ref]$dynamicValue, [ref]$dynamicError)) '动态索引每次访问都必须拒绝其他流程私有变量。'
Assert-True ($valueStore.setValueByIndex(14, 10)) '动态目标索引切换失败。'
$dynamicAnalysis = [Automation.ProcessReadinessService]::Analyze(
    0, $dynamicProc, $dynamicProcesses, $null, $valueStore)
Assert-True $dynamicAnalysis.Runnable '二级索引切换到公共变量后应自动恢复ready。'
Assert-True ($dynamicRef.TryResolveValue(
    $valueStore, '动态变量', $otherProcess.head.Id, [ref]$dynamicValue, [ref]$dynamicError)) "动态索引切换到公共变量后应立即恢复：$dynamicError"
Assert-True ($dynamicValue.Index -eq 10) '动态索引重新解析结果错误。'

$recoveryStore = [Automation.ValueConfigStore]::new()
Assert-True ($recoveryStore.TrySetValue(21, '恢复输出变量', 'double', '0', '引用恢复回归')) '引用恢复输出变量创建失败。'
$recoveryProc = [Automation.Proc]::new()
$recoveryProc.head = [Automation.ProcHead]::new()
$recoveryProc.head.Name = '名称引用恢复流程'
$recoveryStep = [Automation.Step]::new()
$recoveryStep.Id = [Guid]::NewGuid()
$recoveryStep.Name = '名称读取'
$recoveryGet = [Automation.GetValue]::new()
$recoveryGet.Id = [Guid]::NewGuid()
$recoveryGet.Params[0].ValueSourceName = '待恢复变量'
$recoveryGet.Params[0].ValueSaveName = '恢复输出变量'
$recoveryStep.Ops.Add($recoveryGet)
$recoveryEnd = [Automation.EndProcess]::new()
$recoveryEnd.Id = [Guid]::NewGuid()
$recoveryStep.Ops.Add($recoveryEnd)
$recoveryProc.steps.Add($recoveryStep)
$recoveryProcesses = [System.Collections.Generic.List[Automation.Proc]]::new()
$recoveryProcesses.Add($recoveryProc)
$recoveryAnalysis = [Automation.ProcessReadinessService]::Analyze(
    0, $recoveryProc, $recoveryProcesses, $null, $recoveryStore)
Assert-True ($recoveryAnalysis.ReadinessStatus -eq 'incomplete') '缺失名称引用应允许保存为incomplete。'
Assert-True ($recoveryGet.Params[0].ValueSourceName -eq '待恢复变量') '缺失名称引用文本必须保留。'
Assert-True ($recoveryStore.TrySetValue(22, '待恢复变量', 'double', '7', '引用恢复回归')) '同名变量恢复创建失败。'
$recoveryAnalysis = [Automation.ProcessReadinessService]::Analyze(
    0, $recoveryProc, $recoveryProcesses, $null, $recoveryStore)
Assert-True $recoveryAnalysis.Runnable '重新创建同名可访问变量后流程应自动恢复ready。'

$aiChangeSet = [Automation.Protocol.AiChangeSet]::new()
$aiChangeSet.Version = 2
$aiChangeSet.Title = 'AI私有变量作用域回归'
$aiChangeSet.Processes = [System.Collections.Generic.List[Automation.Protocol.ProcessDefinition]]::new()
$aiProcess = [Automation.Protocol.ProcessDefinition]::new()
$aiProcess.Key = 'ai_private_scope'
$aiProcess.Action = 'create'
$aiProcess.Name = 'AI引用其他流程私有变量'
$aiProcess.Steps = [System.Collections.Generic.List[Automation.Protocol.StepDefinition]]::new()
$aiStep = [Automation.Protocol.StepDefinition]::new()
$aiStep.Key = 'main'
$aiStep.Name = '主步骤'
$aiStep.Operations = [System.Collections.Generic.List[Automation.Protocol.SemanticOperation]]::new()
$aiAdd = [Automation.Protocol.SemanticOperation]::new()
$aiAdd.Key = 'add_private'
$aiAdd.Kind = 'variable.add'
$aiAdd.Variable = '流程私有状态重命名'
$aiAdd.Amount = 1
$aiStep.Operations.Add($aiAdd)
$aiEnd = [Automation.Protocol.SemanticOperation]::new()
$aiEnd.Key = 'end'
$aiEnd.Kind = 'flow.end'
$aiStep.Operations.Add($aiEnd)
$aiProcess.Steps.Add($aiStep)
$aiChangeSet.Processes.Add($aiProcess)
$aiCurrentProcesses = [System.Collections.Generic.List[Automation.Proc]]::new()
$aiCurrentProcesses.Add($privateOwner)
$aiCompileResult = [Automation.AiChangeSetCompiler]::Compile(
    $aiChangeSet, $aiCurrentProcesses, $valueStore.BuildSaveData(), [Automation.AiResourceSnapshot]::new())
Assert-True ($aiCompileResult.ReadinessStatus -eq 'incomplete') 'AI应允许保存其他流程私有变量引用并标记incomplete。'
Assert-True (-not $aiCompileResult.Runnable) 'AI编译出的其他流程私有变量引用不得可运行。'
Assert-True (($aiCompileResult.RunBlockers.ToString()).Contains('其他流程的私有变量')) 'AI预演应返回私有变量访问阻塞。'

$aiAdd.Variable = 'AI流程私有状态'
$aiChangeSet.Variables = [System.Collections.Generic.List[Automation.Protocol.VariableChange]]::new()
$aiVariable = [Automation.Protocol.VariableChange]::new()
$aiVariable.Name = 'AI流程私有状态'
$aiVariable.Scope = 'process'
$aiVariable.OwnerProcess = [Automation.Protocol.ProcessSelector]::new()
$aiVariable.OwnerProcess.Key = 'ai_private_scope'
$aiVariable.Type = 'double'
$aiVariable.Value = '5'
$aiVariable.Policy = 'create'
$aiChangeSet.Variables.Add($aiVariable)
$aiOwnedCompileResult = [Automation.AiChangeSetCompiler]::Compile(
    $aiChangeSet, $aiCurrentProcesses, $valueStore.BuildSaveData(), [Automation.AiResourceSnapshot]::new())
Assert-True $aiOwnedCompileResult.Runnable 'AI同阶段新建流程应能访问通过key归属的私有变量。'
$aiVariableResolution = $aiOwnedCompileResult.VariableResolutions[0]
Assert-True ($aiVariableResolution.scope.ToString() -eq 'process') 'AI变量解析结果必须返回process作用域。'
Assert-True ($aiVariableResolution.ownerProcName.ToString() -eq $aiProcess.Name) 'AI变量解析结果必须返回最终所属流程名称。'
Assert-True ([Guid]::Parse($aiVariableResolution.variableId.ToString()) -ne [Guid]::Empty) 'AI变量解析结果必须返回稳定变量ID。'
Assert-True ($aiOwnedCompileResult.CreatedObjects.variables.Count -eq 1) 'AI提交结果预演必须包含新变量稳定身份。'

$copyTargetProcId = [Guid]::NewGuid()
$copyProc = New-VariableBindingProcess
$copyProc.head.Id = $copyTargetProcId
$copyProc.steps[0].Ops[0].ValueSourceIndex = '12'
$copyProc.steps[0].Ops[0].OutputValueIndex = '12'
$copyProc.steps[0].Ops[1].Params[0].ValueIndex = '12'
$copyProc.steps[0].Ops[2].Params[0].ValueSourceIndex = '12'
$continuousCopyReference = [Automation.GetDataStructItem]::new()
$continuousCopyReference.Id = [Guid]::NewGuid()
$continuousCopyReference.Name = '连续变量复制警告'
$continuousCopyReference.IsAllItem = $true
$continuousCopyReference.FirstResultVariableName = '流程私有状态重命名'
$copyProc.steps[0].Ops.Add($continuousCopyReference)
$dynamicCopyReference = [Automation.GetValue]::new()
$dynamicCopyReference.Id = [Guid]::NewGuid()
$dynamicCopyReference.Name = '动态变量复制警告'
$dynamicCopyReference.Params[0].ValueSourceIndex2Index = '14'
$dynamicCopyReference.Params[0].ValueSaveIndex = '11'
$copyProc.steps[0].Ops.Add($dynamicCopyReference)
$copyVariables = $valueStore.BuildSaveData()
$copyResult = [Automation.ProcessVariableLifecycleService]::CopyPrivateVariables(
    $privateOwner.head.Id, $copyTargetProcId, $copyProc, $copyVariables)
Assert-True ($copyResult.Mappings.Count -eq 1) '流程复制应复制全部私有变量。'
$copyMapping = $copyResult.Mappings[0]
Assert-True ($copyMapping.VariableId -ne $privateId) '复制变量必须生成新的稳定ID。'
Assert-True ($copyMapping.Name -eq '流程私有状态重命名_副本') '复制变量名称应使用“原名称_副本”。'
Assert-True ($copyMapping.Index -ne 12) '复制变量必须分配新槽位。'
Assert-True ($copyProc.steps[0].Ops[0].ValueSourceIndex -eq $copyMapping.Index.ToString()) '副本内部索引引用应自动改写。'
Assert-True $copyResult.HasUnresolvedReferences '连续变量引用应形成允许保存的复制警告。'
Assert-True ($continuousCopyReference.FirstResultVariableName -eq '流程私有状态重命名') '无法可靠转换的连续引用必须保留原文本。'
Assert-True (($copyResult.Warnings -join "`n").Contains('动态变量引用')) '动态变量引用应在复制预览中形成警告。'
$copiedVariable = $copyVariables[$copyMapping.Name]
Assert-True ($copiedVariable.OwnerProcId -eq $copyTargetProcId) '复制变量必须归属副本流程。'
Assert-True ([Math]::Abs($copiedVariable.GetDValue() - $valueStore.get_D_ValueByIndex(12)) -lt 0.000001) '流程复制应保留变量当前值。'
Assert-True ([Automation.ProcessVariableLifecycleService]::RemoveOwnedVariables(
    $copyVariables, [Guid[]]@($copyTargetProcId)) -eq 1) '删除流程应同步删除其私有变量。'
Assert-True (-not $copyVariables.ContainsKey($copyMapping.Name)) '删除流程后不应保留其私有变量。'

Assert-True ($valueStore.setValueByIndex(10, 0)) '预绑定回归源变量复位失败。'
$bindingProc = New-VariableBindingProcess
$bindingEngine = New-TestEngine -Process $bindingProc -ValueStore $valueStore
try {
    Assert-True ($bindingEngine.StartProc($bindingProc, 0)) '变量预绑定流程应能够启动。'
    Wait-ProcessState -Engine $bindingEngine -State ([Automation.ProcRunState]::Stopped)
    Assert-True ($bindingEngine.GetSnapshot(0).TerminationReason -eq [Automation.ProcTerminationReason]::Completed) '变量预绑定流程应自然结束。'
    Assert-True ([Math]::Abs($valueStore.get_D_ValueByIndex(10) - 11) -lt 0.000001) '预绑定修改变量或条件跳转结果错误。'
    Assert-True ([Math]::Abs($valueStore.get_D_ValueByIndex(11) - 11) -lt 0.000001) '预绑定获取变量结果错误。'
    $runtimeBindingProperty = [Automation.OperationType].GetProperty(
        'RuntimeBinding',
        [Reflection.BindingFlags]'Instance,NonPublic')
    Assert-True ($null -ne $runtimeBindingProperty.GetValue($bindingProc.steps[0].Ops[0])) '修改变量运行计划未预绑定。'
    Assert-True ($null -ne $runtimeBindingProperty.GetValue($bindingProc.steps[0].Ops[1])) '逻辑判断运行计划未预绑定。'
    Assert-True ($null -ne $runtimeBindingProperty.GetValue($bindingProc.steps[0].Ops[2])) '获取变量运行计划未预绑定。'
}
finally {
    $bindingEngine.Dispose()
}

$dataStore = [Automation.DataStructStore]::new()
$dataError = $null
Assert-True ($dataStore.AddStruct('高速结构', [ref]$dataError)) "数据结构创建失败：$dataError"
$itemIndex = -1
Assert-True ($dataStore.CreateItem(0, '数据项', 0, [ref]$itemIndex, [ref]$dataError)) "数据项创建失败：$dataError"
$fieldIndex = -1
Assert-True ($dataStore.AddField(0, $itemIndex, '数值字段', [Automation.DataStructValueType]::Number,
    '1.5', 0, [ref]$fieldIndex, [ref]$dataError)) "数据字段创建失败：$dataError"
Assert-True ($dataStore.TrySetItemValueByIndex(0, $itemIndex, $fieldIndex, '3.75')) '数据结构运行值写入失败。'
$fieldValue = $null
Assert-True ($dataStore.TryGetItemValueByIndex(0, $itemIndex, $fieldIndex, [ref]$fieldValue)) '数据结构运行值读取失败。'
Assert-True ([Math]::Abs([double]$fieldValue - 3.75) -lt 0.000001) '数据结构单项锁读取结果错误。'
$dataSnapshot = $dataStore.GetSnapshot()
Assert-True ([Math]::Abs($dataSnapshot[0].dataStructItems[0].num[0] - 3.75) -lt 0.000001) '数据结构快照未保留运行值。'

$normalAnalysisConfig = [Automation.AppConfig]::new()
$normalAnalysisConfig.CommMaxMessageQueueSize = 1000
$normalAnalysisConfig.RuntimeMode = [Automation.AutomationRuntimeMode]::Hardware
$normalAnalysisConfig.StartupView = [Automation.AutomationStartupView]::Hmi
$normalAnalysisConfig.ProcessExecutionMode = [Automation.ProcessExecutionMode]::Normal
$normalAnalysisConfig.EnablePerformanceAnalysis = $true
$configuredOptions = $null
$configureError = $null
Assert-True ([Automation.AutomationRuntimeOptions]::TryConfigure(
    [string[]]@(), $normalAnalysisConfig, [ref]$configuredOptions, [ref]$configureError)) "配置普通模式性能分析回归失败：$configureError"

$busyProc = New-BusyLoopProcess
$busyEngine = New-TestEngine -Process $busyProc
try {
    Assert-True ($busyEngine.StartProc($busyProc, 0)) '无等待循环应能够启动。'
    $deadline = [DateTime]::UtcNow.AddSeconds(6)
    $detected = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        $busySnapshot = $busyEngine.GetSnapshot(0)
        if ($null -ne $busySnapshot.Performance -and $busySnapshot.Performance.AbnormalCpuLoopDetected) {
            $detected = $true
            break
        }
        Start-Sleep -Milliseconds 50
    }
    Assert-True $detected '性能分析应报告持续占满单核的无等待循环。'
    Assert-True ($busyEngine.GetSnapshot(0).State -eq [Automation.ProcRunState]::Running) '性能异常报告不得终止流程。'
    Assert-True ($busyEngine.GetSnapshot(0).Performance.OperationDurationSampleCount -gt 0) '性能分析应保留抽样指令耗时。'
    $busyEngine.Stop(0)
    Wait-ProcessState -Engine $busyEngine -State ([Automation.ProcRunState]::Stopped)
}
finally {
    $busyEngine.Dispose()
}

$highPerformanceConfig = [Automation.AppConfig]::new()
$highPerformanceConfig.CommMaxMessageQueueSize = 1000
$highPerformanceConfig.RuntimeMode = [Automation.AutomationRuntimeMode]::Hardware
$highPerformanceConfig.StartupView = [Automation.AutomationStartupView]::Hmi
$highPerformanceConfig.ProcessExecutionMode = [Automation.ProcessExecutionMode]::HighPerformance
$highPerformanceConfig.EnablePerformanceAnalysis = $true
Assert-True ([Automation.AutomationRuntimeOptions]::TryConfigure(
    [string[]]@(), $highPerformanceConfig, [ref]$configuredOptions, [ref]$configureError)) "配置高性能分析回归失败：$configureError"
Assert-True ($configuredOptions.ProcessExecutionMode -eq [Automation.ProcessExecutionMode]::HighPerformance) '执行模式应为高性能模式。'
Assert-True $configuredOptions.PerformanceAnalysisEnabled '高性能模式与性能分析开关必须能够同时启用。'
$highPerformanceProc = New-TestProcess -DelayMs 200
$highPerformanceEngine = New-TestEngine -Process $highPerformanceProc
try {
    Assert-True ($highPerformanceEngine.StartProc($highPerformanceProc, 0)) '高性能模式流程应能够启动。'
    Wait-ProcessState -Engine $highPerformanceEngine -State ([Automation.ProcRunState]::Running)
    $highPerformanceSnapshot = $highPerformanceEngine.GetSnapshot(0)
    Assert-True $highPerformanceSnapshot.Performance.Enabled '高性能模式不得自动关闭性能分析。'
    Assert-True ($highPerformanceSnapshot.Performance.ExecutionMode -eq [Automation.ProcessExecutionMode]::HighPerformance) '流程快照执行模式错误。'
    Wait-ProcessState -Engine $highPerformanceEngine -State ([Automation.ProcRunState]::Stopped)
}
finally {
    $highPerformanceEngine.Dispose()
}

$highPerformanceConfig.EnablePerformanceAnalysis = $false
Assert-True ([Automation.AutomationRuntimeOptions]::TryConfigure(
    [string[]]@(), $highPerformanceConfig, [ref]$configuredOptions, [ref]$configureError)) "配置高性能无分析回归失败：$configureError"
$observedBusyProc = New-BusyLoopProcess
$observedBusyEngine = New-TestEngine -Process $observedBusyProc
try {
    Assert-True ($observedBusyEngine.StartProc($observedBusyProc, 0)) '高性能无分析循环应能够启动。'
    Wait-ProcessState -Engine $observedBusyEngine -State ([Automation.ProcRunState]::Running)
    $initialObservedTicks = $observedBusyEngine.GetSnapshot(0).UpdateTicks
    Start-Sleep -Milliseconds 750
    $observedSnapshot = $observedBusyEngine.GetSnapshot(0)
    Assert-True (-not $observedSnapshot.Performance.Enabled) '高性能模式不得强制启用性能分析。'
    Assert-True ($observedSnapshot.UpdateTicks -gt $initialObservedTicks) '高性能模式关闭分析后仍应低频刷新运行快照。'
    Assert-True ($observedSnapshot.State -eq [Automation.ProcRunState]::Running) '低频观测不得改变流程状态。'
    $observedBusyEngine.Stop(0)
    Wait-ProcessState -Engine $observedBusyEngine -State ([Automation.ProcRunState]::Stopped)
}
finally {
    $observedBusyEngine.Dispose()
}

$invalidValueConfigPath = Join-Path ([IO.Path]::GetTempPath()) ("AutomationValueContract_" + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($invalidValueConfigPath) | Out-Null
try {
    $invalidValueFile = Join-Path $invalidValueConfigPath 'value.json'
    $legacyValueJson = '{"旧变量":{"Index":0,"Type":"double","Name":"旧变量","Value":"1","Note":"旧格式"}}'
    [IO.File]::WriteAllText($invalidValueFile, $legacyValueJson, [Text.UTF8Encoding]::new($false))
    $invalidValueStore = [Automation.ValueConfigStore]::new()
    Assert-True (-not $invalidValueStore.Load($invalidValueConfigPath, [Automation.Proc[]]@())) '旧value.json必须按不兼容契约拒绝加载。'
    Assert-True $invalidValueStore.ConfigurationFaulted '旧value.json加载失败后必须进入变量配置故障状态。'
    Assert-True ([IO.File]::ReadAllText($invalidValueFile) -eq $legacyValueJson) '非法旧value.json必须原样保留，平台不得自动覆盖。'
}
finally {
    [IO.Directory]::Delete($invalidValueConfigPath, $true)
}

Write-Host '核心运行回归通过：流程私有变量隔离、AI两阶段变量编译、静态预绑定、动态索引复验、当前值保持、流程复制/删除、旧契约降级，以及既有运行与性能回归。'
