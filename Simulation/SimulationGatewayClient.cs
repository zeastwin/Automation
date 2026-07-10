using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Automation.MotionControl;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Automation.FrmCard;

namespace Automation.Simulation
{
    public sealed class SimulationGatewayClient : IMotionRuntime, IIoRuntime, IDisposable
    {
        private const int ProtocolVersion = 1;
        private const int MaxFrameLength = 1024 * 1024;
        private const string PipeName = "AutomationSimulationPipe";
        private readonly object sendLock = new object();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<GatewayEnvelope>> pending = new ConcurrentDictionary<string, TaskCompletionSource<GatewayEnvelope>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, GatewayIoState> ioStates = new ConcurrentDictionary<string, GatewayIoState>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, GatewayAxisState> axisStates = new ConcurrentDictionary<string, GatewayAxisState>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> axisEquivalents = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private readonly string sessionId = Guid.NewGuid().ToString("N");
        private readonly ManualResetEventSlim snapshotReady = new ManualResetEventSlim(false);
        private NamedPipeClientStream stream;
        private CancellationTokenSource cts;
        private Task readerTask;
        private System.Threading.Timer watchdog;
        private long sendSequence;
        private long receiveSequence;
        private long lastReceiveTicks;
        private int faultRaised;
        private bool disposed;

        public event Action<string> Faulted;
        public bool IsCardInitialized { get; private set; }
        public IReadOnlyList<SimulationModbusMapping> ModbusMappings { get; private set; } = Array.Empty<SimulationModbusMapping>();
        public IReadOnlyList<SimulationTcpMapping> TcpMappings { get; private set; } = Array.Empty<SimulationTcpMapping>();
        public IReadOnlyList<SimulationSerialMapping> SerialMappings { get; private set; } = Array.Empty<SimulationSerialMapping>();

        public void Connect(int timeoutMs)
        {
            if (timeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            if (stream != null) throw new InvalidOperationException("仿真网关已连接");
            stream = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                stream.Connect(timeoutMs);
                cts = new CancellationTokenSource();
                lastReceiveTicks = Stopwatch.GetTimestamp();
                readerTask = Task.Run(() => ReaderLoopAsync(cts.Token));
                GatewayEnvelope response = SendRequest("hello", new JObject(), "hello.ack", timeoutMs);
                bool ready = RequireBool(response.Payload, "ready");
                if (!ready)
                {
                    string errors = response.Payload["errors"] is JArray array ? string.Join("；", array.Values<string>()) : "模拟器拒绝握手";
                    throw new InvalidOperationException(errors);
                }
                ParseMappings(response.Payload["mappings"] as JObject ?? throw new InvalidDataException("模拟器端点映射为空"));
                IsCardInitialized = true;
                if (!snapshotReady.Wait(1000)) throw new TimeoutException("等待模拟器初始状态快照超时");
                watchdog = new System.Threading.Timer(CheckHeartbeat, null, 250, 250);
            }
            catch
            {
                DisposeConnection();
                throw;
            }
        }

        public void ApplyEndpointMappings(EngineContext context)
        {
            if (!IsCardInitialized) throw new InvalidOperationException("仿真网关尚未就绪");
            if (context?.PlcStore != null)
            {
                List<PlcDevice> devices = context.PlcStore.Devices.Select(CloneDevice).ToList();
                foreach (SimulationModbusMapping mapping in ModbusMappings)
                {
                    PlcDevice device = devices.FirstOrDefault(item => string.Equals(item.Name, mapping.Name, StringComparison.Ordinal));
                    if (device == null) throw new InvalidOperationException($"PLC端点映射指向未知设备:{mapping.Name}");
                    if (!string.Equals(device.Protocol, "ModbusTcp", StringComparison.Ordinal)) throw new InvalidOperationException($"仿真端点仅支持ModbusTcp:{mapping.Name}");
                    device.Ip = mapping.Address;
                    device.Port = mapping.Port;
                    device.UnitId = mapping.UnitId;
                }
                context.PlcStore.ReplaceDevices(devices);
            }
            if (context?.SocketInfos != null)
            {
                foreach (SimulationTcpMapping mapping in TcpMappings)
                {
                    SocketInfo info = context.SocketInfos.FirstOrDefault(item => item != null && string.Equals(item.Name, mapping.Name, StringComparison.Ordinal));
                    if (info == null) throw new InvalidOperationException($"TCP端点映射指向未知通道:{mapping.Name}");
                    if (!string.Equals(info.Type, mapping.PlatformType, StringComparison.Ordinal)) throw new InvalidOperationException($"TCP通道类型不匹配:{mapping.Name}");
                    info.Address = mapping.Address;
                    info.Port = mapping.Port;
                }
            }
            if (context?.SerialPortInfos != null)
            {
                foreach (SimulationSerialMapping mapping in SerialMappings)
                {
                    SerialPortInfo info = context.SerialPortInfos.FirstOrDefault(item => item != null && string.Equals(item.Name, mapping.Name, StringComparison.Ordinal));
                    if (info == null) throw new InvalidOperationException($"串口端点映射指向未知通道:{mapping.Name}");
                    info.Port = mapping.PlatformPort;
                }
            }
        }

