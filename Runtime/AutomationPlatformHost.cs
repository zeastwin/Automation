using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

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
        public int Index { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public string Note { get; set; }
    }

    public sealed class PlatformLogEntry
    {
        public string TimeText { get; set; }
        public string Message { get; set; }
        public FrmInfo.Level Level { get; set; }
    }

    public sealed class AutomationPlatformHost : IDisposable
    {
        private static readonly string[] RequiredConfigFiles =
        {
            "AppConfig.json",
            "value.json",
            "DataStruct.json",
            "AlarmInfo.json",
            "card.json",
            "IOMap.json",
            "IODebugMap.json",
            "DataStation.json",
            "SocketInfo.json",
            "SerialPortInfo.json",
            "PlcDevice.json",
            "PlcMap.json",
            "GooseConfig.json"
        };

        private readonly int uiThreadId = Thread.CurrentThread.ManagedThreadId;
        private readonly Dictionary<string, CustomFunc.FunctionDelegate> pendingCustomFunctions =
            new Dictionary<string, CustomFunc.FunctionDelegate>(StringComparer.Ordinal);
        private FrmMain platformEditor;
        private PlatformRuntimeState state = PlatformRuntimeState.Created;
        private bool disposed;

        public event EventHandler<PlatformRuntimeStateChangedEventArgs> RuntimeStateChanged;
        public event Action<EngineSnapshot> ProcessSnapshotChanged;
        public event EventHandler<ValueChangedEventArgs> ValueChanged;

        public string ConfigRoot
        {
            get
            {
                string configRoot = SF.ConfigPath;
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

        internal FrmMain PlatformEditor
        {
            get
            {
                EnsureReadyOrFaulted();
                return platformEditor;
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

        public bool Initialize(out string error)
        {
            EnsureUiThread();
            error = null;
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
                platformEditor = new FrmMain
                {
                    HideOnUserClose = true
                };
                foreach (KeyValuePair<string, CustomFunc.FunctionDelegate> item in pendingCustomFunctions)
                {
                    platformEditor.customFunc.RegisterFunction(item.Key, item.Value);
                }

                platformEditor.dataRun.SnapshotChanged += OnProcessSnapshotChanged;
                SF.valueStore.ValueChanged += OnValueChanged;
                platformEditor.InitializePlatform();
                MonitorSystemValue("复位状态");
                MonitorSystemValue("系统状态");

                if (SF.ProcConfigFaulted)
                {
                    throw new InvalidOperationException("流程配置校验失败，平台已停止启动。");
                }
                SetState(PlatformRuntimeState.Ready, SF.SecurityLocked
                    ? $"平台已初始化，但处于安全锁定状态:{SF.SecurityLockReason}"
                    : "平台已就绪");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                SetState(PlatformRuntimeState.Faulted, $"平台初始化失败:{ex.Message}");
                try
                {
                    platformEditor?.ShutdownPlatform();
                }
                catch
                {
                }
                return false;
            }
        }

        public void PreparePlatformOnlyMode()
        {
            EnsureUiThread();
            EnsureReadyOrFaulted();
            platformEditor.HideOnUserClose = false;
            platformEditor.EnsureAiInfrastructureStarted();
        }

        public void ShowPlatformEditor(IWin32Window owner)
        {
            EnsureUiThread();
            EnsureReadyOrFaulted();
            platformEditor.HideOnUserClose = true;
            platformEditor.EnsureAiInfrastructureStarted();
            if (!platformEditor.Visible)
            {
                if (owner == null)
                {
                    platformEditor.Show();
                }
                else
                {
                    platformEditor.Show(owner);
                }
            }
            if (platformEditor.WindowState == FormWindowState.Minimized)
            {
                platformEditor.WindowState = FormWindowState.Maximized;
            }
            platformEditor.BringToFront();
            platformEditor.Activate();
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
            IList<Proc> procs = platformEditor.dataRun?.Context?.Procs;
            if (procs == null)
            {
                return result;
            }
            for (int i = 0; i < procs.Count; i++)
            {
                Proc proc = procs[i];
                EngineSnapshot snapshot = platformEditor.dataRun.GetSnapshot(i);
                result.Add(new PlatformProcessInfo
                {
                    Index = i,
                    Id = proc?.head?.Id ?? Guid.Empty,
                    Name = string.IsNullOrWhiteSpace(proc?.head?.Name) ? $"流程{i}" : proc.head.Name,
                    State = snapshot?.State ?? ProcRunState.Stopped,
                    Disabled = proc?.head?.Disable == true,
                    IsAlarm = snapshot?.IsAlarm == true,
                    AlarmMessage = snapshot?.AlarmMessage ?? string.Empty
                });
            }
            return result;
        }

        public bool TryStartProcess(int procIndex, out string error)
        {
            if (!CanIssueRuntimeCommand(out error))
            {
                return false;
            }
            IList<Proc> procs = platformEditor.dataRun?.Context?.Procs;
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
            if (!SF.procStore.StartProc(procIndex))
            {
                error = $"流程启动失败:{procIndex}";
                return false;
            }
            return true;
        }

        public bool TryPauseProcess(int procIndex, out string error)
        {
            return TryExecuteProcessCommand(procIndex, index => SF.procStore != null && SF.procStore.Pause(index), "暂停", false, out error);
        }

        public bool TryResumeProcess(int procIndex, out string error)
        {
            return TryExecuteProcessCommand(procIndex, index => SF.procStore != null && SF.procStore.Resume(index), "继续", true, out error);
        }

        public bool TryStopProcess(int procIndex, out string error)
        {
            return TryExecuteProcessCommand(procIndex, index => SF.procStore != null && SF.procStore.Stop(index), "停止", false, out error);
        }

        public bool TryStopAllProcesses(out string error)
        {
            error = null;
            if (platformEditor == null || state == PlatformRuntimeState.Created || state == PlatformRuntimeState.Stopped)
            {
                error = $"当前状态禁止停止流程:{state}";
                return false;
            }
            IReadOnlyList<PlatformProcessInfo> processes = GetProcesses();
            bool success = true;
            List<string> failures = new List<string>();
            foreach (PlatformProcessInfo process in processes)
            {
                if (SF.procStore == null || !SF.procStore.Stop(process.Index))
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
            if (SF.valueStore == null || !SF.valueStore.TryGetValueByName(name, out DicValue value) || value == null)
            {
                error = $"变量不存在:{name}";
                return false;
            }
            snapshot = new PlatformValueSnapshot
            {
                Index = value.Index,
                Name = value.Name,
                Type = value.Type,
                Value = value.Value,
                Note = value.Note
            };
            return true;
        }

        public bool TrySetValue(string name, object value, out string error)
        {
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
            if (SF.valueStore == null || !SF.valueStore.setValueByName(name, value, "外围代码"))
            {
                error = $"变量写入失败:{name}";
                return false;
            }
            return true;
        }

        public bool TryMonitorValue(string name, bool enabled, out string error)
        {
            error = null;
            if (state != PlatformRuntimeState.Ready && state != PlatformRuntimeState.Initializing)
            {
                error = $"当前状态禁止设置变量监控:{state}";
                return false;
            }
            if (SF.valueStore == null || !SF.valueStore.TryGetValueByName(name, out DicValue value) || value == null)
            {
                error = $"变量不存在:{name}";
                return false;
            }
            SF.valueStore.SetMonitorFlag(value.Index, enabled);
            SF.valueStore.SetMonitorEnabled(true);
            return true;
        }

        public IReadOnlyList<PlatformLogEntry> GetRecentLogs(int maxCount)
        {
            EnsureReadyOrFaulted();
            if (maxCount <= 0)
            {
                return new List<PlatformLogEntry>();
            }
            IReadOnlyList<FrmInfo.InfoLogSnapshot> source = platformEditor.frmInfo.GetInfoLogTail(maxCount);
            return source.Select(item => new PlatformLogEntry
            {
                TimeText = item.TimeText,
                Message = item.Message,
                Level = item.Level
            }).ToList();
        }

        public void Shutdown()
        {
            EnsureUiThread();
            if (state == PlatformRuntimeState.Stopped || state == PlatformRuntimeState.ShuttingDown)
            {
                return;
            }
            SetState(PlatformRuntimeState.ShuttingDown, "正在安全关闭控制平台");
            try
            {
                if (platformEditor != null)
                {
                    if (platformEditor.dataRun != null)
                    {
                        platformEditor.dataRun.SnapshotChanged -= OnProcessSnapshotChanged;
                    }
                    if (SF.valueStore != null)
                    {
                        SF.valueStore.ValueChanged -= OnValueChanged;
                    }
                    platformEditor.ShutdownPlatform();
                    platformEditor.AllowFinalClose();
                    if (!platformEditor.IsDisposed)
                    {
                        platformEditor.Close();
                        platformEditor.Dispose();
                    }
                }
            }
            finally
            {
                SetState(PlatformRuntimeState.Stopped, "控制平台已关闭");
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Shutdown();
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
            if (SF.SecurityLocked)
            {
                error = string.IsNullOrWhiteSpace(SF.SecurityLockReason)
                    ? "系统处于安全锁定状态。"
                    : $"系统处于安全锁定状态:{SF.SecurityLockReason}";
                return false;
            }
            if (SF.ProcConfigFaulted)
            {
                error = "流程配置异常，禁止控制操作。";
                return false;
            }
            if (SF.VersionRestartRequired)
            {
                error = "设备配置已还原，必须重启程序后才能继续操作。";
                return false;
            }
            return true;
        }

        private void MonitorSystemValue(string name)
        {
            if (!TryMonitorValue(name, true, out string error))
            {
                throw new InvalidOperationException(error);
            }
        }

        private void OnProcessSnapshotChanged(EngineSnapshot snapshot)
        {
            ProcessSnapshotChanged?.Invoke(snapshot);
        }

        private void OnValueChanged(object sender, ValueChangedEventArgs e)
        {
            ValueChanged?.Invoke(this, e);
        }

        private void SetState(PlatformRuntimeState nextState, string message)
        {
            state = nextState;
            StateMessage = message ?? string.Empty;
            RuntimeStateChanged?.Invoke(this, new PlatformRuntimeStateChangedEventArgs(nextState, StateMessage));
        }

        private void EnsureReadyOrFaulted()
        {
            if (platformEditor == null || (state != PlatformRuntimeState.Ready && state != PlatformRuntimeState.Faulted))
            {
                throw new InvalidOperationException($"平台尚未初始化，当前状态:{state}");
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
            if (!Directory.Exists(ConfigRoot))
            {
                throw new DirectoryNotFoundException($"Config 目录不存在:{ConfigRoot}");
            }
            foreach (string fileName in RequiredConfigFiles)
            {
                string filePath = Path.Combine(ConfigRoot, fileName);
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"Config 缺少文件:{fileName}", filePath);
                }
            }

            string workRoot = Path.Combine(ConfigRoot, "Work");
            if (!Directory.Exists(workRoot))
            {
                throw new DirectoryNotFoundException($"流程目录不存在:{workRoot}");
            }
            List<int> indices = new List<int>();
            foreach (string filePath in Directory.GetFiles(workRoot, "*.json", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (!int.TryParse(fileName, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0)
                {
                    throw new InvalidDataException($"流程文件名无效:{Path.GetFileName(filePath)}");
                }
                indices.Add(index);
            }
            indices.Sort();
            for (int i = 0; i < indices.Count; i++)
            {
                if (indices[i] != i)
                {
                    throw new InvalidDataException($"流程文件索引不连续，期望 {i}.json，实际 {indices[i]}.json");
                }
            }
        }
    }
}
