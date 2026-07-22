$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$violations = New-Object System.Collections.Generic.List[string]
Get-ChildItem -LiteralPath $repoRoot -Filter *.cs -Recurse | Where-Object {
    $_.FullName -notmatch '\\(bin|obj|Tests|packages)\\'
} | ForEach-Object {
    Select-String -LiteralPath $_.FullName -Pattern '\bSF\s*\.' | ForEach-Object {
        $violations.Add("$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
    foreach ($pattern in @(
        "Application.MessageLoop",
        "Application.DoEvents"))
    {
        Select-String -LiteralPath $_.FullName -SimpleMatch $pattern | ForEach-Object {
            $violations.Add("$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
        }
    }
}

$sfPath = Join-Path $repoRoot "SF.cs"
if (Test-Path -LiteralPath $sfPath)
{
    $violations.Add("全局容器文件仍然存在：$sfPath")
}
$projectPath = Join-Path $repoRoot "Automation.csproj"
Select-String -LiteralPath $projectPath -SimpleMatch 'Compile Include="SF.cs"' | ForEach-Object {
    $violations.Add("$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
}
Select-String -LiteralPath $projectPath -Pattern '(Compile|Content|None) Include="IntentTemplates' | ForEach-Object {
    $violations.Add("项目文件仍包含已退役 IntentTemplates：$($_.Path):$($_.LineNumber)")
}

$bridgePath = Join-Path $repoRoot "Bridge\AutomationBridgeService.cs"
$bridgeFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot "Bridge") -Filter "AutomationBridgeService*.cs"
foreach ($retiredRoute in @(
    "/bridge/intent/list_templates",
    "/bridge/intent/get_template",
    "/bridge/intent/build_patch",
    "/bridge/patch/preview_intent",
    "/bridge/patch/apply_intent",
    "/bridge/patch/preview_patch",
    "/bridge/patch/apply_patch",
    "/bridge/proc/create_batch"))
{
    $bridgeFiles | Select-String -SimpleMatch $retiredRoute | ForEach-Object {
        $violations.Add("Bridge 仍暴露已退役写入路由：$($_.Line.Trim())")
    }
}

foreach ($requiredBridgeModule in @(
    "Bridge\AutomationBridgeService.ChangeSet.cs",
    "Bridge\AutomationBridgeService.DataStructures.cs",
    "Bridge\AutomationBridgeService.AuditDiagnostics.cs",
    "Bridge\AutomationBridgeService.HardwareResources.cs",
    "Bridge\AutomationBridgeService.IoResources.cs",
    "Bridge\AutomationBridgeService.Migration.cs",
    "Bridge\AutomationBridgeService.PreviewState.cs",
    "Bridge\AutomationBridgeService.ProcessCommands.cs",
    "Bridge\AutomationBridgeService.ProcessInspection.cs",
    "Bridge\AutomationBridgeService.ProcessProjection.cs",
    "Bridge\AutomationBridgeService.ProcessQueries.cs",
    "Bridge\AutomationBridgeService.Protocol.cs",
    "Bridge\AutomationBridgeService.ProtocolSupport.cs",
    "Bridge\AutomationBridgeService.ReferenceDiagnostics.cs",
    "Bridge\AutomationBridgeService.Routing.cs",
    "Bridge\AutomationBridgeService.RuntimeDiagnostics.cs",
    "Bridge\AutomationBridgeService.OperationProjection.cs",
    "Bridge\AutomationBridgeService.Stations.cs",
    "Bridge\AutomationBridgeService.ValueConversion.cs",
    "Bridge\AutomationBridgeService.Variables.cs"))
{
    $modulePath = Join-Path $repoRoot $requiredBridgeModule
    if (-not (Test-Path -LiteralPath $modulePath))
    {
        $violations.Add("Bridge 模块缺失：$modulePath")
    }
}
foreach ($movedHandler in @(
    "private JObject HandlePreviewChangeSet",
    "private JObject HandleApplyChangeSet",
    "private JObject HandleGetMigrationConfiguration",
    "private JObject HandleApplyMigrationConfiguration",
    "private JObject HandleGetRuntimeSnapshot",
    "private JObject HandleDiagnoseIssue",
    "private JObject HandleGetOperationDetail",
    "private JObject HandleValidateProc",
    "private JObject HandleListProcs",
    "private JObject HandleControlProc",
    "private JObject HandleListVariables",
    "private JObject HandleListStations",
    "private JObject HandleListDataStructs",
    "private JObject HandleListIo",
    "private JObject HandleListPlcDevices",
    "private JObject BuildProcOverview"))
{
    Select-String -LiteralPath $bridgePath -SimpleMatch $movedHandler | ForEach-Object {
        $violations.Add("已拆分的 Bridge handler 回流主文件：$($_.Path):$($_.LineNumber)")
    }
}

$bridgeMainLineCount = (Get-Content -LiteralPath $bridgePath).Count
if ($bridgeMainLineCount -gt 250)
{
    $violations.Add("Bridge 主文件重新膨胀：当前 $bridgeMainLineCount 行，组合根上限为 250 行。")
}
foreach ($bridgeModule in Get-ChildItem -LiteralPath (Join-Path $repoRoot "Bridge") -Filter "AutomationBridgeService*.cs")
{
    $lineCount = (Get-Content -LiteralPath $bridgeModule.FullName).Count
    if ($lineCount -gt 1000)
    {
        $violations.Add("Bridge 职责文件重新膨胀：$($bridgeModule.Name) 当前 $lineCount 行，上限为 1000 行。")
    }
}
foreach ($retiredBridgeFile in @(
    "Bridge\AutomationBridgeService.Diagnostics.cs",
    "Bridge\AutomationBridgeService.Serialization.cs"))
{
    $retiredPath = Join-Path $repoRoot $retiredBridgeFile
    if (Test-Path -LiteralPath $retiredPath)
    {
        $violations.Add("Bridge 聚合文件已退役但仍存在：$retiredPath")
    }
}
$bridgeChangeSetPath = Join-Path $repoRoot "Bridge\AutomationBridgeService.ChangeSet.cs"
foreach ($pattern in @(
    "ProcessVariableConfigurationTransaction.Commit(",
    "runtime.Stores.Values.ReplaceConfiguration(",
    "runtime.EditorUi.RefreshProcesses("))
{
    Select-String -LiteralPath $bridgeChangeSetPath -SimpleMatch $pattern | ForEach-Object {
        $violations.Add("Bridge ChangeSet 绕过流程-变量联合提交服务：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
}

$runtimeContractsProject = Join-Path $repoRoot "Automation.Runtime.Contracts\Automation.Runtime.Contracts.csproj"
if (-not (Test-Path -LiteralPath $runtimeContractsProject))
{
    $violations.Add("运行时契约程序集缺失：$runtimeContractsProject")
}
else
{
    $runtimeContractsRoot = Split-Path -Parent $runtimeContractsProject
    Get-ChildItem -LiteralPath $runtimeContractsRoot -Filter *.cs -Recurse | Where-Object {
        $_.FullName -notmatch '\\(bin|obj)\\'
    } | ForEach-Object {
        foreach ($pattern in @("System.Windows.Forms", "Newtonsoft.Json", '\bFrm[A-Z]', '\bPlatformRuntime\b'))
        {
            Select-String -LiteralPath $_.FullName -Pattern $pattern | ForEach-Object {
                $violations.Add("运行时契约程序集含实现依赖：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
            }
        }
    }
    $runtimeContractsProjectSource = Get-Content -LiteralPath $runtimeContractsProject -Raw
    if ($runtimeContractsProjectSource -match '<ProjectReference|UseWindowsForms|System\.Windows\.Forms')
    {
        $violations.Add("Automation.Runtime.Contracts 不得引用实现项目或 WinForms。")
    }
}
if (-not (Select-String -LiteralPath $projectPath -SimpleMatch 'Automation.Runtime.Contracts\Automation.Runtime.Contracts.csproj'))
{
    $violations.Add("主程序缺少 Automation.Runtime.Contracts 项目引用。")
}

foreach ($retiredArchitectureName in @(
    "ProcessDefinitionStore",
    "ProcessConfigStore",
    "ProcessEngineStore",
    "IProcessEngineStore",
    "AiConfigurationTransaction"))
{
    Get-ChildItem -LiteralPath $repoRoot -File -Recurse | Where-Object {
        $isSourceFile = $_.Extension -eq ".cs" -or $_.Extension -eq ".csproj"
        $isSourceFile -and $_.Name -ne "nul" -and $_.FullName -notmatch '\\(bin|obj|Tests|packages)\\'
    } | Select-String -Pattern "\b$retiredArchitectureName\b" | ForEach-Object {
        $violations.Add("已退役架构名称仍存在：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
}

$ioDebugFormPath = Join-Path $repoRoot "FrmIODebug.cs"
foreach ($pattern in @("GetInIO(", "GetOutIO(", "UpdateIoCacheIfNeeded", "TryResolveIoByName"))
{
    Select-String -LiteralPath $ioDebugFormPath -SimpleMatch $pattern | ForEach-Object {
        $violations.Add("FrmIODebug 重新承担设备读取或 IO 索引：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
}
$ioDebugMonitorPath = Join-Path $repoRoot "UI\IoDebugMonitorService.cs"
if (-not (Test-Path -LiteralPath $ioDebugMonitorPath))
{
    $violations.Add("IO 调试监视服务缺失：$ioDebugMonitorPath")
}
$ioDebugEditorPath = Join-Path $repoRoot "UI\IoDebugConfigurationEditorService.cs"
$ioDebugModelsPath = Join-Path $repoRoot "Stores\IoDebugModels.cs"
foreach ($requiredIoDebugFile in @($ioDebugEditorPath, $ioDebugModelsPath))
{
    if (-not (Test-Path -LiteralPath $requiredIoDebugFile))
    {
        $violations.Add("IO 调试配置边界缺失：$requiredIoDebugFile")
    }
}
if (Test-Path -LiteralPath $ioDebugEditorPath)
{
    Select-String -LiteralPath $ioDebugEditorPath -Pattern "System.Windows.Forms|\bControl\b|\bForm\b" | ForEach-Object {
        $violations.Add("IO 调试配置服务反向依赖 WinForms：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
}
foreach ($pattern in @(
    "Stores.IoDebug.TryCommit(",
    "TrySaveIoDebugMap(",
    "CloneForDebug(",
    "class IODebugMap",
    "class IOConnect"))
{
    Select-String -LiteralPath $ioDebugFormPath -SimpleMatch $pattern | ForEach-Object {
        $violations.Add("FrmIODebug 重新承担配置提交、复制或模型定义：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
}

$variableEditorPath = Join-Path $repoRoot "UI\VariableEditorService.cs"
$processSelectionPath = Join-Path $repoRoot "UI\ProcessEditorSelectionState.cs"
foreach ($requiredEditorService in @($variableEditorPath, $processSelectionPath))
{
    if (-not (Test-Path -LiteralPath $requiredEditorService))
    {
        $violations.Add("编辑器应用服务缺失：$requiredEditorService")
    }
    else
    {
        Select-String -LiteralPath $requiredEditorService -Pattern "System.Windows.Forms|\bControl\b|\bForm\b" | ForEach-Object {
            $violations.Add("编辑器应用服务反向依赖 WinForms：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
        }
    }
}
$valueFormPath = Join-Path $repoRoot "FrmValue.cs"
foreach ($pattern in @("TryCommitConfiguration(", "BuildSaveData(", "setValueByIndex(", "VariableReferenceCatalog"))
{
    Select-String -LiteralPath $valueFormPath -SimpleMatch $pattern | ForEach-Object {
        $violations.Add("FrmValue 重新承担变量配置规则：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
}
$dataGridFormPath = Join-Path $repoRoot "FrmDataGrid.cs"
foreach ($pattern in @("Workspace.Proc.SelectedProcNum", "Workspace.Proc.SelectedStepNum", "Workspace.Proc.procsList", "Workspace.Proc?.SelectedProcNum", "Workspace.Proc?.SelectedStepNum"))
{
    Select-String -LiteralPath $dataGridFormPath -SimpleMatch $pattern | ForEach-Object {
        $violations.Add("FrmDataGrid 重新读取 FrmProc 的选择或流程数据：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
}
foreach ($pattern in @(
    "TryCommitProcDraft(",
    "RewriteGotoTargets(",
    "RewriteAddedOperationGotoTargetsFromPreviousLayout("))
{
    Select-String -LiteralPath $dataGridFormPath -SimpleMatch $pattern | ForEach-Object {
        $violations.Add("FrmDataGrid 重新承担指令草稿提交：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
}

$procFormPath = Join-Path $repoRoot "FrmProc.cs"
foreach ($pattern in @("ProcessVariableConfigurationTransaction", "TryCommitProcessVariableConfiguration"))
{
    Select-String -LiteralPath $procFormPath -SimpleMatch $pattern | ForEach-Object {
        $violations.Add("FrmProc 重新承担流程-变量联合事务：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
}

foreach ($requiredEditingService in @(
    "Engine\OperationEditingService.cs",
    "Runtime\ProcessVariableConfigurationService.cs",
    "Stores\ProcessVariableConfigurationTransaction.cs"))
{
    $servicePath = Join-Path $repoRoot $requiredEditingService
    if (-not (Test-Path -LiteralPath $servicePath))
    {
        $violations.Add("流程编辑服务缺失：$servicePath")
    }
}
$processRuntimeControlPath = Join-Path $repoRoot "Runtime\ProcessRuntimeControl.cs"
foreach ($unusedMember in @(
    "GetProcCount(",
    "TryGetProcName(",
    "TryGetProcState(",
    "TryGetSnapshot(",
    "StartProcAt(",
    "RunSingleOpOnce(",
    "Step("))
{
    Select-String -LiteralPath $processRuntimeControlPath -SimpleMatch $unusedMember | ForEach-Object {
        $violations.Add("运行控制契约重新加入无调用能力：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
}

$aiFormPath = Join-Path $repoRoot "FrmAiAssistant.cs"
foreach ($requiredAiModule in @(
    "FrmAiAssistant.WebTemplate.cs",
    "FrmAiAssistant.Rendering.cs",
    "FrmAiAssistant.Dialogs.cs",
    "Runtime\AiConversationCoordinator.cs",
    "Runtime\GooseAcpEventReader.cs",
    "UI\AiPreviewConfirmationCoordinator.cs",
    "Bridge\AutomationBridgePreviewClient.cs"))
{
    $aiModulePath = Join-Path $repoRoot $requiredAiModule
    if (-not (Test-Path -LiteralPath $aiModulePath))
    {
        $violations.Add("AI 助手职责文件缺失：$aiModulePath")
    }
}
foreach ($pattern in @(
    "runtime.Running =",
    "runtime.Status =",
    "runtime.Cancellation =",
    "promptCts",
    ".PromptAsync("))
{
    Select-String -LiteralPath $aiFormPath -SimpleMatch $pattern | ForEach-Object {
        $violations.Add("FrmAiAssistant 重新承担任务执行状态机：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
}
foreach ($pattern in @(
    "NamedPipeClientStream",
    "promptedPreviewIds",
    "List<AiConversation> conversations",
    "Dictionary<string, AiTaskRuntime> taskRuntimes",
    "private static string ExtractToolResultText"))
{
    Select-String -LiteralPath $aiFormPath -SimpleMatch $pattern | ForEach-Object {
        $violations.Add("FrmAiAssistant 重新承担会话存储或预演协议：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
}
$aiFormLineCount = (Get-Content -LiteralPath $aiFormPath).Count
if ($aiFormLineCount -gt 3000)
{
    $violations.Add("FrmAiAssistant 主文件重新膨胀：当前 $aiFormLineCount 行，上限为 3000 行。")
}
foreach ($nonUiCoordinator in @(
    "Runtime\AiConversationCoordinator.cs",
    "Runtime\GooseAcpEventReader.cs",
    "UI\AiPreviewConfirmationCoordinator.cs"))
{
    $coordinatorPath = Join-Path $repoRoot $nonUiCoordinator
    if (Test-Path -LiteralPath $coordinatorPath)
    {
        Select-String -LiteralPath $coordinatorPath -Pattern "System.Windows.Forms|\bControl\b|\bForm\b" | ForEach-Object {
            $violations.Add("AI 状态协调器反向依赖 WinForms：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
        }
    }
}
$retiredAiTaskCoordinatorPath = Join-Path $repoRoot "Runtime\AiTaskExecutionCoordinator.cs"
if (Test-Path -LiteralPath $retiredAiTaskCoordinatorPath)
{
    $violations.Add("AI 任务执行已并回会话协调器，独立协调器文件不得恢复：$retiredAiTaskCoordinatorPath")
}
Select-String -LiteralPath $projectPath -SimpleMatch "AiTaskExecutionCoordinator" | ForEach-Object {
    $violations.Add("项目文件重新包含独立 AI 任务协调器：$($_.Path):$($_.LineNumber)")
}

foreach ($requiredInspectorFile in @(
    "Inspector\InspectorCollectionFieldControls.cs",
    "Inspector\InspectorComboBoxControl.cs",
    "Inspector\InspectorControls.cs",
    "Inspector\InspectorFieldControls.cs",
    "Inspector\InspectorScalarFieldControl.cs",
    "Inspector\InspectorToggleControls.cs",
    "Inspector\InspectorValueCellControls.cs",
    "Inspector\InspectorValueReferenceFieldControl.cs",
    "Inspector\InspectorValueConversion.cs"))
{
    $inspectorPath = Join-Path $repoRoot $requiredInspectorFile
    if (-not (Test-Path -LiteralPath $inspectorPath))
    {
        $violations.Add("Inspector 职责文件缺失：$inspectorPath")
    }
}
$inspectorCollectionServicePath = Join-Path $repoRoot "Inspector\InspectorCollectionEditorService.cs"
if (Test-Path -LiteralPath $inspectorCollectionServicePath)
{
    $violations.Add("Inspector 集合编辑只服务一个控件，不得恢复独立 Service：$inspectorCollectionServicePath")
}
Select-String -LiteralPath $projectPath -SimpleMatch "InspectorCollectionEditorService" | ForEach-Object {
    $violations.Add("项目文件重新包含 Inspector 集合 Service：$($_.Path):$($_.LineNumber)")
}
Get-ChildItem -LiteralPath (Join-Path $repoRoot "Inspector") -Filter "Inspector*.cs" | ForEach-Object {
    $lineCount = (Get-Content -LiteralPath $_.FullName).Count
    if ($lineCount -gt 1000)
    {
        $violations.Add("Inspector 职责文件重新膨胀：$($_.Name) 当前 $lineCount 行，上限为 1000 行。")
    }
}
$inspectorViewPath = Join-Path $repoRoot "Inspector\InspectorView.cs"
$inspectorViewLineCount = (Get-Content -LiteralPath $inspectorViewPath).Count
if ($inspectorViewLineCount -gt 600)
{
    $violations.Add("InspectorView 重新膨胀：当前 $inspectorViewLineCount 行，上限为 600 行。")
}
$inspectorValuePath = Join-Path $repoRoot "Inspector\InspectorValueConversion.cs"
if (-not (Select-String -LiteralPath $inspectorValuePath -SimpleMatch "class InspectorFieldValueService"))
{
    $violations.Add("Inspector 字段写入服务缺失：$inspectorValuePath")
}
else
{
    Select-String -LiteralPath $ioDebugMonitorPath -Pattern "System.Windows.Forms|\bControl\b|\bForm\b" | ForEach-Object {
        $violations.Add("IO 调试监视服务反向依赖 WinForms：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
}

Get-ChildItem -LiteralPath $repoRoot -Filter "Frm*.cs" | ForEach-Object {
    Select-String -LiteralPath $_.FullName -SimpleMatch "AtomicJsonFileStore" | ForEach-Object {
        $violations.Add("窗体仍直接读写配置：$($_.Path):$($_.LineNumber)")
    }
}
Get-ChildItem -LiteralPath (Join-Path $repoRoot "Bridge") -Filter *.cs -Recurse | ForEach-Object {
    Select-String -LiteralPath $_.FullName -SimpleMatch "AtomicJsonFileStore" | ForEach-Object {
        $violations.Add("Bridge 仍绕过 Store 直接读写配置：$($_.Path):$($_.LineNumber)")
    }
}

$storesPath = Join-Path $repoRoot "Stores"
Get-ChildItem -LiteralPath $storesPath -Filter *.cs -Recurse | ForEach-Object {
    foreach ($pattern in @("System.Windows.Forms", "MessageBox", '\bFrm[A-Z]'))
    {
        Select-String -LiteralPath $_.FullName -Pattern $pattern | ForEach-Object {
            $violations.Add("Store 仍依赖 UI：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
        }
    }
}

$enginePath = Join-Path $repoRoot "Engine"
Get-ChildItem -LiteralPath $enginePath -Filter *.cs -Recurse | ForEach-Object {
    foreach ($pattern in @("System.Windows.Forms", '\bFrm[A-Z]', "UiInvoker"))
    {
        Select-String -LiteralPath $_.FullName -Pattern $pattern | ForEach-Object {
            $violations.Add("Engine 仍依赖具体 UI：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
        }
    }
}

$runtimeComposerPath = Join-Path $repoRoot "Runtime\PlatformRuntimeComposer.cs"
if (-not (Test-Path -LiteralPath $runtimeComposerPath))
{
    $violations.Add("平台内核组合器缺失：$runtimeComposerPath")
}

foreach ($runtimeCoreFile in @(
    "Runtime\PlatformRuntimeComposer.cs",
    "Runtime\PlatformRuntimeInitializer.cs",
    "Runtime\PlatformDeviceCoordinator.cs",
    "Runtime\PlatformSystemStatusService.cs"))
{
    $runtimeCorePath = Join-Path $repoRoot $runtimeCoreFile
    if (-not (Test-Path -LiteralPath $runtimeCorePath))
    {
        $violations.Add("运行时核心服务缺失：$runtimeCorePath")
        continue
    }
    foreach ($pattern in @("System.Windows.Forms", "MessageBox", '\bFrm[A-Z]'))
    {
        Select-String -LiteralPath $runtimeCorePath -Pattern $pattern | ForEach-Object {
            $violations.Add("运行时核心服务依赖 UI：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
        }
    }
}

$platformHostPath = Join-Path $repoRoot "Runtime\AutomationPlatformHost.cs"
$platformHostSource = Get-Content -LiteralPath $platformHostPath -Raw
$initializeMethod = [regex]::Match(
    $platformHostSource,
    'public bool Initialize\(out string error\)[\s\S]*?(?=\r?\n\s*public void ShowPlatformEditor\()')
if (-not $initializeMethod.Success)
{
    $violations.Add("无法定位 AutomationPlatformHost.Initialize，架构门禁需要同步更新。")
}
elseif ($initializeMethod.Value -match '\bFrmMain\b|EnsurePlatformEditorCreated|platformEditor')
{
    $violations.Add("AutomationPlatformHost.Initialize 重新依赖平台编辑器窗体。")
}
$mainFormPath = Join-Path $repoRoot "FrmMain.cs"
foreach ($pattern in @(
    "new EngineContext",
    "new ProcessEngine",
    "new MotionCtrl",
    "new SimulationGatewayClient",
    "new ManualMotionService"))
{
    Select-String -LiteralPath $mainFormPath -SimpleMatch $pattern | ForEach-Object {
        $violations.Add("FrmMain 重新承担内核组合：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
    }
}
$mainFormLineCount = (Get-Content -LiteralPath $mainFormPath).Count
if ($mainFormLineCount -gt 1100)
{
    $violations.Add("FrmMain 主文件重新膨胀：当前 $mainFormLineCount 行，上限为 1100 行。")
}
foreach ($requiredMainModule in @("FrmMain.Lifecycle.cs", "FrmMain.Navigation.cs"))
{
    $mainModulePath = Join-Path $repoRoot $requiredMainModule
    if (-not (Test-Path -LiteralPath $mainModulePath))
    {
        $violations.Add("FrmMain 职责文件缺失：$mainModulePath")
    }
}
$shutdownCoordinatorPath = Join-Path $repoRoot "Runtime\PlatformShutdownCoordinator.cs"
if (-not (Test-Path -LiteralPath $shutdownCoordinatorPath))
{
    $violations.Add("平台关闭协调器缺失：$shutdownCoordinatorPath")
}
foreach ($mainLifecycleFile in @("FrmMain.cs", "FrmMain.Lifecycle.cs"))
{
    $lifecyclePath = Join-Path $repoRoot $mainLifecycleFile
    foreach ($pattern in @("Communication.Dispose(", "PlcRuntime.Dispose(", "Devices.Dispose(", "ProcessEngine.Dispose(", "WaitForAllProcsStopped"))
    {
        Select-String -LiteralPath $lifecyclePath -SimpleMatch $pattern | ForEach-Object {
            $violations.Add("FrmMain 绕过统一关闭协调器：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
        }
    }
}

Get-ChildItem -LiteralPath $repoRoot -Filter *.cs -Recurse | Where-Object {
    $_.FullName -notmatch '\\(bin|obj|Tests|packages)\\'
} | ForEach-Object {
    foreach ($pattern in @("StopAllProcsForSafety", "using static Automation.FrmCard", "using static Automation.FrmProc"))
    {
        Select-String -LiteralPath $_.FullName -SimpleMatch $pattern | ForEach-Object {
            $violations.Add("遗留架构入口仍存在：$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
        }
    }
}

$motionPath = Join-Path $repoRoot "MotionControl"
Get-ChildItem -LiteralPath $motionPath -Filter *.cs -Recurse | ForEach-Object {
    foreach ($pattern in @("System.Windows.Forms", "MessageBox."))
    {
        Select-String -LiteralPath $_.FullName -SimpleMatch $pattern | ForEach-Object {
            $violations.Add("$($_.Path):$($_.LineNumber): $($_.Line.Trim())")
        }
    }
}

if ($violations.Count -gt 0)
{
    $violations | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "ArchitectureBoundaryRegression: PASS"
