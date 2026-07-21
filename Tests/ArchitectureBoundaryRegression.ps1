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
    Select-String -LiteralPath $bridgePath -SimpleMatch $retiredRoute | ForEach-Object {
        $violations.Add("Bridge 仍暴露已退役写入路由：$($_.Line.Trim())")
    }
}

foreach ($requiredBridgeModule in @(
    "Bridge\AutomationBridgeService.ChangeSet.cs",
    "Bridge\AutomationBridgeService.Diagnostics.cs",
    "Bridge\AutomationBridgeService.Migration.cs",
    "Bridge\AutomationBridgeService.ProcessInspection.cs"))
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
    "private JObject HandleValidateProc"))
{
    Select-String -LiteralPath $bridgePath -SimpleMatch $movedHandler | ForEach-Object {
        $violations.Add("已拆分的 Bridge handler 回流主文件：$($_.Path):$($_.LineNumber)")
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
