using System;
// 模块：运行时 / 宿主组合。
// 职责范围：负责平台入口、实例组合、初始化、路径和宿主对外生命周期。
// 排查入口：先看 RuntimeStatus/StateMessage，再沿 Initialize、SetState 或 ShutdownRuntimeCore 找到失败阶段。

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation
{
    public enum PlatformRuntimeState
    {
        Created = 0,
        Initializing = 1,
        Ready = 2,
        Faulted = 3,
        ShuttingDown = 4,
        Stopped = 5
    }

    public sealed class PlatformRuntimeStateChangedEventArgs : EventArgs
    {
        public PlatformRuntimeStateChangedEventArgs(PlatformRuntimeState state, string message)
        {
            State = state;
            Message = message ?? string.Empty;
        }

        public PlatformRuntimeState State { get; }
        public string Message { get; }
    }

    public sealed class PlatformProcessInfo
    {
        public int Index { get; set; }
        public Guid Id { get; set; }
        public string Name { get; set; }
        public ProcRunState State { get; set; }
        public bool Disabled { get; set; }
        public bool IsAlarm { get; set; }
        public string AlarmMessage { get; set; }

        public override string ToString()
        {
            return $"{Index} - {Name}";
        }
    }

    public sealed class PlatformValueSnapshot
    {
        public Guid Id { get; set; }
        public int Index { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public string Scope { get; set; }
        public Guid? OwnerProcId { get; set; }
        public string OwnerProcName { get; set; }
        public string Note { get; set; }
    }

    public sealed class AutomationPlatformHost : IAutomationPlatform
    {
        private readonly int uiThreadId = Thread.CurrentThread.ManagedThreadId;
        private readonly Dictionary<string, CustomFunc.FunctionDelegate> pendingCustomFunctions =
            new Dictionary<string, CustomFunc.FunctionDelegate>(StringComparer.Ordinal);
        private readonly PlatformRuntime runtime;
        private readonly IDisposable exceptionSafetyRegistration;
        private readonly WinFormsProcessInteractionCoordinator processInteraction;
        private FrmMain platformEditor;
        private PlatformRuntimeState state = PlatformRuntimeState.Created;
        private string deviceFaultMessage;
        private bool autoStartTriggered;
        private bool runtimeCoreStopped;
        private bool disposed;

        public AutomationPlatformHost()
        {
            runtime = new PlatformRuntime();
            processInteraction = new WinFormsProcessInteractionCoordinator(runtime);
            runtime.ProcessInteraction = processInteraction;
            exceptionSafetyRegistration = RuntimeExceptionLogger.RegisterSafetyCoordinator(runtime.Safety);
            Values = new PlatformValueStoreFacade(this);
            Processes = new PlatformProcessStoreFacade(this);
            Authentication = runtime.Accounts;
        }

        public event EventHandler<PlatformRuntimeStateChangedEventArgs> RuntimeStateChanged;
        public event Action<EngineSnapshot> ProcessSnapshotChanged;
        public event EventHandler<ValueChangedEventArgs> ValueChanged;
        public event EventHandler<PlatformRuntimeStatusChangedEventArgs> RuntimeStatusChanged;

        public IValueStore Values { get; }

        public IProcessStore Processes { get; }

        public IAuthenticationSession Authentication { get; }

        public string ApiVersion => PlatformApiInfo.ApiVersion;

        public string PlatformVersion => typeof(AutomationPlatformHost).Assembly
            .GetName().Version?.ToString() ?? "0.0.0.0";

        PlatformRuntimeStatus IAutomationPlatform.RuntimeStatus => (PlatformRuntimeStatus)(int)state;

        string IAutomationPlatform.RuntimeMessage => StateMessage;

        public string ConfigRoot
        {
            get
            {
                string configRoot = runtime.Paths.ConfigPath;
                if (string.IsNullOrWhiteSpace(configRoot))
                {
                    throw new InvalidOperationException("无法确定 Automation 配置目录。");
                }
                return Path.GetFullPath(configRoot);
            }
        }
        public PlatformRuntimeState State => state;
        public string StateMessage { get; private set; } = "尚未初始化";
        public bool IsPlatformVisible => platformEditor != null && !platformEditor.IsDisposed && platformEditor.Visible;

        internal PlatformRuntime Runtime => runtime;

        /// <summary>
        /// 将已经初始化的平台编辑器作为设备程序主窗口运行。
        /// 此入口只改变窗口关闭语义，不重复初始化平台。
        /// </summary>
        public Form PreparePlatformEditorMainWindow()
        {
            EnsureUiThread();
            EnsureReadyOrFaulted();
            FrmMain editor = EnsurePlatformEditorCreated();
            editor.HideOnUserClose = false;
            editor.Owner = null;
            editor.ShowInTaskbar = true;
            return editor;
        }

        /// <summary>
        /// 在 HMI 已显示且 UI 空闲时隐藏预加载平台编辑器。
        /// 这里创建并附加编辑器、提前创建主窗口句柄并启动分片缓存预热，
        /// 但不显示窗口，也不提前启动 AI/MCP。
        /// </summary>
        internal bool TryPreloadPlatformEditor(out string error)
        {
            error = null;
            try
            {
                EnsureUiThread();
                EnsureReadyOrFaulted();
                FrmMain editor = EnsurePlatformEditorCreated();
                editor.PrepareHiddenPreload();
                return true;
            }
            catch (Exception ex)
            {
                error = "平台编辑器隐藏预加载失败：" + ex.Message;
                runtime.ProcessEngine?.Logger?.Log(error, LogLevel.Error);
                return false;
            }
        }

        public void RegisterCustomFunction(string name, CustomFunc.FunctionDelegate function)
        {
            EnsureUiThread();
            if (state != PlatformRuntimeState.Created)
            {
                throw new InvalidOperationException("自定义方法必须在平台初始化前注册。");
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("自定义方法名称不能为空。", nameof(name));
            }
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }
            if (pendingCustomFunctions.ContainsKey(name))
            {
                throw new InvalidOperationException($"自定义方法重复注册:{name}");
            }
            pendingCustomFunctions.Add(name, function);
        }

        void IAutomationPlatform.RegisterCustomFunction(string name, Action function)
        {
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }
            RegisterCustomFunction(name, function.Invoke);
        }

        public bool Initialize(out string error)
        {
            EnsureUiThread();
            error = null;
            // Initialize 只允许 Created -> Initializing -> Ready/Faulted。
            // 若现场看到其他状态再次进入这里，应先追查宿主生命周期，不能重复组合一套运行时来掩盖问题。
            if (state == PlatformRuntimeState.Ready)
            {
                return true;
            }
            if (state != PlatformRuntimeState.Created)
            {
                error = $"当前状态禁止初始化:{state}";
                return false;
            }

            SetState(PlatformRuntimeState.Initializing, "正在初始化控制平台");
            try
            {
                ValidateConfigLayout();
                // 设备 HMI 会在平台组合前注册业务函数；必须先写入容器，再让流程加载和就绪检查解析这些函数。
                foreach (KeyValuePair<string, CustomFunc.FunctionDelegate> item in pendingCustomFunctions)
                {
                    runtime.CustomFunctions.RegisterFunction(item.Key, item.Value);
                }
                PlatformRuntimeComposition composition = PlatformRuntimeComposer.Compose(
                    runtime,
                    processInteraction,
                    processInteraction,
                    new LocalFileLogger(@"D:\AutomationLogs\ProcessLog"));
                // Compose 只建立对象图，Initializer 才恢复事务、加载配置并启动设备；排障时不要把两个阶段混为一谈。
                processInteraction.AttachEngine(composition.ProcessEngine);
                composition.ProcessEngine.SnapshotChanged += OnProcessSnapshotChanged;
                runtime.Devices.Faulted += OnDeviceFaulted;
                runtime.Stores.Values.ValueChanged += OnValueChanged;
                PlatformRuntimeInitializer.Initialize(runtime);
                MonitorSystemValue("复位状态");
                MonitorSystemValue("系统状态");

                SetState(PlatformRuntimeState.Ready, BuildReadyStateMessage());
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                SetState(PlatformRuntimeState.Faulted, $"平台初始化失败:{ex.Message}");
                try
                {
                    // 初始化可能只完成了一半，仍统一走幂等关闭链释放已经创建的资源。
                    ShutdownRuntimeCore();
                }
                catch
                {
                }
                return false;
            }
        }

        public void ShowPlatformEditor()
        {
            EnsureUiThread();
            EnsureReadyOrFaulted();
            FrmMain editor = EnsurePlatformEditorCreated();
            try
            {
                editor.HideOnUserClose = true;
                editor.Owner = null;
                editor.ShowInTaskbar = true;
                if (!editor.Visible)
                {
                    editor.Show();
                }
                if (editor.WindowState == FormWindowState.Minimized)
                {
                    editor.WindowState = FormWindowState.Maximized;
                }
                editor.BringToFront();
                editor.Activate();
                NotifyInteractionUiReady();
            }
            catch (ObjectDisposedException)
            {
                platformEditor = null;
                throw;
            }
        }

        public void NotifyInteractionUiReady()
        {
            EnsureUiThread();
            EnsureReadyOrFaulted();
            processInteraction.NotifyUiReady();
            TryStartAutoProcesses();
        }

        public void HidePlatformEditor()
        {
            EnsureUiThread();
            if (platformEditor != null && !platformEditor.IsDisposed)
            {
                platformEditor.Hide();
            }
        }

        public IReadOnlyList<PlatformProcessInfo> GetProcesses()
        {
            EnsureReadyOrFaulted();
            List<PlatformProcessInfo> result = new List<PlatformProcessInfo>();
            IList<Proc> procs = runtime.ProcessEngine?.Context?.Procs;
            if (procs == null)
            {
                return result;
            }
            for (int i = 0; i < procs.Count; i++)
            {
                Proc proc = procs[i];
                EngineSnapshot snapshot = runtime.ProcessEngine.GetSnapshot(i);
                result.Add(new PlatformProcessInfo
                {
                    Index = i,
                    Id = proc?.head?.Id ?? Guid.Empty,
                    Name = string.IsNullOrWhiteSpace(proc?.head?.Name) ? $"流程{i}" : proc.head.Name,
                    State = snapshot?.State ?? ProcRunState.Ready,
                    Disabled = proc?.head?.Disable == true,
                    IsAlarm = snapshot?.IsAlarm == true,
                    AlarmMessage = snapshot?.AlarmMessage ?? string.Empty
                });
            }
            return result;
        }

        public bool TryStartProcess(int procIndex, out string error)
        {
            if (!runtime.Accounts.Authorize(
                PlatformPermissionCodes.ProcessRun,
                "启动流程",
                out error))
            {
                return false;
            }
            if (!CanIssueRuntimeCommand(out error))
            {
                return false;
            }
            IList<Proc> procs = runtime.ProcessEngine?.Context?.Procs;
            if (procs == null || procIndex < 0 || procIndex >= procs.Count || procs[procIndex] == null)
            {
                error = $"流程索引无效:{procIndex}";
                return false;
            }
            if (procs[procIndex].head?.Disable == true)
            {
                error = $"流程已禁用:{procIndex}";
                return false;
            }
            if (!runtime.ProcessEngine.TryValidateProcessInactive(procIndex, out error))
            {
                return false;
            }
            ProcessReadinessAnalysis readiness = ProcessReadinessService.Analyze(
                procIndex, procs[procIndex], procs,
                runtime.CreateProcessValidationContext(), runtime.Stores.Values);
            if (!readiness.Runnable)
            {
                error = "流程配置尚不可运行：" + string.Join("；", readiness.RunBlockers);
                return false;
            }
            if (!runtime.ProcessControl.StartProc(procIndex))
            {
                error = runtime.ProcessEngine.TryValidateProcessInactive(procIndex, out string inactiveError)
                    ? $"流程启动请求未被内核接受:{procIndex}"
                    : inactiveError;
                return false;
            }
            return true;
        }

        public bool TryPauseProcess(int procIndex, out string error)
        {
            if (!runtime.Accounts.Authorize(
                PlatformPermissionCodes.ProcessRun,
                "暂停流程",
                out error))
            {
                return false;
            }
            return TryExecuteProcessCommand(procIndex, index => runtime.ProcessControl != null && runtime.ProcessControl.Pause(index), "暂停", false, out error);
        }

        public bool TryResumeProcess(int procIndex, out string error)
        {
            if (!runtime.Accounts.Authorize(
                PlatformPermissionCodes.ProcessRun,
                "继续流程",
                out error))
            {
                return false;
            }
            return TryExecuteProcessCommand(procIndex, index => runtime.ProcessControl != null && runtime.ProcessControl.Resume(index), "继续", true, out error);
        }

        public bool TryStopProcess(int procIndex, out string error)
        {
            return TryExecuteProcessCommand(procIndex, index => runtime.ProcessControl != null && runtime.ProcessControl.Stop(index), "停止", false, out error);
        }

        public bool TryStopAllProcesses(out string error)
        {
            error = null;
            if (state == PlatformRuntimeState.Created || state == PlatformRuntimeState.Stopped)
            {
                error = $"当前状态禁止停止流程:{state}";
                return false;
            }
            IReadOnlyList<PlatformProcessInfo> processes = GetProcesses();
            bool success = true;
            List<string> failures = new List<string>();
            foreach (PlatformProcessInfo process in processes)
            {
                if (runtime.ProcessControl == null || !runtime.ProcessControl.Stop(process.Index))
                {
                    success = false;
                    failures.Add(process.Index.ToString(CultureInfo.InvariantCulture));
                }
            }
            if (!success)
            {
                error = "以下流程停止失败:" + string.Join(",", failures);
            }
            return success;
        }

        public bool TryGetValue(string name, out PlatformValueSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = null;
            if (state != PlatformRuntimeState.Ready && state != PlatformRuntimeState.Faulted)
            {
                error = $"当前状态禁止读取变量:{state}";
                return false;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "变量名称不能为空。";
                return false;
            }
            if (runtime.Stores.Values == null || !runtime.Stores.Values.TryGetValueByName(name, out DicValue value) || value == null)
            {
                error = $"变量不存在:{name}";
                return false;
            }
            snapshot = new PlatformValueSnapshot
            {
                Index = value.Index,
                Id = value.Id,
                Name = value.Name,
                Type = value.Type,
                Value = value.Value,
                Scope = value.Scope,
                OwnerProcId = value.OwnerProcId,
                OwnerProcName = ResolveOwnerProcessName(value.OwnerProcId),
                Note = value.Note
            };
            return true;
        }

        public void ShowRuntimeDiagnostics()
        {
            EnsureUiThread();
            EnsureReadyOrFaulted();
            if (!runtime.Accounts.Authorize(
                PlatformPermissionCodes.PlatformDiagnosticsUse,
                "打开运行诊断",
                out string permissionError))
            {
                throw new UnauthorizedAccessException(permissionError);
            }
            EnsurePlatformEditorCreated().ShowRuntimeDiagnostics();
        }

        public void ShowPerformanceAnalysis()
        {
            EnsureUiThread();
            EnsureReadyOrFaulted();
            if (!runtime.Accounts.Authorize(
                PlatformPermissionCodes.PlatformDiagnosticsUse,
                "打开性能分析",
                out string permissionError))
            {
                throw new UnauthorizedAccessException(permissionError);
            }
            EnsurePlatformEditorCreated().ShowPerformanceAnalysis();
        }

        public bool TryGetValue(int index, out PlatformValueSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = null;
            if (state != PlatformRuntimeState.Ready && state != PlatformRuntimeState.Faulted)
            {
                error = $"当前状态禁止读取变量:{state}";
                return false;
            }
            if (runtime.Stores.Values == null || !runtime.Stores.Values.TryGetValueByIndex(index, out DicValue value) || value == null)
            {
                error = $"变量索引不存在:{index}";
                return false;
            }
            snapshot = new PlatformValueSnapshot
            {
                Index = value.Index,
                Id = value.Id,
                Name = value.Name,
                Type = value.Type,
                Value = value.Value,
                Scope = value.Scope,
                OwnerProcId = value.OwnerProcId,
                OwnerProcName = ResolveOwnerProcessName(value.OwnerProcId),
                Note = value.Note
            };
            return true;
        }

        public IReadOnlyList<string> GetValueNames()
        {
            EnsureReadyOrFaulted();
            return runtime.Stores.Values?.GetValueNames() ?? new List<string>();
        }

        internal string ResolveOwnerProcessName(Guid? ownerProcId)
        {
            if (!ownerProcId.HasValue) return string.Empty;
            return (runtime.Stores.Processes?.Items ?? new List<Proc>())
                .FirstOrDefault(proc => proc?.head?.Id == ownerProcId.Value)?.head?.Name ?? string.Empty;
        }

        public bool TrySetValue(string name, object value, out string error)
        {
            if (!runtime.Accounts.Authorize(
                PlatformPermissionCodes.VariableRuntimeWrite,
                "写入运行变量",
                out error))
            {
                return false;
            }
            if (!CanIssueRuntimeCommand(out error))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "变量名称不能为空。";
                return false;
            }
            if (value == null)
            {
                error = "变量值不能为空。";
                return false;
            }
            if (runtime.Stores.Values == null || !runtime.Stores.Values.setValueByName(name, value, "HMI 代码"))
            {
                error = $"变量写入失败:{name}";
                return false;
            }
            return true;
        }

        public bool TrySetValue(int index, object value, out string error)
        {
            if (!runtime.Accounts.Authorize(
                PlatformPermissionCodes.VariableRuntimeWrite,
                "写入运行变量",
                out error))
            {
                return false;
            }
            if (!CanIssueRuntimeCommand(out error))
            {
                return false;
            }
            if (index < 0 || index >= ValueConfigStore.ValueCapacity)
            {
                error = $"变量索引超出范围:{index}";
                return false;
            }
            if (value == null)
            {
                error = "变量值不能为空。";
                return false;
            }
            if (runtime.Stores.Values == null || !runtime.Stores.Values.setValueByIndex(index, value, "HMI 代码"))
            {
                error = $"变量写入失败:{index}";
                return false;
            }
            return true;
        }

        public bool TryMonitorValue(string name, bool enabled, out string error)
        {
            error = null;
            if (state != PlatformRuntimeState.Ready
                && state != PlatformRuntimeState.Initializing
                && state != PlatformRuntimeState.Faulted)
            {
                error = $"当前状态禁止设置变量监控:{state}";
                return false;
            }
            if (runtime.Stores.Values == null || !runtime.Stores.Values.TryGetValueByName(name, out DicValue value) || value == null)
            {
                error = $"变量不存在:{name}";
                return false;
            }
            runtime.Stores.Values.SetMonitorFlag(value.Index, enabled);
            runtime.Stores.Values.SetMonitorEnabled(true);
            return true;
        }

        public void Shutdown()
        {
            EnsureUiThread();
            if (state == PlatformRuntimeState.Stopped || state == PlatformRuntimeState.ShuttingDown)
            {
                return;
            }
            if (state == PlatformRuntimeState.Created)
            {
                Authentication.Logout();
                ShutdownRuntimeCore();
                SetState(PlatformRuntimeState.Stopped, "控制平台已关闭");
                return;
            }
            SetState(PlatformRuntimeState.ShuttingDown, "正在安全关闭控制平台");
            Authentication.Logout();
            DetachRuntimeEvents();
            try
            {
                if (platformEditor != null && !platformEditor.IsDisposed)
                {
                    platformEditor.ShutdownPlatform();
                    platformEditor.AllowFinalClose();
                    platformEditor.Close();
                    platformEditor.Dispose();
                }
            }
            finally
            {
                try
                {
                    ShutdownRuntimeCore();
                }
                finally
                {
                    SetState(PlatformRuntimeState.Stopped, "控制平台已关闭");
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            try
            {
                Shutdown();
            }
            finally
            {
                exceptionSafetyRegistration.Dispose();
            }
        }

        private bool TryExecuteProcessCommand(int procIndex, Func<int, bool> command, string action, bool requireWritableState, out string error)
        {
            error = null;
            if (requireWritableState && !CanIssueRuntimeCommand(out error))
            {
                return false;
            }
            if (!requireWritableState && state != PlatformRuntimeState.Ready && state != PlatformRuntimeState.Faulted)
            {
                error = $"当前状态禁止{action}流程:{state}";
                return false;
            }
            if (command == null || !command(procIndex))
            {
                error = $"流程{action}失败:{procIndex}";
                return false;
            }
            return true;
        }

        private bool CanIssueRuntimeCommand(out string error)
        {
            error = null;
            if (state != PlatformRuntimeState.Ready)
            {
                error = $"当前状态禁止控制操作:{state}";
                return false;
            }
            if (runtime.Maintenance.Active)
            {
                error = string.IsNullOrWhiteSpace(runtime.Maintenance.Reason)
                    ? "系统正在执行配置维护。"
                    : $"系统正在执行配置维护:{runtime.Maintenance.Reason}";
                return false;
            }
            if (runtime.Safety.IsLocked)
            {
                error = string.IsNullOrWhiteSpace(runtime.Safety.LockReason)
                    ? "系统处于安全锁定状态。"
                    : $"系统处于安全锁定状态:{runtime.Safety.LockReason}";
                return false;
            }
            if (runtime.Readiness.ProcConfigFaulted)
            {
                error = "流程配置异常，禁止控制操作。";
                return false;
            }
            if (runtime.Readiness.VersionRestartRequired)
            {
                error = "配置版本已还原，必须重启程序后才能继续操作。";
                return false;
            }
            return true;
        }

        private void MonitorSystemValue(string name)
        {
            if (!TryMonitorValue(name, true, out string error))
            {
                // 变量配置可能损坏或系统保留区已满，此时运行值监控不可用，但编辑器正是
                // 修复该配置的入口。记录原始原因并继续完成宿主初始化。
                runtime.ProcessEngine?.Logger?.Log(
                    $"系统变量监控未启用，平台编辑器继续启动:{error}",
                    LogLevel.Error);
            }
        }

        private void OnProcessSnapshotChanged(EngineSnapshot snapshot)
        {
            DispatchSdkEvent(() => ProcessSnapshotChanged?.Invoke(snapshot));
        }

        private void OnValueChanged(object sender, ValueChangedEventArgs e)
        {
            DispatchSdkEvent(() => ValueChanged?.Invoke(this, e));
        }

        private void OnDeviceFaulted(string message)
        {
            if (state == PlatformRuntimeState.ShuttingDown || state == PlatformRuntimeState.Stopped)
            {
                return;
            }
            deviceFaultMessage = string.IsNullOrWhiteSpace(message)
                ? "设备运行时发生故障。"
                : message.Trim();
            runtime.ProcessEngine?.Logger?.Log(
                $"设备能力不可用，平台编辑和非设备能力继续运行:{deviceFaultMessage}",
                LogLevel.Error);

            // Faulted 只表示平台对象图或核心配置未能完成初始化。设备初始化、急停、限位等
            // 运行故障由设备协调器和 Safety/Readiness 收窄受影响能力，不能关闭编辑器修复入口。
            if (state == PlatformRuntimeState.Ready)
            {
                SetState(PlatformRuntimeState.Ready, BuildReadyStateMessage());
            }
        }

        private string BuildReadyStateMessage()
        {
            if (runtime.Safety.IsLocked)
            {
                return $"平台已初始化，但处于安全锁定状态:{runtime.Safety.LockReason}";
            }
            if (runtime.Readiness.ProcConfigFaulted)
            {
                return "平台已初始化，但流程配置异常；所有流程已停止且禁止启动，请打开编辑器修复流程配置。";
            }
            if (!string.IsNullOrWhiteSpace(deviceFaultMessage))
            {
                return $"平台已就绪；设备能力暂不可用，但编辑器和非设备功能可继续使用:{deviceFaultMessage}";
            }
            return "平台已就绪";
        }

        private void SetState(PlatformRuntimeState nextState, string message)
        {
            state = nextState;
            StateMessage = message ?? string.Empty;
            DispatchSdkEvent(() =>
            {
                RuntimeStateChanged?.Invoke(this, new PlatformRuntimeStateChangedEventArgs(nextState, StateMessage));
                RuntimeStatusChanged?.Invoke(this, new PlatformRuntimeStatusChangedEventArgs(
                    (PlatformRuntimeStatus)(int)nextState, StateMessage));
            });
        }

        private void DispatchSdkEvent(Action callback)
        {
            if (callback == null)
            {
                return;
            }
            void InvokeSafely()
            {
                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    runtime.ProcessEngine?.Logger?.Log($"设备 SDK 事件处理异常:{ex.Message}", LogLevel.Error);
                }
            }

            if (Thread.CurrentThread.ManagedThreadId == uiThreadId)
            {
                InvokeSafely();
                return;
            }
            processInteraction.Post(InvokeSafely);
        }

        private void EnsureReadyOrFaulted()
        {
            if (state != PlatformRuntimeState.Ready && state != PlatformRuntimeState.Faulted)
            {
                throw new InvalidOperationException($"平台尚未初始化，当前状态:{state}");
            }
        }

        private FrmMain EnsurePlatformEditorCreated()
        {
            EnsureUiThread();
            if (runtimeCoreStopped || runtime.ProcessEngine == null)
            {
                throw new InvalidOperationException(
                    $"平台运行时未完成初始化，无法创建编辑器:{StateMessage}");
            }
            if (platformEditor != null && !platformEditor.IsDisposed)
            {
                return platformEditor;
            }
            FrmMain editor = new FrmMain(runtime)
            {
                HideOnUserClose = true
            };
            try
            {
                editor.AttachInitializedPlatform();
                platformEditor = editor;
                return editor;
            }
            catch
            {
                editor.Dispose();
                throw;
            }
        }

        private void TryStartAutoProcesses()
        {
            ProcessEngine engine = runtime.ProcessEngine;
            IList<Proc> procs = engine?.Context?.Procs;
            if (engine == null || procs == null || !engine.TryValidateStartGate(out _))
            {
                autoStartTriggered = false;
                return;
            }
            if (autoStartTriggered)
            {
                return;
            }
            autoStartTriggered = true;
            for (int i = 0; i < procs.Count; i++)
            {
                Proc proc = procs[i];
                if (proc?.head?.AutoStart != true || proc.head.Disable)
                {
                    continue;
                }
                EngineSnapshot snapshot = engine.GetSnapshot(i);
                if (snapshot == null || snapshot.State.IsInactive())
                {
                    engine.StartProcAuto(proc, i);
                }
            }
        }

        private void ShutdownRuntimeCore()
        {
            // 先解除外部事件，避免关闭期间的设备/变量回调再次触发 UI 或宿主逻辑。
            DetachRuntimeEvents();
            if (runtimeCoreStopped)
            {
                return;
            }
            runtimeCoreStopped = true;
            runtime.ShutdownCoordinator.Shutdown();
        }

        private void DetachRuntimeEvents()
        {
            if (runtime.ProcessEngine != null)
            {
                runtime.ProcessEngine.SnapshotChanged -= OnProcessSnapshotChanged;
            }
            runtime.Stores.Values.ValueChanged -= OnValueChanged;
            if (runtime.Devices != null)
            {
                runtime.Devices.Faulted -= OnDeviceFaulted;
            }
        }

        private void EnsureUiThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != uiThreadId)
            {
                throw new InvalidOperationException("平台生命周期和窗口操作必须在创建 Host 的 UI 线程执行。");
            }
        }

        private void ValidateConfigLayout()
        {
            Directory.CreateDirectory(ConfigRoot);
            if (!AppConfigStorage.TryLoad(out _, out string appConfigError))
            {
                throw new InvalidDataException($"程序配置初始化失败:{appConfigError}");
            }
            // AI 是辅助能力，其配置在真正启动 AI 时自行校验并降级，不能阻止平台编辑器启动。
            // Work 内容由 ProcessWorkDirectoryTransaction 加载并形成 ProcConfigFaulted，
            // 这样即使流程文件损坏或编号断档，用户仍能进入编辑器修复。
            string workRoot = Path.Combine(ConfigRoot, "Work");
            Directory.CreateDirectory(workRoot);
        }
    }
}
