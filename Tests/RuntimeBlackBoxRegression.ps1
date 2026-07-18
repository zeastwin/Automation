param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\bin\Debug\Automation.exe')
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$resolvedAssembly = [IO.Path]::GetFullPath($AssemblyPath)
Assert-True (Test-Path -LiteralPath $resolvedAssembly) "找不到待测程序集：$resolvedAssembly"
$assembly = [Reflection.Assembly]::LoadFrom($resolvedAssembly)

$proc = [Automation.Proc]::new()
$proc.head = [Automation.ProcHead]::new()
$proc.head.Id = [Guid]::NewGuid()
$proc.head.Name = '黑匣子回归流程'
$step = [Automation.Step]::new()
$step.Id = [Guid]::NewGuid()
$delay = [Automation.Delay]::new()
$delay.Id = [Guid]::NewGuid()
$delay.DelayMs = 80
$step.Ops.Add($delay)
$end = [Automation.EndProcess]::new()
$end.Id = [Guid]::NewGuid()
$step.Ops.Add($end)
$proc.steps.Add($step)

$processes = [System.Collections.Generic.List[Automation.Proc]]::new()
$processes.Add($proc)
$valueStore = [Automation.ValueConfigStore]::new()
$context = [Automation.EngineContext]::new()
$context.Procs = $processes
$context.ValueStore = $valueStore
$engine = [Automation.ProcessEngine]::new($context)

$recorderType = $assembly.GetType('Automation.RuntimeBlackBoxRecorder', $true)
$recorder = [Activator]::CreateInstance(
    $recorderType,
    [Reflection.BindingFlags]'Instance,Public,NonPublic',
    $null,
    @($engine, $valueStore),
    $null)