        public void ArmScenario(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains("..") || name.Contains("/") || name.Contains("\\") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException($"仿真场景名称无效:{name}");
            }
            EnsureCommandSucceeded(SendCommand("scenario.arm", new JObject { ["name"] = name }));
        }

        public void InitCardType() { }
        public bool InitCard() => IsCardInitialized;
        public void DownLoadConfig() { EnsureReady(); }
        public void SetAllAxisEquiv() { EnsureReady(); }

        public bool SetIO(IO io, bool isOpen)
        {
            if (io == null || string.IsNullOrWhiteSpace(io.Name)) return false;
            try
            {
                GatewayEnvelope response = SendCommand("io.write", new JObject { ["name"] = io.Name, ["value"] = isOpen });
                return RequireBool(response.Payload, "success");
            }
            catch { return false; }
        }

        public bool GetOutIO(IO io, ref bool value) => TryGetIo(io, "通用输出", ref value);
        public bool GetInIO(IO io, ref bool value) => TryGetIo(io, "通用输入", ref value);

        public void SettHomeParam(ushort card, ushort axis, ushort dir, ushort speed, ushort homeMode)
        {
            EnsureAxisExists(card, axis);
        }

        public void StartHome(ushort card, ushort axis)
        {
            EnsureCommandSucceeded(SendCommand("motion.home", AxisPayload(card, axis)));
        }

        public void CleanPos(ushort card, ushort axis)
        {
            EnsureCommandSucceeded(SendCommand("motion.clearPosition", AxisPayload(card, axis)));
        }

        public double GetAxisPos(ushort card, ushort axis)
        {
            GatewayAxisState state = GetAxis(card, axis);
            int equivalent = axisEquivalents.TryGetValue(AxisKey(card, axis), out int value) && value > 0 ? value : 1;
            return state.Position * equivalent;
        }

        public void SetMovParam(ushort card, ushort axis, double minVel, double maxVel, double acc, double dec, double stopVel, double sPara, int equiv)
        {
            if (maxVel <= 0 || acc <= 0 || dec <= 0 || equiv <= 0) throw new InvalidOperationException($"运动参数无效:{card}-{axis}");
            axisEquivalents[AxisKey(card, axis)] = equiv;
            EnsureCommandSucceeded(SendCommand("motion.setParameters", new JObject
            {
                ["card"] = (int)card,
                ["axis"] = (int)axis,
                ["maxVelocity"] = maxVel,
                ["acceleration"] = acc,
                ["deceleration"] = dec
            }));
        }

        public void Mov(ushort card, ushort axis, double distance, ushort positionMode, bool wait)
        {
            if (positionMode != 0 && positionMode != 1) throw new InvalidOperationException($"位置模式无效:{positionMode}");
            EnsureCommandSucceeded(SendCommand("motion.move", new JObject
            {
                ["card"] = (int)card,
                ["axis"] = (int)axis,
                ["position"] = distance,
                ["absolute"] = positionMode == 1
            }));
        }

        public void Jog(ushort card, ushort axis, ushort direction)
        {
            if (direction != 0 && direction != 1) throw new InvalidOperationException($"Jog方向无效:{direction}");
            EnsureCommandSucceeded(SendCommand("motion.jog", new JObject { ["card"] = (int)card, ["axis"] = (int)axis, ["direction"] = (int)direction }));
        }

        public void StopOneAxis(ushort card, ushort axis, ushort stopMode)
        {
            EnsureCommandSucceeded(SendCommand("motion.stop", AxisPayload(card, axis)));
        }

        public void StopConnect()
        {
            DisposeConnection();
        }

        public bool HomeStatus(ushort card, ushort axis) => GetAxis(card, axis).Homed;
        public bool GetInPos(ushort card, ushort axis) => GetAxis(card, axis).InPosition;
        public bool GetAxisSevon(ushort card, ushort axis) => GetAxis(card, axis).ServoOn;

        public void SetAxisSevon(ushort card, ushort axis, bool isSevon)
        {
            EnsureCommandSucceeded(SendCommand("motion.setServo", new JObject { ["card"] = (int)card, ["axis"] = (int)axis, ["value"] = isSevon }));
        }

        public void SetAllAxisSevonOn()
        {
            foreach (GatewayAxisState state in axisStates.Values) SetAxisSevon((ushort)state.Card, (ushort)state.Axis, true);
        }

        public void CleanAlarm()
        {
            EnsureCommandSucceeded(SendCommand("motion.clearAlarm", new JObject()));
        }

        public double GetAxisCurSpeed(ushort card, ushort axis)
        {
            GatewayAxisState state = GetAxis(card, axis);
            return state.Running ? state.MaxVelocity : 0;
        }

        public uint GetAxisIoStatus(ushort card, ushort axis)
        {
            GatewayAxisState state = GetAxis(card, axis);
            uint result = 0;
            if (state.PositiveLimit) result |= 1u << 1;
            if (state.NegativeLimit) result |= 1u << 2;
            if (state.Homed) result |= 1u << 3;
            if (state.Alarm) result |= 1u << 4;
            return result;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            DisposeConnection();
            snapshotReady.Dispose();
        }

        private GatewayEnvelope SendCommand(string type, JObject payload)
        {
            return SendRequest(type, payload, "command.ack", 1000);
        }

