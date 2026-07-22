using Automation.MotionControl;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    /// <summary>
    /// IO 调试页的后台读取模型。负责把可变配置和设备读取结果转换为不可变刷新快照，
    /// WinForms 只消费快照并更新控件。
    /// </summary>
    internal sealed class IoDebugMonitorService
    {
        private readonly PlatformRuntime runtime;
        private readonly object catalogLock = new object();
        private Dictionary<string, IO> inputs = new Dictionary<string, IO>(StringComparer.Ordinal);
        private Dictionary<string, IO> outputs = new Dictionary<string, IO>(StringComparer.Ordinal);
        private HashSet<string> duplicateInputs = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> duplicateOutputs = new HashSet<string>(StringComparer.Ordinal);
        private int catalogHash;

        public IoDebugMonitorService(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public IoDebugRefreshSnapshot BuildSnapshot(
            int tabIndex,
            IODebugMap debugMap,
            IReadOnlyList<IOConnect> connections)
        {
            if (debugMap == null) throw new ArgumentNullException(nameof(debugMap));
            RefreshCatalog();

            bool deviceReady = runtime.Motion?.IsCardInitialized == true && runtime.Io != null;
            switch (tabIndex)
            {
                case 0:
                    return BuildIoSnapshot(0, debugMap.inputs?.ToArray() ?? Array.Empty<IO>(), "通用输入",
                        deviceReady, deviceReady ? new IoReader(runtime.Io.GetInIO) : null);
                case 1:
                    return BuildIoSnapshot(1, debugMap.outputs?.ToArray() ?? Array.Empty<IO>(), "通用输出",
                        deviceReady, deviceReady ? new IoReader(runtime.Io.GetOutIO) : null);
                case 2:
                    return BuildConnectionSnapshot(connections?.ToArray() ?? Array.Empty<IOConnect>(), deviceReady);
                default:
                    return null;
            }
        }

        public bool TryResolveIo(string name, string ioType, out IO io, bool refreshCatalog = true)
        {
            io = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }
            if (refreshCatalog)
            {
                RefreshCatalog();
            }
            lock (catalogLock)
            {
                if (string.Equals(ioType, "通用输入", StringComparison.Ordinal))
                {
                    return !duplicateInputs.Contains(name) && inputs.TryGetValue(name, out io);
                }
                if (string.Equals(ioType, "通用输出", StringComparison.Ordinal))
                {
                    return !duplicateOutputs.Contains(name) && outputs.TryGetValue(name, out io);
                }
                return false;
            }
        }

        public bool TryToggleOutput(string primaryName, string inverseOutputName,
            out IoDebugOutputFailure failure, out string error)
        {
            failure = IoDebugOutputFailure.None;
            error = null;
            if (runtime.Safety.IsLocked)
            {
                failure = IoDebugOutputFailure.SafetyLocked;
                error = runtime.Safety.LockReason;
                return false;
            }
            if (runtime.Io == null || runtime.Motion?.IsCardInitialized != true)
            {
                failure = IoDebugOutputFailure.DeviceUnavailable;
                error = "运动控制卡未初始化，禁止操作输出。";
                return false;
            }
            if (!TryResolveIo(primaryName, "通用输出", out IO primary))
            {
                failure = IoDebugOutputFailure.OutputNotFound;
                error = $"未找到输出：{primaryName}";
                return false;
            }

            bool currentState = false;
            if (!runtime.Io.GetOutIO(primary, ref currentState))
            {
                failure = IoDebugOutputFailure.DeviceOperationFailed;
                error = $"读取输出状态失败：{primary.Name}";
                return false;
            }

            bool targetState = !currentState;
            var commands = new List<IoOutputCommand> { new IoOutputCommand(primary, targetState) };
            if (!string.IsNullOrWhiteSpace(inverseOutputName)
                && !string.Equals(inverseOutputName, primaryName, StringComparison.Ordinal))
            {
                if (!TryResolveIo(inverseOutputName, "通用输出", out IO inverse, false))
                {
                    failure = IoDebugOutputFailure.OutputNotFound;
                    error = $"未找到联动输出：{inverseOutputName}";
                    return false;
                }
                commands.Add(new IoOutputCommand(inverse, !targetState));
            }
            if (!runtime.Io.SetOutputs(commands))
            {
                failure = IoDebugOutputFailure.DeviceOperationFailed;
                error = commands.Count == 1
                    ? $"设置输出失败：{primary.Name}"
                    : $"设置联动输出失败：{primary.Name}、{inverseOutputName}";
                return false;
            }
            return true;
        }

        private IoDebugRefreshSnapshot BuildIoSnapshot(
            int tabIndex,
            IO[] configuredItems,
            string ioType,
            bool deviceReady,
            IoReader reader)
        {
            bool[] states = new bool[configuredItems.Length];
            bool[] valid = new bool[configuredItems.Length];
            if (deviceReady && reader != null)
            {
                for (int index = 0; index < configuredItems.Length; index++)
                {
                    IO configured = configuredItems[index];
                    if (configured == null || configured.IsRemark
                        || !TryResolveIo(configured.Name, ioType, out IO actual, false))
                    {
                        continue;
                    }
                    bool state = false;
                    if (reader(actual, ref state))
                    {
                        states[index] = state;
                        valid[index] = true;
                    }
                }
            }
            return tabIndex == 0
                ? IoDebugRefreshSnapshot.ForInputs(states, valid)
                : IoDebugRefreshSnapshot.ForOutputs(states, valid);
        }

        private IoDebugRefreshSnapshot BuildConnectionSnapshot(IOConnect[] connections, bool deviceReady)
        {
            bool[] outputStates = new bool[connections.Length];
            bool[] outputValid = new bool[connections.Length];
            bool[] input1States = new bool[connections.Length];
            bool[] input1Valid = new bool[connections.Length];
            bool[] input2States = new bool[connections.Length];
            bool[] input2Valid = new bool[connections.Length];
            if (deviceReady)
            {
                for (int index = 0; index < connections.Length; index++)
                {
                    IOConnect connection = connections[index];
                    if (connection?.Output == null || connection.Output.IsRemark)
                    {
                        continue;
                    }
                    ReadIo(connection.Output, "通用输出", runtime.Io.GetOutIO,
                        outputStates, outputValid, index);
                    ReadIo(connection.Intput1, "通用输入", runtime.Io.GetInIO,
                        input1States, input1Valid, index);
                    ReadIo(connection.Intput2, "通用输入", runtime.Io.GetInIO,
                        input2States, input2Valid, index);
                }
            }
            return IoDebugRefreshSnapshot.ForConnections(
                outputStates, outputValid, input1States, input1Valid, input2States, input2Valid);
        }

        private void ReadIo(IO configured, string ioType, IoReader reader,
            bool[] states, bool[] valid, int index)
        {
            if (configured == null || string.IsNullOrWhiteSpace(configured.Name)
                || !TryResolveIo(configured.Name, ioType, out IO actual, false))
            {
                return;
            }
            bool state = false;
            if (reader(actual, ref state))
            {
                states[index] = state;
                valid[index] = true;
            }
        }

        public void RefreshCatalog()
        {
            List<List<IO>> map = runtime.Stores.IoConfiguration.Map;
            IO[] snapshot;
            try
            {
                snapshot = map == null
                    ? Array.Empty<IO>()
                    : map.Where(cardItems => cardItems != null)
                        .SelectMany(cardItems => cardItems.ToArray())
                        .ToArray();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            int hash = 17;
            StringComparer comparer = StringComparer.Ordinal;
            foreach (IO item in snapshot)
            {
                if (item == null) continue;
                hash = hash * 31 + comparer.GetHashCode(item.Name ?? string.Empty);
                hash = hash * 31 + comparer.GetHashCode(item.IOType ?? string.Empty);
                hash = hash * 31 + item.CardNum.GetHashCode();
                hash = hash * 31 + item.Module.GetHashCode();
                hash = hash * 31 + comparer.GetHashCode(item.IOIndex ?? string.Empty);
            }

            lock (catalogLock)
            {
                if (hash == catalogHash)
                {
                    return;
                }
                BuildCatalog(snapshot, "通用输入", out Dictionary<string, IO> nextInputs,
                    out HashSet<string> nextDuplicateInputs);
                BuildCatalog(snapshot, "通用输出", out Dictionary<string, IO> nextOutputs,
                    out HashSet<string> nextDuplicateOutputs);
                inputs = nextInputs;
                outputs = nextOutputs;
                duplicateInputs = nextDuplicateInputs;
                duplicateOutputs = nextDuplicateOutputs;
                catalogHash = hash;
            }
        }

        private static void BuildCatalog(IEnumerable<IO> source, string ioType,
            out Dictionary<string, IO> catalog, out HashSet<string> duplicates)
        {
            catalog = new Dictionary<string, IO>(StringComparer.Ordinal);
            duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (IO item in source)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Name)
                    || !string.Equals(item.IOType, ioType, StringComparison.Ordinal)
                    || duplicates.Contains(item.Name))
                {
                    continue;
                }
                if (catalog.ContainsKey(item.Name))
                {
                    catalog.Remove(item.Name);
                    duplicates.Add(item.Name);
                    continue;
                }
                catalog.Add(item.Name, item);
            }
        }

        private delegate bool IoReader(IO io, ref bool state);
    }

    internal enum IoDebugOutputFailure
    {
        None = 0,
        SafetyLocked = 1,
        DeviceUnavailable = 2,
        OutputNotFound = 3,
        DeviceOperationFailed = 4
    }

    internal sealed class IoDebugRefreshSnapshot
    {
        private IoDebugRefreshSnapshot(int tabIndex)
        {
            TabIndex = tabIndex;
        }

        public int TabIndex { get; }
        public int InputCount => InputStates?.Length ?? 0;
        public int OutputCount => OutputStates?.Length ?? 0;
        public int ConnectCount => ConnectOutStates?.Length ?? 0;
        public bool[] InputStates { get; private set; }
        public bool[] InputValid { get; private set; }
        public bool[] OutputStates { get; private set; }
        public bool[] OutputValid { get; private set; }
        public bool[] ConnectOutStates { get; private set; }
        public bool[] ConnectOutValid { get; private set; }
        public bool[] ConnectIn1States { get; private set; }
        public bool[] ConnectIn1Valid { get; private set; }
        public bool[] ConnectIn2States { get; private set; }
        public bool[] ConnectIn2Valid { get; private set; }

        public static IoDebugRefreshSnapshot ForInputs(bool[] states, bool[] valid)
        {
            return new IoDebugRefreshSnapshot(0) { InputStates = states, InputValid = valid };
        }

        public static IoDebugRefreshSnapshot ForOutputs(bool[] states, bool[] valid)
        {
            return new IoDebugRefreshSnapshot(1) { OutputStates = states, OutputValid = valid };
        }

        public static IoDebugRefreshSnapshot ForConnections(bool[] outputStates, bool[] outputValid,
            bool[] input1States, bool[] input1Valid, bool[] input2States, bool[] input2Valid)
        {
            return new IoDebugRefreshSnapshot(2)
            {
                ConnectOutStates = outputStates,
                ConnectOutValid = outputValid,
                ConnectIn1States = input1States,
                ConnectIn1Valid = input1Valid,
                ConnectIn2States = input2States,
                ConnectIn2Valid = input2Valid
            };
        }
    }
}
