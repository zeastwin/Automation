using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HslCommunication;
using HslCommunication.Core;
using HslCommunication.ModBus;

// 模块：PLC / HSL Modbus 适配。
// 职责范围：把平台 PLC 读写请求转换为 HslCommunication 调用，并返回原始通讯失败事实。
// 排查入口：连接或读写失败时先核对 PlcDeviceConfig、地址转换和 HSL 返回消息，再检查 PlcRuntimeService 会话状态。

namespace Automation
{
    internal interface IPlcAdapter : IDisposable
    {
        bool Connect(out string error);
        void Close();
        bool Read(PlcMapConfig map, out object[] values, out string error);
        bool ReadBatch(IReadOnlyList<PlcMapConfig> maps, out IReadOnlyDictionary<string, object[]> values, out string error);
        bool Write(PlcMapConfig map, IReadOnlyList<object> values, out string error);
    }

    internal static class HslAuthorizationGate
    {
        private static readonly object SyncRoot = new object();
        private static bool attempted;
        private static bool authorized;

        public static bool EnsureAuthorized(out string error)
        {
            lock (SyncRoot)
            {
                if (!attempted)
                {
                    attempted = true;
                    authorized = Authorization.SetAuthorizationCode("123456");
                }
                error = authorized ? null : "HslCommunication 7.0.1授权初始化失败，PLC子系统已禁用。";
                return authorized;
            }
        }
    }

    internal sealed class HslModbusAdapter : IPlcAdapter
    {
        private const int MaxRegisterCount = 120;
        private const int MaxCoilCount = 1968;
        private readonly PlcDeviceConfig config;
        private ModbusTcpNet client;

        public HslModbusAdapter(PlcDeviceConfig config)
        {
            this.config = PlcModelClone.Clone(config) ?? throw new ArgumentNullException(nameof(config));
        }