        private GatewayEnvelope SendRequest(string type, JObject payload, string expectedResponseType, int timeoutMs)
        {
            EnsureConnected();
            string requestId = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<GatewayEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pending.TryAdd(requestId, completion)) throw new InvalidOperationException("请求ID重复");
            try
            {
                var envelope = new JObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["messageType"] = type,
                    ["sessionId"] = sessionId,
                    ["requestId"] = requestId,
                    ["sequence"] = Interlocked.Increment(ref sendSequence),
                    ["payload"] = payload ?? new JObject()
                };
                byte[] body = Encoding.UTF8.GetBytes(envelope.ToString(Formatting.None));
                if (body.Length <= 0 || body.Length > MaxFrameLength) throw new InvalidOperationException($"协议帧长度无效:{body.Length}");
                lock (sendLock)
                {
                    byte[] prefix = BitConverter.GetBytes(body.Length);
                    stream.Write(prefix, 0, prefix.Length);
                    stream.Write(body, 0, body.Length);
                    stream.Flush();
                }
                if (!completion.Task.Wait(timeoutMs)) throw new TimeoutException($"模拟器响应超时:{type}");
                GatewayEnvelope response = completion.Task.GetAwaiter().GetResult();
                if (!string.Equals(response.MessageType, expectedResponseType, StringComparison.Ordinal)) throw new InvalidDataException($"响应类型无效:{response.MessageType}");
                return response;
            }
            finally
            {
                pending.TryRemove(requestId, out _);
            }
        }

        private async Task ReaderLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    byte[] prefix = new byte[4];
                    await ReadExactAsync(stream, prefix, token).ConfigureAwait(false);
                    int length = BitConverter.ToInt32(prefix, 0);
                    if (length <= 0 || length > MaxFrameLength) throw new InvalidDataException($"协议帧长度无效:{length}");
                    byte[] body = new byte[length];
                    await ReadExactAsync(stream, body, token).ConfigureAwait(false);
                    JObject root = JObject.Parse(Encoding.UTF8.GetString(body));
                    GatewayEnvelope envelope = ParseEnvelope(root);
                    if (envelope.ProtocolVersion != ProtocolVersion) throw new InvalidDataException($"协议版本不匹配:{envelope.ProtocolVersion}");
                    if (envelope.Sequence <= receiveSequence) throw new InvalidDataException($"接收序列无效:{envelope.Sequence}");
                    receiveSequence = envelope.Sequence;
                    lastReceiveTicks = Stopwatch.GetTimestamp();
                    if (envelope.MessageType == "state.snapshot")
                    {
                        ApplySnapshot(envelope.Payload);
                    }
                    else if (envelope.MessageType == "heartbeat" || envelope.MessageType == "state.changed" || envelope.MessageType == "command.completed" || envelope.MessageType == "fault.changed" || envelope.MessageType == "log")
                    {
                    }
                    else if (pending.TryGetValue(envelope.RequestId, out TaskCompletionSource<GatewayEnvelope> completion))
                    {
                        completion.TrySetResult(envelope);
                    }
                    else
                    {
                        throw new InvalidDataException($"收到无法关联的响应:{envelope.MessageType}-{envelope.RequestId}");
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { RaiseFault($"仿真网关连接异常:{ex.Message}"); }
        }

        private void ApplySnapshot(JObject payload)
        {
            if (!(payload["io"] is JArray ioArray) || !(payload["axes"] is JArray axisArray)) throw new InvalidDataException("状态快照字段缺失");
            var nextIo = new Dictionary<string, GatewayIoState>(StringComparer.Ordinal);
            foreach (JObject item in ioArray.OfType<JObject>())
            {
                var state = new GatewayIoState
                {
                    Name = RequireString(item, "Name"),
                    IoType = RequireString(item, "IoType"),
                    Value = RequireBool(item, "Value")
                };
                if (nextIo.ContainsKey(state.Name)) throw new InvalidDataException($"状态快照IO重复:{state.Name}");
                nextIo.Add(state.Name, state);
            }
            var nextAxes = new Dictionary<string, GatewayAxisState>(StringComparer.Ordinal);
            foreach (JObject item in axisArray.OfType<JObject>())
            {
                var state = new GatewayAxisState
                {
                    Card = RequireInt(item, "Card"), Axis = RequireInt(item, "Axis"), Name = RequireString(item, "Name"),
                    Position = RequireDouble(item, "Position"), TargetPosition = RequireDouble(item, "TargetPosition"), MaxVelocity = RequireDouble(item, "MaxVelocity"),
                    Running = RequireBool(item, "Running"), InPosition = RequireBool(item, "InPosition"), Homed = RequireBool(item, "Homed"), ServoOn = RequireBool(item, "ServoOn"),
                    PositiveLimit = RequireBool(item, "PositiveLimit"), NegativeLimit = RequireBool(item, "NegativeLimit"), Alarm = RequireBool(item, "Alarm")
                };
                string key = AxisKey((ushort)state.Card, (ushort)state.Axis);
                if (nextAxes.ContainsKey(key)) throw new InvalidDataException($"状态快照轴重复:{key}");
                nextAxes.Add(key, state);
            }
            ioStates.Clear();
            foreach (KeyValuePair<string, GatewayIoState> item in nextIo) ioStates[item.Key] = item.Value;
            axisStates.Clear();
            foreach (KeyValuePair<string, GatewayAxisState> item in nextAxes) axisStates[item.Key] = item.Value;
            snapshotReady.Set();
        }

        private void CheckHeartbeat(object state)
        {
            if (!IsCardInitialized) return;
            double elapsedMs = (Stopwatch.GetTimestamp() - Interlocked.Read(ref lastReceiveTicks)) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs > 1500) RaiseFault($"模拟器心跳超时:{elapsedMs:F0}ms");
        }

        private void RaiseFault(string message)
        {
            if (Interlocked.Exchange(ref faultRaised, 1) != 0) return;
            IsCardInitialized = false;
            foreach (TaskCompletionSource<GatewayEnvelope> completion in pending.Values) completion.TrySetException(new IOException(message));
            Faulted?.Invoke(message);
        }

        private void ParseMappings(JObject mappings)
        {
            ModbusMappings = ParseArray(mappings, "modbus", item => new SimulationModbusMapping
            {
                Name = RequireString(item, "name"), Address = RequireString(item, "address"), Port = RequireInt(item, "port"), UnitId = RequireInt(item, "unitId")
            });
            TcpMappings = ParseArray(mappings, "tcp", item => new SimulationTcpMapping
            {
                Name = RequireString(item, "name"), Address = RequireString(item, "address"), Port = RequireInt(item, "port"), PlatformType = RequireString(item, "platformType")
            });
            SerialMappings = ParseArray(mappings, "serial", item => new SimulationSerialMapping
            {
                Name = RequireString(item, "name"), PlatformPort = RequireString(item, "platformPort")
            });
        }

        private static IReadOnlyList<T> ParseArray<T>(JObject root, string name, Func<JObject, T> factory)
        {
            if (!(root[name] is JArray array)) throw new InvalidDataException($"端点映射字段缺失:{name}");
            return array.OfType<JObject>().Select(factory).ToList();
        }

        private bool TryGetIo(IO io, string expectedType, ref bool value)
        {
            if (io == null || string.IsNullOrWhiteSpace(io.Name)) return false;
            if (!ioStates.TryGetValue(io.Name, out GatewayIoState state) || !string.Equals(state.IoType, expectedType, StringComparison.Ordinal)) return false;
            value = state.Value;
            return true;
        }

        private GatewayAxisState GetAxis(ushort card, ushort axis)
        {
            EnsureReady();
            string key = AxisKey(card, axis);
            if (!axisStates.TryGetValue(key, out GatewayAxisState state)) throw new InvalidOperationException($"模拟器轴状态不存在:{key}");
            return state;
        }

        private void EnsureAxisExists(ushort card, ushort axis) { GetAxis(card, axis); }
        private void EnsureReady() { if (!IsCardInitialized) throw new InvalidOperationException("模拟器未就绪"); }
        private void EnsureConnected() { if (stream == null || !stream.IsConnected || cts == null || cts.IsCancellationRequested) throw new IOException("模拟器未连接"); }

        private static void EnsureCommandSucceeded(GatewayEnvelope response)
        {
            if (!RequireBool(response.Payload, "success")) throw new InvalidOperationException(response.Payload["error"]?.Value<string>() ?? "模拟器拒绝命令");
        }

        private void DisposeConnection()
        {
            IsCardInitialized = false;
            watchdog?.Dispose();
            watchdog = null;
            cts?.Cancel();
            try { stream?.Dispose(); } catch { }
            try { readerTask?.Wait(500); } catch { }
            cts?.Dispose();
            cts = null;
            stream = null;
            readerTask = null;
        }

        private static JObject AxisPayload(ushort card, ushort axis) => new JObject { ["card"] = (int)card, ["axis"] = (int)axis };
        private static string AxisKey(ushort card, ushort axis) => card + "-" + axis;

        private static PlcDevice CloneDevice(PlcDevice source)
        {
            return new PlcDevice { Name = source.Name, Protocol = source.Protocol, CpuType = source.CpuType, Ip = source.Ip, Port = source.Port, Rack = source.Rack, Slot = source.Slot, TimeoutMs = source.TimeoutMs, UnitId = source.UnitId };
        }

        private static GatewayEnvelope ParseEnvelope(JObject root)
        {
            string[] fields = { "protocolVersion", "messageType", "sessionId", "requestId", "sequence", "payload" };
            foreach (JProperty property in root.Properties()) if (!fields.Contains(property.Name, StringComparer.Ordinal)) throw new InvalidDataException($"协议消息包含未知字段:{property.Name}");
            foreach (string field in fields) if (root[field] == null || root[field].Type == JTokenType.Null) throw new InvalidDataException($"协议消息缺少字段:{field}");
            return new GatewayEnvelope
            {
                ProtocolVersion = RequireInt(root, "protocolVersion"), MessageType = RequireString(root, "messageType"), SessionId = RequireString(root, "sessionId"),
                RequestId = RequireString(root, "requestId"), Sequence = RequireLong(root, "sequence"), Payload = root["payload"] as JObject ?? throw new InvalidDataException("payload类型无效")
            };
        }

        private static string RequireString(JObject obj, string name) { string value = obj[name]?.Value<string>(); if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"字段为空:{name}"); return value; }
        private static bool RequireBool(JObject obj, string name) { if (obj[name]?.Type != JTokenType.Boolean) throw new InvalidDataException($"字段类型无效:{name}"); return obj[name].Value<bool>(); }
        private static int RequireInt(JObject obj, string name) { if (obj[name]?.Type != JTokenType.Integer) throw new InvalidDataException($"字段类型无效:{name}"); return obj[name].Value<int>(); }
        private static long RequireLong(JObject obj, string name) { if (obj[name]?.Type != JTokenType.Integer) throw new InvalidDataException($"字段类型无效:{name}"); return obj[name].Value<long>(); }
        private static double RequireDouble(JObject obj, string name) { JToken token = obj[name]; if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) throw new InvalidDataException($"字段类型无效:{name}"); return token.Value<double>(); }

        private static async Task ReadExactAsync(Stream source, byte[] buffer, CancellationToken token)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int count = await source.ReadAsync(buffer, offset, buffer.Length - offset, token).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("模拟器连接已关闭");
                offset += count;
            }
        }

        private sealed class GatewayEnvelope
        {
            public int ProtocolVersion { get; set; }
            public string MessageType { get; set; }
            public string SessionId { get; set; }
            public string RequestId { get; set; }
            public long Sequence { get; set; }
            public JObject Payload { get; set; }
        }

        private sealed class GatewayIoState { public string Name; public string IoType; public bool Value; }
        private sealed class GatewayAxisState
        {
            public int Card; public int Axis; public string Name; public double Position; public double TargetPosition; public double MaxVelocity;
            public bool Running; public bool InPosition; public bool Homed; public bool ServoOn; public bool PositiveLimit; public bool NegativeLimit; public bool Alarm;
        }
    }

    public sealed class SimulationModbusMapping { public string Name { get; set; } public string Address { get; set; } public int Port { get; set; } public int UnitId { get; set; } }
    public sealed class SimulationTcpMapping { public string Name { get; set; } public string Address { get; set; } public int Port { get; set; } public string PlatformType { get; set; } }
    public sealed class SimulationSerialMapping { public string Name { get; set; } public string PlatformPort { get; set; } }

    public static class SimulationManifestBuilder
    {
        public static JObject Build(EngineContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var io = new JArray((context.IoMap ?? new Dictionary<string, IO>()).Values.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name)).GroupBy(item => item.Name, StringComparer.Ordinal).Select(group =>
            {
                IO item = group.First();
                return new JObject { ["name"] = item.Name, ["ioType"] = item.IOType, ["card"] = item.CardNum, ["index"] = item.IOIndex ?? string.Empty };
            }));
            var axes = new JArray();
            if (context.CardStore != null)
            {
                int cards = context.CardStore.GetControlCardCount();
                for (int card = 0; card < cards; card++)
                {
                    int count = context.CardStore.GetAxisCount(card);
                    for (int axis = 0; axis < count; axis++)
                    {
                        if (!context.CardStore.TryGetAxis(card, axis, out Axis item)) throw new InvalidOperationException($"轴配置读取失败:{card}-{axis}");
                        axes.Add(new JObject { ["card"] = card, ["axis"] = axis, ["name"] = item.AxisName ?? $"Axis_{card}_{axis}" });
                    }
                }
            }
            var plc = new JArray((context.PlcStore?.Devices ?? Array.Empty<PlcDevice>()).Select(item => new JObject { ["name"] = item.Name, ["protocol"] = item.Protocol, ["unitId"] = item.UnitId }));
            var tcp = new JArray((context.SocketInfos ?? new List<SocketInfo>()).Where(item => item != null).Select(item => new JObject { ["name"] = item.Name, ["type"] = item.Type, ["frameMode"] = item.FrameMode, ["encodingName"] = item.EncodingName }));
            var serial = new JArray((context.SerialPortInfos ?? new List<SerialPortInfo>()).Where(item => item != null).Select(item => new JObject { ["name"] = item.Name, ["bitRate"] = item.BitRate, ["dataBit"] = item.DataBit, ["checkBit"] = item.CheckBit, ["stopBit"] = item.StopBit }));
            return new JObject
            {
                ["configHash"] = ComputeConfigHash(SF.ConfigPath), ["io"] = io, ["axes"] = axes, ["plcDevices"] = plc, ["tcpChannels"] = tcp, ["serialChannels"] = serial
            };
        }

        private static string ComputeConfigHash(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath) || !Directory.Exists(configPath)) throw new InvalidOperationException($"配置目录不存在:{configPath}");
            string[] files = Directory.GetFiles(configPath, "*.json", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            using (var memory = new MemoryStream())
            {
                foreach (string file in files)
                {
                    byte[] name = Encoding.UTF8.GetBytes(Path.GetFileName(file));
                    memory.Write(name, 0, name.Length);
                    byte[] data = File.ReadAllBytes(file);
                    memory.Write(data, 0, data.Length);
                }
                memory.Position = 0;
                using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(memory)).Replace("-", string.Empty);
            }
        }
    }
}
