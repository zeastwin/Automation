using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Automation
{
    public sealed class PlcRuntimeService : IDisposable
    {
        private readonly object syncRoot = new object();
        private readonly PlcConfigStore configStore;
        private readonly ValueConfigStore valueStore;
        private readonly ILogger logger;
        private readonly Func<PlcDeviceConfig, IPlcAdapter> adapterFactory;
        private readonly Dictionary<string, DeviceSession> sessions =
            new Dictionary<string, DeviceSession>(StringComparer.OrdinalIgnoreCase);
        private bool disposed;
        private bool gateFaulted;
        private string gateFaultReason = string.Empty;

        public PlcRuntimeService(PlcConfigStore configStore, ValueConfigStore valueStore)
            : this(configStore, valueStore, config => new HslModbusAdapter(config))
        {
        }

        internal PlcRuntimeService(PlcConfigStore configStore, ValueConfigStore valueStore,
            Func<PlcDeviceConfig, IPlcAdapter> adapterFactory)
        {
            this.configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            this.valueStore = valueStore ?? throw new ArgumentNullException(nameof(valueStore));
            this.adapterFactory = adapterFactory ?? throw new ArgumentNullException(nameof(adapterFactory));
            logger = new LocalFileLogger(@"D:\AutomationLogs\PLC");
        }

        public event EventHandler<PlcRuntimeEventArgs> RuntimeEvent;

        public bool GateFaulted
        {
            get { lock (syncRoot) return gateFaulted; }
        }

        public string GateFaultReason
        {
            get { lock (syncRoot) return gateFaultReason; }
        }

        public bool Initialize(out string error)
        {
            error = null;
            ThrowIfDisposed();
            if (configStore.Faulted)
            {
                SetGateFault(configStore.FaultReason);
                error = configStore.FaultReason;
                return false;
            }
            if (!HslAuthorizationGate.EnsureAuthorized(out error))
            {
                SetGateFault(error);
                return false;
            }
            return ReloadConfiguration(true, out error);
        }

        public bool ReloadConfiguration(bool connectDevices, out string error)
        {
            error = null;
            ThrowIfDisposed();
            if (configStore.Faulted)
            {
                SetGateFault(configStore.FaultReason);
                error = configStore.FaultReason;
                return false;
            }
            if (!HslAuthorizationGate.EnsureAuthorized(out error))
            {
                SetGateFault(error);
                return false;
            }

            return ReloadConfiguration(configStore.GetSnapshot(), connectDevices, out error);
        }

        internal bool ReloadConfiguration(PlcConfiguration snapshot, bool connectDevices, out string error)
        {
            error = null;
            ThrowIfDisposed();
            if (!PlcConfigStore.Validate(snapshot, valueStore, out error)) return false;
            var replacements = snapshot.Devices.ToDictionary(
                device => device.Name,
                device => new DeviceSession(this, device, valueStore),
                StringComparer.OrdinalIgnoreCase);
            List<DeviceSession> old;
            lock (syncRoot)
            {
                old = sessions.Values.ToList();
                sessions.Clear();
                foreach (KeyValuePair<string, DeviceSession> item in replacements) sessions.Add(item.Key, item.Value);
                gateFaulted = false;
                gateFaultReason = string.Empty;
            }
            foreach (DeviceSession session in old) session.Dispose();

            if (connectDevices)
            {
                foreach (DeviceSession session in replacements.Values)
                {
                    Task.Run(() =>
                    {
                        if (!session.Reinitialize(out string connectError))
                        {
                            Raise(session.DeviceName, connectError, true);
                        }
                    });
                }
            }
            return true;
        }

        public IReadOnlyList<PlcDeviceRuntimeSnapshot> GetSnapshots()
        {
            lock (syncRoot)
            {
                return sessions.Values.Select(session => session.GetSnapshot()).ToList();
            }
        }

        public bool TryReinitialize(string deviceName, out string error)
        {
            if (!TryGetSession(deviceName, out DeviceSession session, out error)) return false;
            return session.Reinitialize(out error);
        }

        public bool TryStartMapping(string deviceName, out string error)
        {
            if (!TryGetSession(deviceName, out DeviceSession session, out error)) return false;
            return session.StartMapping(out error);
        }

        public bool TryStopMapping(string deviceName, out string error)
        {
            if (!TryGetSession(deviceName, out DeviceSession session, out error)) return false;
            return session.StopMapping(out error);
        }

        public bool TryResolveConflict(string deviceName, string mapId, PlcConflictResolution resolution, out string error)
        {
            if (!TryGetSession(deviceName, out DeviceSession session, out error)) return false;
            return session.ResolveConflict(mapId, resolution, out error);
        }

        public bool TryRead(string deviceName, PlcMapConfig request, out object[] values, out string error)
        {
            values = null;
            if (!ValidateDirectRequest(request, false, out error)) return false;
            if (!TryGetSession(deviceName, out DeviceSession session, out error)) return false;
            return session.DirectRead(request, out values, out error);
        }

        public bool TryWrite(string deviceName, PlcMapConfig request, IReadOnlyList<object> values, out string error)
        {
            if (!ValidateDirectRequest(request, true, out error)) return false;
            if (!TryGetSession(deviceName, out DeviceSession session, out error)) return false;
            return session.DirectWrite(request, values, out error);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            List<DeviceSession> snapshot;
            lock (syncRoot)
            {
                snapshot = sessions.Values.ToList();
                sessions.Clear();
            }
            foreach (DeviceSession session in snapshot) session.Dispose();
        }

        internal void Raise(string deviceName, string message, bool isAlarm)
        {
            logger.Log($"PLC[{deviceName}] {message}", isAlarm ? LogLevel.Error : LogLevel.Normal);
            RuntimeEvent?.Invoke(this, new PlcRuntimeEventArgs(deviceName, message, isAlarm));
        }

        private bool TryGetSession(string deviceName, out DeviceSession session, out string error)
        {
            session = null;
            error = null;
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(deviceName)) { error = "PLC设备名称为空。"; return false; }
            lock (syncRoot)
            {
                if (gateFaulted) { error = gateFaultReason; return false; }
                if (!sessions.TryGetValue(deviceName, out session))
                { error = $"PLC设备不存在:{deviceName}"; return false; }
            }
            return true;
        }

        private void SetGateFault(string reason)
        {
            List<DeviceSession> old;
            lock (syncRoot)
            {
                gateFaulted = true;
                gateFaultReason = string.IsNullOrWhiteSpace(reason) ? "PLC子系统不可用。" : reason;
                old = sessions.Values.ToList();
                sessions.Clear();
            }
            foreach (DeviceSession session in old) session.Dispose();
            Raise("子系统", gateFaultReason, true);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(PlcRuntimeService));
        }

        private static bool ValidateDirectRequest(PlcMapConfig request, bool write, out string error)
        {
            error = null;
            if (request == null) { error = "PLC访问参数为空。"; return false; }
            if (!Enum.IsDefined(typeof(PlcArea), request.Area)
                || !Enum.IsDefined(typeof(PlcDataType), request.DataType))
            { error = "PLC地址区或数据类型无效。"; return false; }
            if (request.StartAddress < 0 || request.StartAddress > 65535
                || request.ElementCount < 1 || request.ElementCount > 1000)
            { error = "PLC地址或元素数量超出范围。"; return false; }
            if ((request.Area == PlcArea.Coil || request.Area == PlcArea.DiscreteInput)
                != (request.DataType == PlcDataType.Boolean))
            { error = "Boolean只允许线圈区，其他类型只允许寄存器区。"; return false; }
            if (write && (request.Area == PlcArea.DiscreteInput || request.Area == PlcArea.InputRegister))
            { error = "只读地址区禁止写入。"; return false; }
            if (request.DataType == PlcDataType.String)
            {
                if (request.ElementCount != 1 || request.StringByteLength < 1 || request.StringByteLength > 2000)
                { error = "String必须为单元素且字符串字节数为1..2000。"; return false; }
            }
            else if (request.StringByteLength != 0)
            { error = "非String类型的字符串字节数必须为0。"; return false; }
            if ((long)request.StartAddress + PlcConfigStore.GetAddressSpan(request) - 1 > 65535)
            { error = "PLC访问范围超过65535。"; return false; }
            return true;
        }

        private sealed class DeviceSession : IDisposable
        {
            private readonly object stateLock = new object();
            private readonly object ioLock = new object();
            private readonly PlcRuntimeService owner;
            private readonly ValueConfigStore valueStore;
            private readonly PlcDeviceConfig config;
            private readonly Dictionary<string, MapState> mapStates;
            private IPlcAdapter adapter;
            private CancellationTokenSource mappingCts;
            private Task mappingTask;
            private PlcRuntimeState state = PlcRuntimeState.Uninitialized;
            private string lastError = string.Empty;
            private DateTime? lastCommunicationUtc;
            private long lastScanElapsedMs;
            private bool disposed;

            public DeviceSession(PlcRuntimeService owner, PlcDeviceConfig config, ValueConfigStore valueStore)
            {
                this.owner = owner;
                this.config = PlcModelClone.Clone(config);
                this.valueStore = valueStore;
                mapStates = this.config.Mappings.ToDictionary(
                    map => map.Id,
                    map => new MapState(map),
                    StringComparer.Ordinal);
                SetStatusVariable(0d);
            }

            public string DeviceName => config.Name;

            public bool Reinitialize(out string error)
            {
                error = null;
                if (disposed) { error = "PLC设备运行时已释放。"; return false; }
                StopWorker(false);
                lock (ioLock)
                {
                    SetState(PlcRuntimeState.Connecting, string.Empty);
                    adapter?.Dispose();
                    adapter = owner.adapterFactory(PlcModelClone.Clone(config));
                    if (!adapter.Connect(out error))
                    {
                        Fault(error, false);
                        return false;
                    }
                    foreach (MapState mapState in mapStates.Values) mapState.Reset();
                    SetState(PlcRuntimeState.Ready, string.Empty);
                    SetStatusVariable(1d);
                    owner.Raise(config.Name, "连接初始化成功。", false);
                    return true;
                }
            }

            public bool StartMapping(out string error)
            {
                error = null;
                lock (stateLock)
                {
                    if (state == PlcRuntimeState.Mapping) return true;
                    if (state != PlcRuntimeState.Ready && state != PlcRuntimeState.Stopped)
                    { error = $"设备状态[{state}]禁止启动映射，请先重新初始化。"; return false; }
                }

                lock (ioLock)
                {
                    foreach (MapState mapState in mapStates.Values.Where(item => item.Config.Enabled))
                    {
                        if (!InitializeMap(mapState, out error))
                        {
                            Fault(error, false);
                            return false;
                        }
                    }
                }

                lock (stateLock)
                {
                    mappingCts = new CancellationTokenSource();
                    CancellationToken token = mappingCts.Token;
                    SetStateUnsafe(PlcRuntimeState.Mapping, string.Empty);
                    SetStatusVariable(2d);
                    mappingTask = Task.Run(() => MappingLoop(token), token);
                }
                owner.Raise(config.Name, "变量映射已启动。", false);
                return true;
            }

            public bool StopMapping(out string error)
            {
                error = null;
                if (disposed) return true;
                StopWorker(true);
                return true;
            }

            public bool ResolveConflict(string mapId, PlcConflictResolution resolution, out string error)
            {
                error = null;
                if (!mapStates.TryGetValue(mapId ?? string.Empty, out MapState mapState))
                { error = $"PLC映射不存在:{mapId}"; return false; }
                lock (stateLock)
                {
                    if (state != PlcRuntimeState.Mapping) { error = "设备未处于映射状态。"; return false; }
                    if (mapState.State != PlcMapRuntimeState.Conflict) { error = "映射项当前不存在冲突。"; return false; }
                }
                lock (ioLock)
                {
                    if (!adapter.Read(mapState.Config, out object[] plcValues, out error))
                    { Fault(error, true); return false; }
                    if (!TryGetLocalValues(mapState.Config, out object[] localValues, out error)) return false;
                    if (resolution == PlcConflictResolution.UsePlcValue)
                    {
                        if (!SetLocalValues(mapState.Config, plcValues, out error)) return false;
                        localValues = CloneValues(plcValues);
                    }
                    else
                    {
                        if (!adapter.Write(mapState.Config, localValues, out error))
                        { Fault(error, true); return false; }
                        plcValues = CloneValues(localValues);
                    }
                    mapState.SetNormal(plcValues, localValues);
                    MarkCommunication();
                    owner.Raise(config.Name,
                        $"映射[{mapState.Config.Name}]冲突已由操作员选择[{resolution}]解除。", false);
                    return true;
                }
            }

            public bool DirectRead(PlcMapConfig request, out object[] values, out string error)
            {
                values = null;
                error = null;
                if (!EnsureConnected(out error)) return false;
                lock (ioLock)
                {
                    if (!adapter.Read(request, out values, out error))
                    { Fault(error, true); return false; }
                    MarkCommunication();
                    return true;
                }
            }

            public bool DirectWrite(PlcMapConfig request, IReadOnlyList<object> values, out string error)
            {
                error = null;
                if (!EnsureConnected(out error)) return false;
                lock (stateLock)
                {
                    if (state == PlcRuntimeState.Mapping && config.Mappings.Any(map => map.Enabled && Overlaps(map, request)))
                    { error = "直接写入地址与活动映射重叠，请先停止该设备映射。"; return false; }
                }
                lock (ioLock)
                {
                    if (!adapter.Write(request, values, out error))
                    { Fault(error, true); return false; }
                    MarkCommunication();
                    return true;
                }
            }

            public PlcDeviceRuntimeSnapshot GetSnapshot()
            {
                lock (stateLock)
                {
                    return new PlcDeviceRuntimeSnapshot
                    {
                        DeviceName = config.Name,
                        State = state,
                        LastError = lastError,
                        LastCommunicationUtc = lastCommunicationUtc,
                        LastScanElapsedMs = lastScanElapsedMs,
                        Mappings = mapStates.Values.Select(item => item.GetSnapshot()).ToList()
                    };
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                StopWorker(false);
                lock (ioLock)
                {
                    adapter?.Dispose();
                    adapter = null;
                }
                SetStatusVariable(0d);
            }

            private bool InitializeMap(MapState mapState, out string error)
            {
                error = null;
                PlcMapConfig map = mapState.Config;
                if (map.Direction == PlcMapDirection.ReadFromPlc)
                {
                    if (!adapter.Read(map, out object[] plcValues, out error)) return false;
                    if (!SetLocalValues(map, plcValues, out error))
                    { mapState.SetFault(error); owner.Raise(config.Name, error, true); return true; }
                    mapState.SetNormal(plcValues, plcValues);
                }
                else if (map.Direction == PlcMapDirection.WriteToPlc)
                {
                    if (!TryGetLocalValues(map, out object[] localValues, out error))
                    { mapState.SetFault(error); owner.Raise(config.Name, error, true); return true; }
                    if (!adapter.Write(map, localValues, out error)) return false;
                    mapState.SetNormal(localValues, localValues);
                }
                else
                {
                    if (!adapter.Read(map, out object[] plcValues, out error)) return false;
                    if (!TryGetLocalValues(map, out object[] localValues, out error))
                    { mapState.SetFault(error); owner.Raise(config.Name, error, true); return true; }
                    if (!ValuesEqual(map, plcValues, localValues))
                    {
                        mapState.SetConflict(plcValues, localValues, "启动时PLC值与本地值不一致。" );
                        owner.Raise(config.Name, $"映射[{map.Name}]启动冲突，已隔离且未覆盖任何一侧。", true);
                    }
                    else mapState.SetNormal(plcValues, localValues);
                }
                MarkCommunication();
                return true;
            }

            private void MappingLoop(CancellationToken token)
            {
                uint cycle = 0;
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        Stopwatch watch = Stopwatch.StartNew();
                        List<MapState> dueMaps = mapStates.Values.Where(mapState =>
                            mapState.Config.Enabled
                            && mapState.State != PlcMapRuntimeState.Conflict
                            && mapState.State != PlcMapRuntimeState.Faulted
                            && IsDue(mapState.Config.Priority, cycle)).ToList();
                        lock (ioLock)
                        {
                            List<PlcMapConfig> readMaps = dueMaps
                                .Where(item => item.Config.Direction != PlcMapDirection.WriteToPlc)
                                .Select(item => item.Config).ToList();
                            if (!adapter.ReadBatch(readMaps,
                                out IReadOnlyDictionary<string, object[]> batchValues, out string batchError))
                            {
                                Fault(batchError, true);
                                return;
                            }
                            foreach (MapState mapState in dueMaps)
                            {
                                if (token.IsCancellationRequested) break;
                                batchValues.TryGetValue(mapState.Config.Id, out object[] plcValues);
                                if (!ProcessMap(mapState, plcValues, out string error))
                                {
                                    Fault(error, true);
                                    return;
                                }
                            }
                        }
                        watch.Stop();
                        lock (stateLock) lastScanElapsedMs = watch.ElapsedMilliseconds;
                        cycle = cycle == uint.MaxValue ? 0u : cycle + 1u;
                        int wait = Math.Max(0, config.ScanIntervalMs - (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds));
                        if (token.WaitHandle.WaitOne(wait)) break;
                    }
                }
                catch (Exception ex)
                {
                    Fault("映射线程异常:" + ex.Message, true);
                }
            }

            private bool ProcessMap(MapState mapState, object[] prefetchedPlcValues, out string error)
            {
                error = null;
                PlcMapConfig map = mapState.Config;
                if (map.Direction == PlcMapDirection.ReadFromPlc)
                {
                    object[] plcValues = prefetchedPlcValues;
                    if (plcValues == null) { error = $"映射[{map.Name}]缺少批量读取结果。"; return false; }
                    if (!SetLocalValues(map, plcValues, out error))
                    { IsolateFault(mapState, error); return true; }
                    mapState.SetNormal(plcValues, plcValues);
                }
                else if (map.Direction == PlcMapDirection.WriteToPlc)
                {
                    if (!TryGetLocalValues(map, out object[] localValues, out error))
                    { IsolateFault(mapState, error); return true; }
                    if (!ValuesEqual(map, localValues, mapState.LastLocalValues))
                    {
                        if (!adapter.Write(map, localValues, out error)) return false;
                        mapState.SetNormal(localValues, localValues);
                    }
                }
                else
                {
                    object[] plcValues = prefetchedPlcValues;
                    if (plcValues == null) { error = $"映射[{map.Name}]缺少批量读取结果。"; return false; }
                    if (!TryGetLocalValues(map, out object[] localValues, out error))
                    { IsolateFault(mapState, error); return true; }
                    bool plcChanged = !ValuesEqual(map, plcValues, mapState.LastPlcValues);
                    bool localChanged = !ValuesEqual(map, localValues, mapState.LastLocalValues);
                    if (plcChanged && localChanged)
                    {
                        if (ValuesEqual(map, plcValues, localValues)) mapState.SetNormal(plcValues, localValues);
                        else
                        {
                            mapState.SetConflict(plcValues, localValues, "PLC与本地同时变化且结果不一致。" );
                            owner.Raise(config.Name, $"映射[{map.Name}]发生双向冲突，已隔离。", true);
                        }
                    }
                    else if (plcChanged)
                    {
                        if (!SetLocalValues(map, plcValues, out error)) { IsolateFault(mapState, error); return true; }
                        mapState.SetNormal(plcValues, plcValues);
                    }
                    else if (localChanged)
                    {
                        if (!adapter.Write(map, localValues, out error)) return false;
                        mapState.SetNormal(localValues, localValues);
                    }
                }
                MarkCommunication();
                return true;
            }

            private void IsolateFault(MapState mapState, string error)
            {
                mapState.SetFault(error);
                owner.Raise(config.Name, $"映射[{mapState.Config.Name}]已隔离:{error}", true);
            }

            private bool TryGetLocalValues(PlcMapConfig map, out object[] values, out string error)
            {
                error = null;
                values = new object[map.VariableNames.Count];
                for (int i = 0; i < map.VariableNames.Count; i++)
                {
                    string name = map.VariableNames[i];
                    if (!valueStore.TryGetValueByName(name, out DicValue value) || value == null)
                    { error = $"变量不存在:{name}"; values = null; return false; }
                    if (map.DataType == PlcDataType.String) values[i] = value.Value ?? string.Empty;
                    else if (!double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                        || double.IsNaN(number) || double.IsInfinity(number))
                    { error = $"变量[{name}]不是有效有限数。"; values = null; return false; }
                    else values[i] = number;
                }
                return true;
            }

            private bool SetLocalValues(PlcMapConfig map, IReadOnlyList<object> values, out string error)
            {
                error = null;
                if (values == null || values.Count != map.VariableNames.Count)
                { error = $"映射[{map.Name}]PLC返回数量不匹配。"; return false; }
                for (int i = 0; i < values.Count; i++)
                {
                    object value = map.DataType == PlcDataType.String
                        ? (object)(values[i]?.ToString() ?? string.Empty)
                        : map.DataType == PlcDataType.Boolean
                            ? ((bool)values[i] ? 1d : 0d)
                            : Convert.ToDouble(values[i], CultureInfo.InvariantCulture);
                    if (!valueStore.setValueByName(map.VariableNames[i], value, "PLC映射"))
                    { error = $"变量写入失败:{map.VariableNames[i]}"; return false; }
                }
                return true;
            }

            private bool EnsureConnected(out string error)
            {
                lock (stateLock)
                {
                    if (state == PlcRuntimeState.Ready || state == PlcRuntimeState.Stopped || state == PlcRuntimeState.Mapping)
                    { error = null; return true; }
                    error = $"设备状态[{state}]不可通讯，请先重新初始化。";
                    return false;
                }
            }

            private void StopWorker(bool changeState)
            {
                CancellationTokenSource cts;
                Task task;
                lock (stateLock)
                {
                    cts = mappingCts;
                    task = mappingTask;
                    mappingCts = null;
                    mappingTask = null;
                    cts?.Cancel();
                }
                if (task != null && Task.CurrentId != task.Id)
                {
                    try { task.Wait(Math.Max(1000, config.ReceiveTimeoutMs + 500)); } catch { }
                }
                cts?.Dispose();
                if (changeState)
                {
                    lock (stateLock)
                    {
                        if (state != PlcRuntimeState.Faulted) SetStateUnsafe(PlcRuntimeState.Stopped, string.Empty);
                    }
                    if (state != PlcRuntimeState.Faulted) SetStatusVariable(1d);
                    owner.Raise(config.Name, "变量映射已停止。", false);
                }
            }

            private void Fault(string error, bool fromMappingThread)
            {
                lock (stateLock)
                {
                    lastError = string.IsNullOrWhiteSpace(error) ? "PLC通讯故障。" : error;
                    SetStateUnsafe(PlcRuntimeState.Faulted, lastError);
                    mappingCts?.Cancel();
                }
                try { adapter?.Close(); } catch { }
                SetStatusVariable(-1d);
                owner.Raise(config.Name, lastError + " 设备映射已停止，必须重新初始化。", true);
                if (!fromMappingThread) StopWorker(false);
            }

            private void SetState(PlcRuntimeState next, string error)
            {
                lock (stateLock) SetStateUnsafe(next, error);
            }

            private void SetStateUnsafe(PlcRuntimeState next, string error)
            {
                state = next;
                if (!string.IsNullOrWhiteSpace(error)) lastError = error;
                else if (next != PlcRuntimeState.Faulted) lastError = string.Empty;
            }

            private void MarkCommunication()
            {
                lock (stateLock) lastCommunicationUtc = DateTime.UtcNow;
            }

            private void SetStatusVariable(double value)
            {
                if (string.IsNullOrWhiteSpace(config.StatusVariableName)) return;
                valueStore.setValueByName(config.StatusVariableName, value, "PLC状态");
            }

            private static bool IsDue(PlcMapPriority priority, uint cycle)
            {
                return priority == PlcMapPriority.High
                    || priority == PlcMapPriority.Medium && cycle % 10 == 0
                    || priority == PlcMapPriority.Low && cycle % 40 == 0;
            }

            private static bool ValuesEqual(PlcMapConfig map, IReadOnlyList<object> left, IReadOnlyList<object> right)
            {
                if (left == null || right == null || left.Count != right.Count) return false;
                for (int i = 0; i < left.Count; i++)
                {
                    if (map.DataType == PlcDataType.String)
                    {
                        if (!string.Equals(Convert.ToString(left[i]), Convert.ToString(right[i]), StringComparison.Ordinal)) return false;
                    }
                    else
                    {
                        double a = map.DataType == PlcDataType.Boolean && left[i] is bool lb ? (lb ? 1d : 0d)
                            : Convert.ToDouble(left[i], CultureInfo.InvariantCulture);
                        double b = map.DataType == PlcDataType.Boolean && right[i] is bool rb ? (rb ? 1d : 0d)
                            : Convert.ToDouble(right[i], CultureInfo.InvariantCulture);
                        double tolerance = map.DataType == PlcDataType.Float || map.DataType == PlcDataType.Double
                            ? map.ChangeTolerance : 0d;
                        if (Math.Abs(a - b) > tolerance) return false;
                    }
                }
                return true;
            }

            private static bool Overlaps(PlcMapConfig left, PlcMapConfig right)
            {
                if (left.Area != right.Area) return false;
                int leftEnd = left.StartAddress + PlcConfigStore.GetAddressSpan(left) - 1;
                int rightEnd = right.StartAddress + PlcConfigStore.GetAddressSpan(right) - 1;
                return left.StartAddress <= rightEnd && right.StartAddress <= leftEnd;
            }

            private static object[] CloneValues(IReadOnlyList<object> values)
            {
                return values?.Select(value => value).ToArray();
            }
        }

        private sealed class MapState
        {
            public MapState(PlcMapConfig config)
            {
                Config = PlcModelClone.Clone(config);
            }

            public PlcMapConfig Config { get; }
            public PlcMapRuntimeState State { get; private set; }
            public string Message { get; private set; } = string.Empty;
            public object[] LastPlcValues { get; private set; }
            public object[] LastLocalValues { get; private set; }

            public void Reset()
            {
                State = PlcMapRuntimeState.Idle;
                Message = string.Empty;
                LastPlcValues = null;
                LastLocalValues = null;
            }

            public void SetNormal(IReadOnlyList<object> plc, IReadOnlyList<object> local)
            {
                State = PlcMapRuntimeState.Normal;
                Message = string.Empty;
                LastPlcValues = plc?.ToArray();
                LastLocalValues = local?.ToArray();
            }

            public void SetConflict(IReadOnlyList<object> plc, IReadOnlyList<object> local, string message)
            {
                State = PlcMapRuntimeState.Conflict;
                Message = message ?? string.Empty;
                LastPlcValues = plc?.ToArray();
                LastLocalValues = local?.ToArray();
            }

            public void SetFault(string message)
            {
                State = PlcMapRuntimeState.Faulted;
                Message = message ?? string.Empty;
            }

            public PlcMapRuntimeSnapshot GetSnapshot()
            {
                return new PlcMapRuntimeSnapshot
                {
                    MapId = Config.Id,
                    MapName = Config.Name,
                    State = State,
                    Message = Message,
                    PlcValues = LastPlcValues?.ToArray(),
                    LocalValues = LastLocalValues?.ToArray()
                };
            }
        }
    }
}