        public bool Connect(out string error)
        {
            error = null;
            Close();
            if (!HslAuthorizationGate.EnsureAuthorized(out error)) return false;
            try
            {
                client = new ModbusTcpNet(config.IpAddress, config.Port, (byte)config.UnitId)
                {
                    ConnectTimeOut = config.ConnectTimeoutMs,
                    AddressStartWithZero = config.AddressStartWithZero,
                    IsStringReverse = config.IsStringReverse,
                    DataFormat = ParseDataFormat(config.DataFormat)
                };
                OperateResult result = client.ConnectServer();
                if (!result.IsSuccess)
                {
                    error = FormatError("连接", result);
                    Close();
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "连接异常:" + ex.Message;
                Close();
                return false;
            }
        }

        public void Close()
        {
            ModbusTcpNet target = client;
            client = null;
            if (target == null) return;
            try { target.ConnectClose(); } catch { }
        }

        public bool Read(PlcMapConfig map, out object[] values, out string error)
        {
            values = null;
            error = null;
            if (client == null) { error = "PLC尚未连接。"; return false; }
            try
            {
                if (map.Area == PlcArea.Coil || map.Area == PlcArea.DiscreteInput)
                {
                    return ReadBits(map, out values, out error);
                }
                if (map.DataType == PlcDataType.String)
                {
                    OperateResult<string> result = client.ReadString(BuildRegisterAddress(map.Area, map.StartAddress),
                        checked((ushort)map.StringByteLength));
                    if (!result.IsSuccess) { error = FormatError("读取字符串", result); return false; }
                    string content = result.Content ?? string.Empty;
                    int terminator = content.IndexOf('\0');
                    values = new object[] { terminator < 0 ? content : content.Substring(0, terminator) };
                    return true;
                }

                var output = new List<object>(map.ElementCount);
                int registersPerElement = RegistersPerElement(map.DataType);
                int maxElements = map.DataType == PlcDataType.Byte
                    ? MaxRegisterCount * 2
                    : Math.Max(1, MaxRegisterCount / registersPerElement);
                for (int offset = 0; offset < map.ElementCount; offset += maxElements)
                {
                    int count = Math.Min(maxElements, map.ElementCount - offset);
                    int address = map.DataType == PlcDataType.Byte
                        ? checked(map.StartAddress + offset / 2)
                        : checked(map.StartAddress + offset * registersPerElement);
                    if (!ReadRegisterChunk(map.Area, address, map.DataType, count, output, out error)) return false;
                }
                values = output.ToArray();
                return true;
            }
            catch (Exception ex)
            {
                error = "读取异常:" + ex.Message;
                return false;
            }
        }

        public bool ReadBatch(IReadOnlyList<PlcMapConfig> maps,
            out IReadOnlyDictionary<string, object[]> values, out string error)
        {
            var output = new Dictionary<string, object[]>(StringComparer.Ordinal);
            values = output;
            error = null;
            if (maps == null || maps.Count == 0) return true;
            if (client == null) { error = "PLC尚未连接。"; return false; }
            try
            {
                foreach (IGrouping<PlcArea, PlcMapConfig> areaGroup in maps.GroupBy(map => map.Area))
                {
                    int maximum = areaGroup.Key == PlcArea.Coil || areaGroup.Key == PlcArea.DiscreteInput
                        ? MaxCoilCount : MaxRegisterCount;
                    List<ReadBlock> blocks = BuildReadBlocks(areaGroup.OrderBy(map => map.StartAddress), maximum);
                    foreach (ReadBlock block in blocks)
                    {
                        if (block.Maps.Count == 1 && PlcConfigStore.GetAddressSpan(block.Maps[0]) > maximum)
                        {
                            if (!Read(block.Maps[0], out object[] single, out error)) return false;
                            output.Add(block.Maps[0].Id, single);
                            continue;
                        }
                        if (areaGroup.Key == PlcArea.Coil || areaGroup.Key == PlcArea.DiscreteInput)
                        {
                            string address = block.Start.ToString(CultureInfo.InvariantCulture);
                            OperateResult<bool[]> result = areaGroup.Key == PlcArea.Coil
                                ? client.ReadCoil(address, (ushort)block.Length)
                                : client.ReadDiscrete(address, (ushort)block.Length);
                            if (!result.IsSuccess) { error = FormatError("批量读取线圈", result); return false; }
                            foreach (PlcMapConfig map in block.Maps)
                            {
                                int offset = map.StartAddress - block.Start;
                                output.Add(map.Id, result.Content.Skip(offset).Take(map.ElementCount)
                                    .Select(value => (object)value).ToArray());
                            }
                        }
                        else
                        {
                            OperateResult<byte[]> result = client.Read(
                                BuildRegisterAddress(areaGroup.Key, block.Start), (ushort)block.Length);
                            if (!result.IsSuccess) { error = FormatError("批量读取寄存器", result); return false; }
                            foreach (PlcMapConfig map in block.Maps)
                            {
                                output.Add(map.Id, DecodeRegisterValues(map, result.Content,
                                    checked((map.StartAddress - block.Start) * 2)));
                            }
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "批量读取异常:" + ex.Message;
                return false;
            }
        }

        public bool Write(PlcMapConfig map, IReadOnlyList<object> values, out string error)
        {
            error = null;
            if (client == null) { error = "PLC尚未连接。"; return false; }
            if (values == null || values.Count != map.ElementCount) { error = "写入值数量与元素数量不一致。"; return false; }
            if (!TryNormalizeValues(map.DataType, values, out object normalized, out error)) return false;
            try
            {
                if (map.DataType == PlcDataType.String)
                {
                    OperateResult stringResult = client.Write(
                        map.StartAddress.ToString(CultureInfo.InvariantCulture),
                        ((string[])normalized)[0], map.StringByteLength);
                    if (!stringResult.IsSuccess) { error = FormatError("写入", stringResult); return false; }
                    return true;
                }

                int registersPerElement = RegistersPerElement(map.DataType);
                int maxElements = map.DataType == PlcDataType.Boolean
                    ? MaxCoilCount
                    : map.DataType == PlcDataType.Byte
                        ? MaxRegisterCount * 2
                        : Math.Max(1, MaxRegisterCount / registersPerElement);
                Array source = (Array)normalized;
                for (int offset = 0; offset < source.Length; offset += maxElements)
                {
                    int count = Math.Min(maxElements, source.Length - offset);
                    int startAddress = map.DataType == PlcDataType.Boolean
                        ? checked(map.StartAddress + offset)
                        : map.DataType == PlcDataType.Byte
                            ? checked(map.StartAddress + offset / 2)
                            : checked(map.StartAddress + offset * registersPerElement);
                    if (!WriteChunk(map.DataType, startAddress, source, offset, count, out error)) return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "写入异常:" + ex.Message;
                return false;
            }
        }

        private bool WriteChunk(PlcDataType dataType, int startAddress, Array source, int offset, int count,
            out string error)
        {
            error = null;
            string address = startAddress.ToString(CultureInfo.InvariantCulture);
            OperateResult result;
            switch (dataType)
            {
                case PlcDataType.Boolean: result = client.Write(address, Slice<bool>(source, offset, count)); break;
                case PlcDataType.Byte: result = client.Write(address, Slice<byte>(source, offset, count)); break;
                case PlcDataType.UShort: result = client.Write(address, Slice<ushort>(source, offset, count)); break;
                case PlcDataType.Short: result = client.Write(address, Slice<short>(source, offset, count)); break;
                case PlcDataType.UInt: result = client.Write(address, Slice<uint>(source, offset, count)); break;
                case PlcDataType.Int: result = client.Write(address, Slice<int>(source, offset, count)); break;
                case PlcDataType.Float: result = client.Write(address, Slice<float>(source, offset, count)); break;
                case PlcDataType.Double: result = client.Write(address, Slice<double>(source, offset, count)); break;
                default: error = $"不支持的数据类型:{dataType}"; return false;
            }
            if (!result.IsSuccess) { error = FormatError("写入", result); return false; }
            return true;
        }

        private static T[] Slice<T>(Array source, int offset, int count)
        {
            var result = new T[count];
            Array.Copy(source, offset, result, 0, count);
            return result;
        }

        public void Dispose()
        {
            Close();
        }

        private bool ReadBits(PlcMapConfig map, out object[] values, out string error)
        {
            error = null;
            var output = new List<object>(map.ElementCount);
            for (int offset = 0; offset < map.ElementCount; offset += MaxCoilCount)
            {
                int count = Math.Min(MaxCoilCount, map.ElementCount - offset);
                string address = (map.StartAddress + offset).ToString(CultureInfo.InvariantCulture);
                OperateResult<bool[]> result = map.Area == PlcArea.Coil
                    ? client.ReadCoil(address, (ushort)count)
                    : client.ReadDiscrete(address, (ushort)count);
                if (!result.IsSuccess) { values = null; error = FormatError("读取线圈", result); return false; }
                output.AddRange(result.Content.Select(value => (object)value));
            }
            values = output.ToArray();
            return true;
        }

        private object[] DecodeRegisterValues(PlcMapConfig map, byte[] bytes, int byteOffset)
        {
            switch (map.DataType)
            {
                case PlcDataType.Byte:
                    return client.ByteTransform.TransByte(bytes, byteOffset, map.ElementCount)
                        .Select(value => (object)value).ToArray();
                case PlcDataType.UShort:
                    return client.ByteTransform.TransUInt16(bytes, byteOffset, map.ElementCount)
                        .Select(value => (object)value).ToArray();
                case PlcDataType.Short:
                    return client.ByteTransform.TransInt16(bytes, byteOffset, map.ElementCount)
                        .Select(value => (object)value).ToArray();
                case PlcDataType.UInt:
                    return client.ByteTransform.TransUInt32(bytes, byteOffset, map.ElementCount)
                        .Select(value => (object)value).ToArray();
                case PlcDataType.Int:
                    return client.ByteTransform.TransInt32(bytes, byteOffset, map.ElementCount)
                        .Select(value => (object)value).ToArray();
                case PlcDataType.Float:
                    return client.ByteTransform.TransSingle(bytes, byteOffset, map.ElementCount)
                        .Select(value => (object)value).ToArray();
                case PlcDataType.Double:
                    return client.ByteTransform.TransDouble(bytes, byteOffset, map.ElementCount)
                        .Select(value => (object)value).ToArray();
                case PlcDataType.String:
                    string content = client.ByteTransform.TransString(bytes, byteOffset, map.StringByteLength, Encoding.ASCII)
                        ?? string.Empty;
                    int terminator = content.IndexOf('\0');
                    return new object[] { terminator < 0 ? content : content.Substring(0, terminator) };
                default:
                    throw new InvalidOperationException($"不支持的批量读取类型:{map.DataType}");
            }
        }

        private static List<ReadBlock> BuildReadBlocks(IEnumerable<PlcMapConfig> maps, int maximumLength)
        {
            var result = new List<ReadBlock>();
            ReadBlock current = null;
            foreach (PlcMapConfig map in maps)
            {
                int span = PlcConfigStore.GetAddressSpan(map);
                int end = checked(map.StartAddress + span);
                if (current == null || map.StartAddress > current.End
                    || checked(Math.Max(current.End, end) - current.Start) > maximumLength)
                {
                    current = new ReadBlock { Start = map.StartAddress, End = end };
                    result.Add(current);
                }
                else current.End = Math.Max(current.End, end);
                current.Maps.Add(map);
            }
            return result;
        }

        private sealed class ReadBlock
        {
            public int Start;
            public int End;
            public int Length => End - Start;
            public readonly List<PlcMapConfig> Maps = new List<PlcMapConfig>();
        }

        private bool ReadRegisterChunk(PlcArea area, int startAddress, PlcDataType dataType, int count,
            List<object> output, out string error)
        {
            error = null;
            string address = BuildRegisterAddress(area, startAddress);
            switch (dataType)
            {
                case PlcDataType.Byte:
                    OperateResult<byte[]> bytes = client.Read(address, (ushort)((count + 1) / 2));
                    if (!bytes.IsSuccess) { error = FormatError("读取Byte", bytes); return false; }
                    output.AddRange(bytes.Content.Take(count).Select(value => (object)value));
                    return true;
                case PlcDataType.UShort: return Append(client.ReadUInt16(address, (ushort)count), output, "读取UShort", out error);
                case PlcDataType.Short: return Append(client.ReadInt16(address, (ushort)count), output, "读取Short", out error);
                case PlcDataType.UInt: return Append(client.ReadUInt32(address, (ushort)count), output, "读取UInt", out error);
                case PlcDataType.Int: return Append(client.ReadInt32(address, (ushort)count), output, "读取Int", out error);
                case PlcDataType.Float: return Append(client.ReadFloat(address, (ushort)count), output, "读取Float", out error);
                case PlcDataType.Double: return Append(client.ReadDouble(address, (ushort)count), output, "读取Double", out error);
                default: error = $"不支持的寄存器类型:{dataType}"; return false;
            }
        }

        private static bool Append<T>(OperateResult<T[]> result, List<object> output, string action, out string error)
        {
            error = null;
            if (!result.IsSuccess) { error = FormatError(action, result); return false; }
            output.AddRange(result.Content.Select(value => (object)value));
            return true;
        }

        internal static bool TryNormalizeValues(PlcDataType dataType, IReadOnlyList<object> values,
            out object normalized, out string error)
        {
            normalized = null;
            error = null;
            try
            {
                switch (dataType)
                {
                    case PlcDataType.String:
                        normalized = values.Select(value => value as string ?? throw new FormatException("字符串值类型无效。"))
                            .ToArray();
                        return true;
                    case PlcDataType.Boolean:
                        normalized = values.Select(value =>
                        {
                            double number = RequireFiniteDouble(value);
                            if (number != 0d && number != 1d) throw new FormatException("Boolean值只能是0或1。");
                            return number == 1d;
                        }).ToArray();
                        return true;
                    case PlcDataType.Byte: normalized = values.Select(value => checked((byte)RequireInteger(value, byte.MinValue, byte.MaxValue))).ToArray(); return true;
                    case PlcDataType.UShort: normalized = values.Select(value => checked((ushort)RequireInteger(value, ushort.MinValue, ushort.MaxValue))).ToArray(); return true;
                    case PlcDataType.Short: normalized = values.Select(value => checked((short)RequireInteger(value, short.MinValue, short.MaxValue))).ToArray(); return true;
                    case PlcDataType.UInt: normalized = values.Select(value => checked((uint)RequireInteger(value, uint.MinValue, uint.MaxValue))).ToArray(); return true;
                    case PlcDataType.Int: normalized = values.Select(value => checked((int)RequireInteger(value, int.MinValue, int.MaxValue))).ToArray(); return true;
                    case PlcDataType.Float:
                        normalized = values.Select(value =>
                        {
                            double number = RequireFiniteDouble(value);
                            if (number < -float.MaxValue || number > float.MaxValue) throw new OverflowException("Float值超出范围。");
                            return (float)number;
                        }).ToArray();
                        return true;
                    case PlcDataType.Double: normalized = values.Select(RequireFiniteDouble).ToArray(); return true;
                    default: error = $"不支持的数据类型:{dataType}"; return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static double RequireFiniteDouble(object value)
        {
            if (value == null) throw new FormatException("数值为空。" );
            double number;
            if (value is double direct) number = direct;
            else if (!double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture, out number)) throw new FormatException($"数值格式无效:{value}");
            if (double.IsNaN(number) || double.IsInfinity(number)) throw new FormatException("数值必须是有限数。" );
            return number;
        }

        private static decimal RequireInteger(object value, decimal minimum, decimal maximum)
        {
            double number = RequireFiniteDouble(value);
            if (number != Math.Truncate(number)) throw new FormatException($"整数类型禁止小数:{number.ToString(CultureInfo.InvariantCulture)}");
            decimal decimalValue = Convert.ToDecimal(number);
            if (decimalValue < minimum || decimalValue > maximum) throw new OverflowException("整数值超出目标类型范围。" );
            return decimalValue;
        }

        private static string BuildRegisterAddress(PlcArea area, int address)
        {
            string value = address.ToString(CultureInfo.InvariantCulture);
            return area == PlcArea.InputRegister ? "x=4;" + value : value;
        }

        private static int RegistersPerElement(PlcDataType type)
        {
            switch (type)
            {
                case PlcDataType.Byte:
                case PlcDataType.UShort:
                case PlcDataType.Short: return 1;
                case PlcDataType.UInt:
                case PlcDataType.Int:
                case PlcDataType.Float: return 2;
                case PlcDataType.Double: return 4;
                default: throw new InvalidOperationException($"类型没有寄存器宽度:{type}");
            }
        }

        private static DataFormat ParseDataFormat(string value)
        {
            return (DataFormat)Enum.Parse(typeof(DataFormat), value, false);
        }

        private static string FormatError(string action, OperateResult result)
        {
            return $"{action}失败，错误码={result.ErrorCode}，信息={result.Message}";
        }
    }
}