$incidentPath = $null
try {
    $recorderType.GetMethod('RecordExternalEvent').Invoke(
        $recorder,
        @('communication.frames_dropped', 'Socket:测试通道', '累计丢弃1帧', $true))

    Assert-True ($engine.StartProc($proc, 0)) '黑匣子回归流程应能启动。'
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    while ([DateTime]::UtcNow -lt $deadline -and $engine.GetSnapshot(0).State -ne [Automation.ProcRunState]::Stopped) {
        Start-Sleep -Milliseconds 20
    }
    Assert-True ($engine.GetSnapshot(0).State -eq [Automation.ProcRunState]::Stopped) '黑匣子回归流程未按时结束。'

    $buildEvidence = $recorderType.GetMethod('BuildEvidencePackage')
    $evidence = $buildEvidence.Invoke($recorder, @(0))
    $eventNames = @($evidence['events'] | ForEach-Object { $_['eventName'].ToString() })
    Assert-True ($eventNames -contains 'process.started') '证据包缺少流程启动事件。'
    Assert-True ($eventNames -contains 'process.position.changed') '证据包缺少位置变化事件。'
    Assert-True ($eventNames -contains 'process.completed') '证据包缺少流程结束事件。'
    Assert-True ($eventNames -contains 'communication.frames_dropped') '证据包缺少同时间窗的平台通讯事件。'
    Assert-True ($evidence['evidenceLimits'].Count -gt 0) '证据包必须显式说明采集边界。'
    Assert-True ([int]$evidence['schemaVersion'] -eq 2) '黑匣子证据契约版本应为2。'
    Assert-True ([int]$evidence['retentionPolicy']['processRetentionSeconds'] -eq 600) '流程缓冲应保留10分钟。'
    Assert-True ([int]$evidence['retentionPolicy']['maximumEventsPerProcess'] -eq 8192) '单流程缓冲上限应为8192条。'
    Assert-True ([int]$evidence['retentionPolicy']['maximumAiEvidenceEvents'] -eq 300) 'AI证据上限应为300条。'

    $buildTimeline = $recorderType.GetMethod('BuildTimelinePackage')
    $defaultTimeline = $buildTimeline.Invoke($recorder, @(0, $false))
    Assert-True ([int]$defaultTimeline['capturedEventCount'] -le 200) '默认诊断时间线最多显示200条。'
    $fullTimeline = $buildTimeline.Invoke($recorder, @(0, $true))
    Assert-True ($fullTimeline['selectionMode'].ToString() -eq 'full_retained_buffer') '无事故时展开项应返回完整保留缓冲。'

    for ($index = 0; $index -lt 350; $index++) {
        $recorderType.GetMethod('RecordExternalEvent').Invoke(
            $recorder,
            @('communication.observed', 'Socket:容量验证', "帧$index", $false))
    }
    $boundedEvidence = $buildEvidence.Invoke($recorder, @(0))
    Assert-True ([int]$boundedEvidence['rawRelevantEventCount'] -gt 300) '容量回归必须形成超过300条的原始相关事件。'
    Assert-True ([int]$boundedEvidence['capturedEventCount'] -eq 300) 'AI证据包超过上限时必须稳定裁剪为300条。'

    $failureType = $assembly.GetType('Automation.OperationFailureEntry', $true)
    $failure = [Activator]::CreateInstance(
        $failureType,
        [Reflection.BindingFlags]'Instance,Public,NonPublic',
        $null,
        @([int]0, $proc.head.Id, [int]0, [int]0, $delay.Id,
            '延时', '测试延时', '测试故障', [long]100, $true),
        $null)
    $engine.GetType().GetMethod(
        'RaiseOperationFailed',
        [Reflection.BindingFlags]'Instance,NonPublic').Invoke($engine, @($failure))
    $evidence = $buildEvidence.Invoke($recorder, @(0))
    $incidentId = $evidence['incidentId'].ToString()
    Assert-True (-not [string]::IsNullOrWhiteSpace($incidentId)) '首次指令失败必须生成事故编号。'
    Assert-True ([int]$evidence['retentionPolicy']['incidentPreSeconds'] -eq 180) '事故前置窗口应为3分钟。'
    Assert-True ([int]$evidence['retentionPolicy']['incidentPostSeconds'] -eq 60) '事故后置窗口应为60秒。'
    Assert-True (-not [bool]$evidence['completePostFailureWindow']) '故障刚发生时事故后置窗口不应标记为完整。'
    $failedEvent = $evidence['events'] | Where-Object { $_['eventName'].ToString() -eq 'operation.failed' } | Select-Object -First 1
    Assert-True ($null -ne $failedEvent) '证据包缺少指令失败事件。'
    Assert-True ($failedEvent['operationType'].ToString() -eq '延时') '失败事件必须保留真实 operaType。'

    $incidentPath = Join-Path "D:\AutomationLogs\Incident\$([DateTime]::UtcNow.ToString('yyyy-MM-dd'))" ($incidentId + '.json')
    $archiveDeadline = [DateTime]::UtcNow.AddSeconds(3)
    while ([DateTime]::UtcNow -lt $archiveDeadline -and -not (Test-Path -LiteralPath $incidentPath)) {
        Start-Sleep -Milliseconds 50
    }
    Assert-True (Test-Path -LiteralPath $incidentPath) '首次故障快照必须异步归档到统一事故日志目录。'

    $revisionBeforeDispose = [long]$recorderType.GetProperty('Revision').GetValue($recorder)
    ([IDisposable]$recorder).Dispose()
    Assert-True ($engine.StartProc($proc, 0)) '解除黑匣子订阅后流程仍应能够启动。'
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    while ([DateTime]::UtcNow -lt $deadline -and $engine.GetSnapshot(0).State -ne [Automation.ProcRunState]::Stopped) {
        Start-Sleep -Milliseconds 20
    }
    Assert-True ($engine.GetSnapshot(0).State -eq [Automation.ProcRunState]::Stopped) '解除黑匣子订阅后的流程未按时结束。'
    Assert-True ([long]$recorderType.GetProperty('Revision').GetValue($recorder) -eq $revisionBeforeDispose) '黑匣子关闭后仍在接收运行事件。'
}
finally {
    if ($null -ne $recorder) {
        ([IDisposable]$recorder).Dispose()
    }
    $engine.Dispose()
    if (-not [string]::IsNullOrWhiteSpace($incidentPath)) {
        Start-Sleep -Milliseconds 300
        [IO.File]::Delete($incidentPath)
        [IO.File]::Delete($incidentPath + '.tmp')
    }
}

Write-Output '运行黑匣子回归通过：事故窗口有界归档，关闭后解除运行事件订阅。'
