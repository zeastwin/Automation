// 模块：编辑器 / 外壳。
// 职责范围：页面装配、菜单、工具栏、导航、生命周期和程序设置。
// 文件职责：维护编辑器初始化、辅助服务启动与关闭流程。
// 排查入口：编辑器关闭只释放 Bridge/MCP/页面资源，设备、通讯和引擎最终进入 PlatformShutdownCoordinator。

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmMain
    {
        private void FrmMain_Load(object sender, EventArgs e)
        {
            InitializePlatform();
        }

        private void FrmMain_Shown(object sender, EventArgs e)
        {
            EnsureAiInfrastructureStarted();
        }

        internal void InitializePlatform()
        {
            if (platformInitialized)
            {
                return;
            }
            if (platformInitializationStarted)
            {
                throw new InvalidOperationException("平台初始化已开始但尚未成功完成，禁止重复初始化。");
            }
            if (Thread.CurrentThread.ManagedThreadId != uiThreadId)
            {
                throw new InvalidOperationException("平台必须在创建 FrmMain 的 UI 线程初始化。");
            }
            platformInitializationStarted = true;
            try
            {
                PlatformRuntimeInitializer.Initialize(Runtime);
                AttachInitializedPlatform();
            }
            catch (Exception ex)
            {
                Runtime.Safety.StopAllProcesses($"平台初始化失败:{ex.Message}");
                TryStopMotion();
                throw;
            }
        }

        internal void EnsureAiInfrastructureStarted()
        {
            if (Interlocked.CompareExchange(ref aiInfrastructureStartState, 1, 0) != 0)
            {
                return;
            }
            try
            {
                if (!GooseConfigStorage.TryLoad(out GooseConfig aiConfig, out string aiConfigError))
                {
                    ReportAiInfrastructureUnavailable("EW-AI 配置不可用：" + aiConfigError);
                    return;
                }
                if (!GooseRuntimeEnvironment.TryValidate(aiConfig.GooseExecutablePath, out string runtimeError))
                {
                    ReportAiInfrastructureUnavailable(runtimeError);
                    return;
                }
                if (!GooseConfigStorage.TryApplyStartupSafetyDefaults(out string aiSafetyError))
                {
                    ReportAiInfrastructureUnavailable(aiSafetyError);
                    return;
                }
                if (!GooseRuntimeProvisioner.IsManagedContextAvailable
                    && !GooseRuntimeProvisioner.TryEnsureManagedContext(out string contextError))
                {
                    ReportAiInfrastructureUnavailable(contextError);
                    return;
                }
                automationBridgeHost.Start();
                StartMcpServerOnStartup();
            }
            catch (Exception ex)
            {
                dataRun?.Logger?.Log($"AI 基础设施启动失败:{ex.Message}", LogLevel.Error);
                if (frmInfo != null && !frmInfo.IsDisposed && dataRun?.Logger == null)
                {
                    frmInfo.PrintInfo($"AI 基础设施启动失败:{ex.Message}", FrmInfo.Level.Error);
                }
            }
        }

        private void ReportAiInfrastructureUnavailable(string message)
        {
            string scopedMessage = "AI 基础设施未启动：" + message;
            dataRun?.Logger?.Log(scopedMessage, LogLevel.Error);
            if (frmInfo != null && !frmInfo.IsDisposed && dataRun?.Logger == null)
            {
                frmInfo.PrintInfo(scopedMessage, FrmInfo.Level.Error);
            }
        }
        
        private async void StartMcpServerOnStartup()
        {
            string baseUri = GooseConfigStorage.CreateDefaultConfig().McpUri;
            string toolProfile = GooseConfigStorage.CreateDefaultConfig().ToolProfile;
            if (GooseConfigStorage.TryLoad(out GooseConfig config, out string loadError))
            {
                baseUri = config.McpUri;
                toolProfile = config.ToolProfile;
            }
            else if (frmInfo != null && !frmInfo.IsDisposed)
            {
                frmInfo.PrintInfo($"MCP Server：EW-AI 配置读取失败，使用默认 MCP 地址。{loadError}", FrmInfo.Level.Error);
            }

            try
            {
                string result = await automationMcpServerManager.EnsureStartedAsync(baseUri, toolProfile).ConfigureAwait(true);
                if (frmInfo != null && !frmInfo.IsDisposed)
                {
                    frmInfo.PrintInfo("MCP Server：" + result, FrmInfo.Level.Normal);
                }
            }
            catch (Exception ex)
            {
                if (frmInfo != null && !frmInfo.IsDisposed)
                {
                    frmInfo.PrintInfo("MCP Server 启动失败：" + ex.Message, FrmInfo.Level.Error);
                }
            }
        }

    
        private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (allowFinalClose)
            {
                return;
            }
            if (HideOnUserClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            if (e.CloseReason == CloseReason.UserClosing)
            {
                DialogResult result = MessageBox.Show("确认退出程序？", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            ShutdownPlatform();
            allowFinalClose = true;
        }

        internal void AllowFinalClose()
        {
            allowFinalClose = true;
        }

        internal void ShutdownPlatform()
        {
            if (Interlocked.Exchange(ref shutdownStarted, 1) != 0)
            {
                return;
            }

            RunEditorShutdownStage("解除编辑器事件", () =>
            {
                if (Runtime.ManualMotion != null)
                    Runtime.ManualMotion.CommandRejected -= HandleManualMotionRejected;
                if (Runtime.PlcRuntime != null)
                    Runtime.PlcRuntime.RuntimeEvent -= HandlePlcRuntimeEvent;
                if (Runtime.Communication != null)
                    Runtime.Communication.FramesDropped -= HandleCommunicationFramesDropped;
                if (Runtime.Devices != null)
                    Runtime.Devices.Faulted -= HandleDeviceFault;
                if (dataRun != null)
                    dataRun.SnapshotChanged -= CacheSnapshot;
            });
            Runtime.Safety.StopAllProcesses("系统关闭，停止所有流程。");
            RunEditorShutdownStage("关闭调试窗口", () =>
            {
                frmDataBreakpoints?.Close();
                frmRuntimeDiagnostics?.Close();
                frmPerformanceAnalysis?.Close();
                dataBreakpointService.BreakpointHit -= HandleDataBreakpointHit;
                dataBreakpointService.Dispose();
            });

            // Goose 必须先于 UI 线程和 Bridge 释放，避免后台权限请求同步回调已关闭的窗体。
            RunEditorShutdownStage("关闭Goose客户端", () => frmAiAssistant?.DisposeGooseClient());
            RunEditorShutdownStage("关闭MCP Server", () => automationMcpServerManager?.Dispose());
            RunEditorShutdownStage("关闭Bridge Host", () => automationBridgeHost?.Stop());
            RunEditorShutdownStage("释放编辑器诊断资源", () =>
            {
                runtimeBlackBoxRecorder?.Dispose();
                runtimeBlackBoxRecorder = null;
                Runtime.RuntimeBlackBoxRecorder = null;
                processTraceAuditSink?.Dispose();
            });

            Runtime.ShutdownCoordinator.Shutdown();
            RunEditorShutdownStage("释放编辑器计时器", () =>
            {
                if (snapshotTimer == null) return;
                snapshotTimer.Stop();
                snapshotTimer.Tick -= SnapshotTimer_Tick;
                snapshotTimer.Dispose();
                snapshotTimer = null;
            });
            RunEditorShutdownStage("释放UI调度器", () => uiDispatcher?.Dispose());
            platformInitialized = false;
        }

        private void RunEditorShutdownStage(string stageName, Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                string message = $"关闭阶段[{stageName}]失败:{ex.Message}";
                if (dataRun?.Logger != null) dataRun.Logger.Log(message, LogLevel.Error);
                else Debug.WriteLine(message);
            }
        }

    }
}
