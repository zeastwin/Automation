using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using S7.Net;

namespace Automation
{
    public sealed class PlcHub : IDisposable
    {
        private readonly ConcurrentDictionary<string, PlcSession> sessions = new ConcurrentDictionary<string, PlcSession>(StringComparer.OrdinalIgnoreCase);

        public bool TryConnect(PlcDevice device, out string error)
        {
            error = null;
            if (device == null)
            {
                error = "PLC设备为空";
                return false;
            }
            if (!PlcConfigStore.ValidateDevices(new List<PlcDevice> { device }, out error))
            {
                return false;
            }

            PlcSession session = sessions.GetOrAdd(device.Name, _ => new PlcSession(device));
            lock (session.SyncRoot)
            {
                try
                {
                    session.UpdateDevice(device);
                    if (session.IsConnected)
                    {
                        return true;
                    }
                    session.Connect();
                    return session.IsConnected;
                }
                catch (Exception ex)
                {
                    session.Disconnect();
                    error = ex.Message;
                    return false;
                }
            }
        }

        public void Disconnect(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }
            if (sessions.TryGetValue(name, out PlcSession session))
            {
                lock (session.SyncRoot)
                {
                    session.Disconnect();
                }
            }
        }

        public bool Reconnect(PlcDevice device, out string error)
        {
            error = null;
            if (device == null)
            {
                error = "PLC设备为空";
                return false;
            }
            PlcSession session = sessions.GetOrAdd(device.Name, _ => new PlcSession(device));
            lock (session.SyncRoot)
            {
                session.Disconnect();
                return TryConnect(device, out error);
            }
        }

        public void DisconnectAll()
        {
            foreach (PlcSession session in sessions.Values)
            {
                lock (session.SyncRoot)
                {
                    session.Disconnect();
                }
            }
        }

        public bool TryReadValue(PlcDevice device, PlcMapItem map, out object value, out string error)
        {
            value = null;
            error = null;
            if (!TryEnsureConnected(device, out error))
            {
                return false;
            }
            if (!TryParseDataType(map?.DataType, out PlcDataType dataType, out error))
            {
                return false;
            }
            if (!TryParseDirection(map?.Direction, out PlcDirection direction, out error))
            {
                return false;
            }
            if (direction == PlcDirection.Write)
            {
                error = "映射为写PLC，无法读取";
                return false;
            }
            if (!TryValidateQuantity(dataType, map.Quantity, out error))
            {
                return false;
            }
            if (!TryParseAddress(device.Protocol, map.PlcAddress, dataType, map.Quantity, out PlcAddress address, out error))
            {
                return false;
            }

            PlcSession session = sessions[device.Name];
            lock (session.SyncRoot)
            {
                try
                {
                    value = session.ReadValue(address, dataType, map.Quantity);
                    return true;
                }
                catch (Exception ex)
                {
                    session.Disconnect();
                    error = ex.Message;
                    return false;
                }
            }
        }

        public bool TryWriteValue(PlcDevice device, PlcMapItem map, object inputValue, out string error)
        {
            error = null;
            if (!TryEnsureConnected(device, out error))
            {
                return false;
            }
            if (!TryParseDataType(map?.DataType, out PlcDataType dataType, out error))
            {
                return false;
            }
            if (!TryParseDirection(map?.Direction, out PlcDirection direction, out error))
            {
                return false;
            }
            if (direction == PlcDirection.Read)
            {
                error = "映射为读PLC，无法写入";
                return false;
            }
            if (!TryValidateQuantity(dataType, map.Quantity, out error))
            {
                return false;
            }
            if (!TryParseAddress(device.Protocol, map.PlcAddress, dataType, map.Quantity, out PlcAddress address, out error))
            {
                return false;
            }

            PlcSession session = sessions[device.Name];
            lock (session.SyncRoot)
            {
                try
                {
                    session.WriteValue(address, dataType, map.Quantity, inputValue);
                    return true;
                }
                catch (Exception ex)
                {
                    session.Disconnect();
                    error = ex.Message;
                    return false;
                }
            }
        }

        public bool TryReadValue(PlcDevice device, string dataTypeText, string addressText, int quantity, out object value, out string error)
        {
            value = null;
            error = null;
            if (!TryEnsureConnected(device, out error))
            {
                return false;
            }
            if (!TryParseDataType(dataTypeText, out PlcDataType dataType, out error))
            {
                return false;
            }
            if (!TryValidateQuantity(dataType, quantity, out error))
            {
                return false;
            }
            if (!TryParseAddress(device.Protocol, addressText, dataType, quantity, out PlcAddress address, out error))
            {
                return false;
            }

            PlcSession session = sessions[device.Name];
            lock (session.SyncRoot)
            {
                try
                {
                    value = session.ReadValue(address, dataType, quantity);
                    return true;
                }
                catch (Exception ex)
                {
                    session.Disconnect();
                    error = ex.Message;
                    return false;
                }
            }
        }

        public bool TryWriteValue(PlcDevice device, string dataTypeText, string addressText, int quantity, object inputValue, out string error)
        {
            error = null;
            if (!TryEnsureConnected(device, out error))
            {
                return false;
            }
            if (!TryParseDataType(dataTypeText, out PlcDataType dataType, out error))
            {
                return false;
            }
            if (!TryValidateQuantity(dataType, quantity, out error))
            {
                return false;
            }
            if (!TryParseAddress(device.Protocol, addressText, dataType, quantity, out PlcAddress address, out error))
            {
                return false;
            }

            PlcSession session = sessions[device.Name];
            lock (session.SyncRoot)
            {
                try
                {
                    session.WriteValue(address, dataType, quantity, inputValue);
                    return true;
                }
                catch (Exception ex)
                {
                    session.Disconnect();
                    error = ex.Message;
                    return false;
                }
            }
        }

        private bool TryEnsureConnected(PlcDevice device, out string error)
        {
            error = null;
            if (device == null)
            {
                error = "PLC设备为空";
                return false;
            }
            if (!PlcConfigStore.ValidateDevices(new List<PlcDevice> { device }, out error))
            {
                return false;
            }
            PlcSession session = sessions.GetOrAdd(device.Name, _ => new PlcSession(device));
            lock (session.SyncRoot)
            {
                session.UpdateDevice(device);
                if (!session.IsConnected)
                {
                    try
                    {
                        session.Connect();
                    }
                    catch (Exception ex)
                    {
                        session.Disconnect();
                        error = ex.Message;
                        return false;
                    }
                }
                return session.IsConnected;
            }
        }

        public void Dispose()
        {
            DisconnectAll();
        }

        private enum PlcDataType
        {
            String,
            Boolean,
            Byte,
            UShort,
            Short,
            UInt,
            Int,
            Float,
            Double
        }

        private enum PlcDirection
        {
            Read,
            Write,
            ReadWrite
        }

        private abstract class PlcAddress
        {
            public string Protocol { get; set; }
        }

        private sealed class S7Address : PlcAddress
        {
            public DataType Area { get; set; }
            public int DbNumber { get; set; }
            public int StartByte { get; set; }
            public int BitIndex { get; set; }
            public bool HasBit { get; set; }
        }

        private sealed class ModbusAddress : PlcAddress
        {
            public ModbusArea Area { get; set; }
            public ushort Address { get; set; }
        }

        private enum ModbusArea
        {
            Coil,
            DiscreteInput,
            HoldingRegister,
            InputRegister
        }

        private sealed class PlcSession
        {
            public readonly object SyncRoot = new object();
            private PlcDevice device;
            private Plc s7Client;
            private ModbusTcpClient modbusClient;

            public PlcSession(PlcDevice device)
            {
                this.device = device;
            }

            public bool IsConnected
            {
                get
                {
                    if (device == null)
                    {
                        return false;
                    }
                    if (string.Equals(device.Protocol, "S7", StringComparison.OrdinalIgnoreCase))
                    {
                        return s7Client != null && s7Client.IsConnected;
                    }
                    return modbusClient != null && modbusClient.IsConnected;
                }
            }

            public void UpdateDevice(PlcDevice newDevice)
            {
                device = newDevice;
            }

            public void Connect()
            {
                if (device == null)
                {
                    throw new InvalidOperationException("PLC设备为空");
                }
                if (string.Equals(device.Protocol, "S7", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseCpuType(device.CpuType, out CpuType cpuType))
                    {
                        throw new InvalidOperationException($"CPU类型无效:{device.CpuType}");
                    }
                    s7Client?.Close();
                    s7Client = new Plc(cpuType, device.Ip, (short)device.Rack, (short)device.Slot);
                    s7Client.ReadTimeout = device.TimeoutMs;
                    s7Client.WriteTimeout = device.TimeoutMs;
                    s7Client.Open();
                    if (!s7Client.IsConnected)
                    {
                        throw new InvalidOperationException("S7连接失败");
                    }
                }
                else
                {
                    modbusClient?.Disconnect();
                    modbusClient = new ModbusTcpClient();
                    modbusClient.Connect(device.Ip, device.Port, device.TimeoutMs);
                }
            }

            public void Disconnect()
            {
                if (s7Client != null)
                {
                    s7Client.Close();
                    s7Client = null;
                }
                if (modbusClient != null)
                {
                    modbusClient.Disconnect();
                    modbusClient = null;
                }
            }

            public object ReadValue(PlcAddress address, PlcDataType dataType, int quantity)
            {
                if (address is S7Address s7Address)
                {
                    return ReadS7Value(s7Address, dataType, quantity);
                }
                if (address is ModbusAddress modbusAddress)
                {
                    return ReadModbusValue(modbusAddress, dataType, quantity);
                }
                throw new InvalidOperationException("PLC地址类型不支持");
            }

            public void WriteValue(PlcAddress address, PlcDataType dataType, int quantity, object inputValue)
            {
                if (address is S7Address s7Address)
                {
                    WriteS7Value(s7Address, dataType, quantity, inputValue);
                    return;
                }
                if (address is ModbusAddress modbusAddress)
                {
                    WriteModbusValue(modbusAddress, dataType, quantity, inputValue);
                    return;
                }
                throw new InvalidOperationException("PLC地址类型不支持");
            }

            private object ReadS7Value(S7Address address, PlcDataType dataType, int quantity)
            {
                if (s7Client == null || !s7Client.IsConnected)
                {
                    throw new InvalidOperationException("S7未连接");
                }
                if (dataType == PlcDataType.Boolean)
                {
                    byte[] buffer = s7Client.ReadBytes(address.Area, address.DbNumber, address.StartByte, 1);
                    bool result = (buffer[0] & (1 << address.BitIndex)) != 0;
                    return result ? 1d : 0d;
                }
                int byteCount = GetByteCount(dataType, quantity);
                byte[] bytes = s7Client.ReadBytes(address.Area, address.DbNumber, address.StartByte, byteCount);
                    return ConvertBytesToValue(bytes, dataType, quantity, true);
            }

            private void WriteS7Value(S7Address address, PlcDataType dataType, int quantity, object inputValue)
            {
                if (s7Client == null || !s7Client.IsConnected)
                {
                    throw new InvalidOperationException("S7未连接");
                }
                if (address.Area == DataType.Input)
                {
                    throw new InvalidOperationException("S7输入区禁止写入");
                }
                if (dataType == PlcDataType.Boolean)
                {
                    byte[] buffer = s7Client.ReadBytes(address.Area, address.DbNumber, address.StartByte, 1);
                    bool target = ConvertToBool(inputValue);
                    if (target)
                    {
                        buffer[0] |= (byte)(1 << address.BitIndex);
                    }
                    else
                    {
                        buffer[0] &= (byte)~(1 << address.BitIndex);
                    }
                    s7Client.WriteBytes(address.Area, address.DbNumber, address.StartByte, buffer);
                    return;
                }
                byte[] bytes = ConvertValueToBytes(inputValue, dataType, quantity, true);
                s7Client.WriteBytes(address.Area, address.DbNumber, address.StartByte, bytes);
            }

            private object ReadModbusValue(ModbusAddress address, PlcDataType dataType, int quantity)
            {
                if (modbusClient == null || !modbusClient.IsConnected)
                {
                    throw new InvalidOperationException("Modbus未连接");
                }
                byte unitId = (byte)device.UnitId;
                if (dataType == PlcDataType.Boolean)
                {
                    byte[] bits = address.Area == ModbusArea.Coil
                        ? modbusClient.ReadCoils(unitId, address.Address, (ushort)quantity)
                        : modbusClient.ReadDiscreteInputs(unitId, address.Address, (ushort)quantity);
                    if (bits.Length < 1)
                    {
                        throw new InvalidOperationException("Modbus读取线圈失败");
                    }
                    return bits[0] != 0 ? 1d : 0d;
                }
                ushort[] registers = address.Area == ModbusArea.HoldingRegister
                    ? modbusClient.ReadHoldingRegisters(unitId, address.Address, (ushort)GetRegisterCount(dataType, quantity))
                    : modbusClient.ReadInputRegisters(unitId, address.Address, (ushort)GetRegisterCount(dataType, quantity));
                byte[] bytes = RegistersToBytes(registers);
                return ConvertBytesToValue(bytes, dataType, quantity, true);
            }

            private void WriteModbusValue(ModbusAddress address, PlcDataType dataType, int quantity, object inputValue)
            {
                if (modbusClient == null || !modbusClient.IsConnected)
                {
                    throw new InvalidOperationException("Modbus未连接");
                }
                byte unitId = (byte)device.UnitId;
                if (dataType == PlcDataType.Boolean)
                {
                    bool target = ConvertToBool(inputValue);
                    if (quantity <= 1)
                    {
                        modbusClient.WriteSingleCoil(unitId, address.Address, target);
                    }
                    else
                    {
                        bool[] values = new bool[quantity];
                        for (int i = 0; i < values.Length; i++)
                        {
                            values[i] = target;
                        }
                        modbusClient.WriteMultipleCoils(unitId, address.Address, values);
                    }
                    return;
                }
                byte[] writeBytes;
                if (dataType == PlcDataType.String)
                {
                    writeBytes = BuildStringBytes(inputValue, GetRegisterCount(dataType, quantity) * 2);
                }
                else if (dataType == PlcDataType.Byte)
                {
                    byte value = (byte)Clamp(ConvertToNumber(inputValue), byte.MinValue, byte.MaxValue);
                    writeBytes = new[] { (byte)0x00, value };
                }
                else
                {
                    writeBytes = ConvertValueToBytes(inputValue, dataType, quantity, true);
                }
                ushort[] registers = BytesToRegisters(writeBytes);
                if (registers.Length == 1)
                {
                    modbusClient.WriteSingleRegister(unitId, address.Address, registers[0]);
                }
                else
                {
                    modbusClient.WriteMultipleRegisters(unitId, address.Address, registers);
                }
            }

            private static ushort[] BytesToRegisters(byte[] bytes)
            {
                if (bytes == null || bytes.Length == 0 || bytes.Length % 2 != 0)
                {
                    throw new InvalidOperationException("寄存器字节长度无效");
                }
                int count = bytes.Length / 2;
                ushort[] registers = new ushort[count];
                for (int i = 0; i < count; i++)
                {
                    registers[i] = (ushort)((bytes[i * 2] << 8) | bytes[i * 2 + 1]);
                }
                return registers;
            }
        }

        private static bool TryParseCpuType(string text, out CpuType cpuType)
        {
            cpuType = CpuType.S71200;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            string normalized = text.Replace("-", string.Empty).Trim().ToUpperInvariant();
            switch (normalized)
            {
                case "S7200":
                    cpuType = CpuType.S7200;
                    return true;
                case "S7300":
                    cpuType = CpuType.S7300;
                    return true;
                case "S7400":
                    cpuType = CpuType.S7400;
                    return true;
                case "S71200":
                    cpuType = CpuType.S71200;
                    return true;
                case "S71500":
                    cpuType = CpuType.S71500;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseDataType(string text, out PlcDataType dataType, out string error)
        {
            error = null;
            dataType = PlcDataType.Float;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "数据类型不能为空";
                return false;
            }
            switch (text.Trim())
            {
                case "String":
                    dataType = PlcDataType.String;
                    return true;
                case "Boolean":
                    dataType = PlcDataType.Boolean;
                    return true;
                case "Byte":
                    dataType = PlcDataType.Byte;
                    return true;
                case "UShort":
                    dataType = PlcDataType.UShort;
                    return true;
                case "Short":
                    dataType = PlcDataType.Short;
                    return true;
                case "UInt":
                    dataType = PlcDataType.UInt;
                    return true;
                case "Int":
                    dataType = PlcDataType.Int;
                    return true;
                case "Float":
                    dataType = PlcDataType.Float;
                    return true;
                case "Double":
                    dataType = PlcDataType.Double;
                    return true;
                default:
                    error = $"数据类型不支持:{text}";
                    return false;
            }
        }

        private static bool TryParseDirection(string text, out PlcDirection direction, out string error)
        {
            error = null;
            direction = PlcDirection.Read;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "读写方向不能为空";
                return false;
            }
            switch (text.Trim())
            {
                case "读PLC":
                    direction = PlcDirection.Read;
                    return true;
                case "写PLC":
                    direction = PlcDirection.Write;
                    return true;
                case "读写":
                    direction = PlcDirection.ReadWrite;
                    return true;
                default:
                    error = $"读写方向不支持:{text}";
                    return false;
            }
        }

        private static bool TryValidateQuantity(PlcDataType dataType, int quantity, out string error)
        {
            error = null;
            if (quantity <= 0)
            {
                error = "数据数量必须大于0";
                return false;
            }
            if (dataType != PlcDataType.String && quantity != 1)
            {
                error = "非字符串类型仅支持数量=1";
                return false;
            }
            if (dataType == PlcDataType.Boolean && quantity != 1)
            {
                error = "布尔类型仅支持数量=1";
                return false;
            }
            return true;
        }

        private static bool TryParseAddress(string protocol, string addressText, PlcDataType dataType, int quantity, out PlcAddress address, out string error)
        {
            address = null;
            error = null;
            if (string.IsNullOrWhiteSpace(protocol))
            {
                error = "PLC协议为空";
                return false;
            }
            if (string.IsNullOrWhiteSpace(addressText))
            {
                error = "PLC地址为空";
                return false;
            }
            if (string.Equals(protocol, "S7", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseS7Address(addressText.Trim(), dataType, out address, out error);
            }
            if (string.Equals(protocol, "ModbusTcp", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseModbusAddress(addressText.Trim(), dataType, quantity, out address, out error);
            }
            error = $"PLC协议不支持:{protocol}";
            return false;
        }

        private static bool TryParseS7Address(string text, PlcDataType dataType, out PlcAddress address, out string error)
        {
            address = null;
            error = null;
            string upper = text.ToUpperInvariant();
            if (dataType == PlcDataType.Boolean)
            {
                if (TryParseS7Bit(upper, out S7Address s7Bit, out error))
                {
                    address = s7Bit;
                    return true;
                }
                return false;
            }

            if (TryParseS7ByteWordDword(upper, out S7Address s7Address, out char width, out error))
            {
                if (dataType == PlcDataType.Byte && width != 'B')
                {
                    error = "S7字节类型必须使用B地址";
                    return false;
                }
                if ((dataType == PlcDataType.UShort || dataType == PlcDataType.Short) && width != 'W')
                {
                    error = "S7字类型必须使用W地址";
                    return false;
                }
                if ((dataType == PlcDataType.UInt || dataType == PlcDataType.Int || dataType == PlcDataType.Float || dataType == PlcDataType.Double) && width != 'D')
                {
                    error = "S7双字类型必须使用D地址";
                    return false;
                }
                if (dataType == PlcDataType.String && width != 'B')
                {
                    error = "S7字符串必须使用B地址";
                    return false;
                }
                address = s7Address;
                return true;
            }

            return false;
        }

        private static bool TryParseS7Bit(string text, out S7Address address, out string error)
        {
            address = null;
            error = "S7位地址格式无效";
            if (text.StartsWith("DB", StringComparison.Ordinal))
            {
                int dotIndex = text.IndexOf('.');
                int dbNumber;
                if (dotIndex < 2 || !int.TryParse(text.Substring(2, dotIndex - 2), out dbNumber))
                {
                    return false;
                }
                string remain = text.Substring(dotIndex + 1);
                if (!remain.StartsWith("DBX", StringComparison.Ordinal))
                {
                    return false;
                }
                string bitPart = remain.Substring(3);
                string[] parts = bitPart.Split('.');
                if (parts.Length != 2)
                {
                    return false;
                }
                if (!int.TryParse(parts[0], out int byteIndex))
                {
                    return false;
                }
                if (!int.TryParse(parts[1], out int bitIndex) || bitIndex < 0 || bitIndex > 7)
                {
                    return false;
                }
                address = new S7Address
                {
                    Protocol = "S7",
                    Area = DataType.DataBlock,
                    DbNumber = dbNumber,
                    StartByte = byteIndex,
                    BitIndex = bitIndex,
                    HasBit = true
                };
                return true;
            }

            char area = text.Length > 0 ? text[0] : '\0';
            if (area != 'I' && area != 'Q' && area != 'M')
            {
                return false;
            }
            string[] parts2 = text.Substring(1).Split('.');
            if (parts2.Length != 2)
            {
                return false;
            }
            if (!int.TryParse(parts2[0], out int byteIndex2))
            {
                return false;
            }
            if (!int.TryParse(parts2[1], out int bitIndex2) || bitIndex2 < 0 || bitIndex2 > 7)
            {
                return false;
            }
            address = new S7Address
            {
                Protocol = "S7",
                Area = area == 'I' ? DataType.Input : area == 'Q' ? DataType.Output : DataType.Memory,
                DbNumber = 0,
                StartByte = byteIndex2,
                BitIndex = bitIndex2,
                HasBit = true
            };
            return true;
        }

        private static bool TryParseS7ByteWordDword(string text, out S7Address address, out char width, out string error)
        {
            address = null;
            width = '\0';
            error = "S7字节/字/双字地址格式无效";
            if (text.StartsWith("DB", StringComparison.Ordinal))
            {
                int dotIndex = text.IndexOf('.');
                if (dotIndex < 2)
                {
                    return false;
                }
                if (!int.TryParse(text.Substring(2, dotIndex - 2), out int dbNumber))
                {
                    return false;
                }
                string remain = text.Substring(dotIndex + 1);
                if (remain.StartsWith("DBB", StringComparison.Ordinal))
                {
                    if (!int.TryParse(remain.Substring(3), out int byteIndex))
                    {
                        return false;
                    }
                    address = new S7Address
                    {
                        Protocol = "S7",
                        Area = DataType.DataBlock,
                        DbNumber = dbNumber,
                        StartByte = byteIndex,
                        BitIndex = 0,
                        HasBit = false
                    };
                    width = 'B';
                    return true;
                }
                if (remain.StartsWith("DBW", StringComparison.Ordinal))
                {
                    if (!int.TryParse(remain.Substring(3), out int byteIndex))
                    {
                        return false;
                    }
                    address = new S7Address
                    {
                        Protocol = "S7",
                        Area = DataType.DataBlock,
                        DbNumber = dbNumber,
                        StartByte = byteIndex,
                        BitIndex = 0,
                        HasBit = false
                    };
                    width = 'W';
                    return true;
                }
                if (remain.StartsWith("DBD", StringComparison.Ordinal))
                {
                    if (!int.TryParse(remain.Substring(3), out int byteIndex))
                    {
                        return false;
                    }
                    address = new S7Address
                    {
                        Protocol = "S7",
                        Area = DataType.DataBlock,
                        DbNumber = dbNumber,
                        StartByte = byteIndex,
                        BitIndex = 0,
                        HasBit = false
                    };
                    width = 'D';
                    return true;
                }
                return false;
            }

            if (text.Length < 2)
            {
                return false;
            }
            char areaChar = text[0];
            if (areaChar != 'I' && areaChar != 'Q' && areaChar != 'M')
            {
                return false;
            }
            char typeChar = text[1];
            if (typeChar != 'B' && typeChar != 'W' && typeChar != 'D')
            {
                return false;
            }
            if (!int.TryParse(text.Substring(2), out int byteIndex2))
            {
                return false;
            }
            address = new S7Address
            {
                Protocol = "S7",
                Area = areaChar == 'I' ? DataType.Input : areaChar == 'Q' ? DataType.Output : DataType.Memory,
                DbNumber = 0,
                StartByte = byteIndex2,
                BitIndex = 0,
                HasBit = false
            };
            width = typeChar;
            return true;
        }

        private static bool TryParseModbusAddress(string text, PlcDataType dataType, int quantity, out PlcAddress address, out string error)
        {
            address = null;
            error = "Modbus地址格式无效";
            string[] parts = text.Split(':');
            if (parts.Length != 2)
            {
                return false;
            }
            string areaText = parts[0].Trim().ToUpperInvariant();
            string addrText = parts[1].Trim();
            if (!ushort.TryParse(addrText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort addr))
            {
                return false;
            }

            ModbusArea area;
            switch (areaText)
            {
                case "C":
                    area = ModbusArea.Coil;
                    break;
                case "DI":
                    area = ModbusArea.DiscreteInput;
                    break;
                case "HR":
                    area = ModbusArea.HoldingRegister;
                    break;
                case "IR":
                    area = ModbusArea.InputRegister;
                    break;
                default:
                    return false;
            }

            if (dataType == PlcDataType.Boolean)
            {
                if (area != ModbusArea.Coil && area != ModbusArea.DiscreteInput)
                {
                    error = "布尔类型仅支持线圈或离散输入";
                    return false;
                }
            }
            else
            {
                if (area != ModbusArea.HoldingRegister && area != ModbusArea.InputRegister)
                {
                    error = "数值类型仅支持寄存器区域";
                    return false;
                }
            }

            if (dataType == PlcDataType.String && quantity <= 0)
            {
                error = "字符串数量必须大于0";
                return false;
            }

            address = new ModbusAddress
            {
                Protocol = "ModbusTcp",
                Area = area,
                Address = addr
            };
            return true;
        }

        private static int GetByteCount(PlcDataType dataType, int quantity)
        {
            switch (dataType)
            {
                case PlcDataType.Boolean:
                    return 1;
                case PlcDataType.Byte:
                    return 1;
                case PlcDataType.UShort:
                case PlcDataType.Short:
                    return 2;
                case PlcDataType.UInt:
                case PlcDataType.Int:
                case PlcDataType.Float:
                    return 4;
                case PlcDataType.Double:
                    return 8;
                case PlcDataType.String:
                    return (quantity + 1) / 2;
                default:
                    throw new InvalidOperationException("数据类型不支持");
            }
        }

        private static int GetRegisterCount(PlcDataType dataType, int quantity)
        {
            switch (dataType)
            {
                case PlcDataType.Boolean:
                    return 1;
                case PlcDataType.Byte:
                case PlcDataType.UShort:
                case PlcDataType.Short:
                    return 1;
                case PlcDataType.UInt:
                case PlcDataType.Int:
                case PlcDataType.Float:
                    return 2;
                case PlcDataType.Double:
                    return 4;
                case PlcDataType.String:
                    return quantity;
                default:
                    throw new InvalidOperationException("数据类型不支持");
            }
        }

        private static object ConvertBytesToValue(byte[] bytes, PlcDataType dataType, int quantity, bool bigEndian)
        {
            if (dataType == PlcDataType.String)
            {
                string text = Encoding.ASCII.GetString(bytes ?? Array.Empty<byte>());
                return text.TrimEnd('\0');
            }
            if (bytes == null || bytes.Length == 0)
            {
                return 0d;
            }
            switch (dataType)
            {
                case PlcDataType.Boolean:
                    return bytes[0] != 0 ? 1d : 0d;
                case PlcDataType.Byte:
                    return (double)bytes[bytes.Length - 1];
                case PlcDataType.UShort:
                    return (double)ReadUInt16(bytes, bigEndian);
                case PlcDataType.Short:
                    return (double)ReadInt16(bytes, bigEndian);
                case PlcDataType.UInt:
                    return (double)ReadUInt32(bytes, bigEndian);
                case PlcDataType.Int:
                    return (double)ReadInt32(bytes, bigEndian);
                case PlcDataType.Float:
                    return (double)ReadSingle(bytes, bigEndian);
                case PlcDataType.Double:
                    return ReadDouble(bytes, bigEndian);
                default:
                    throw new InvalidOperationException("数据类型不支持");
            }
        }

        private static byte[] ConvertValueToBytes(object value, PlcDataType dataType, int quantity, bool bigEndian)
        {
            if (dataType == PlcDataType.String)
            {
                string text = value == null ? string.Empty : value.ToString();
                byte[] bytes = Encoding.ASCII.GetBytes(text ?? string.Empty);
                byte[] buffer = new byte[quantity];
                int copyLength = Math.Min(buffer.Length, bytes.Length);
                Array.Copy(bytes, buffer, copyLength);
                return buffer;
            }

            double number = ConvertToNumber(value);
            byte[] bytesNumber;
            switch (dataType)
            {
                case PlcDataType.Boolean:
                    return new[] { (byte)(ConvertToBool(value) ? 1 : 0) };
                case PlcDataType.Byte:
                    bytesNumber = new[] { (byte)Clamp(number, byte.MinValue, byte.MaxValue) };
                    break;
                case PlcDataType.UShort:
                    bytesNumber = GetBytes((ushort)Clamp(number, ushort.MinValue, ushort.MaxValue));
                    break;
                case PlcDataType.Short:
                    bytesNumber = GetBytes((short)Clamp(number, short.MinValue, short.MaxValue));
                    break;
                case PlcDataType.UInt:
                    bytesNumber = GetBytes((uint)Clamp(number, uint.MinValue, uint.MaxValue));
                    break;
                case PlcDataType.Int:
                    bytesNumber = GetBytes((int)Clamp(number, int.MinValue, int.MaxValue));
                    break;
                case PlcDataType.Float:
                    bytesNumber = GetBytes((float)number);
                    break;
                case PlcDataType.Double:
                    bytesNumber = GetBytes(number);
                    break;
                default:
                    throw new InvalidOperationException("数据类型不支持");
            }
            return bytesNumber;
        }

        private static byte[] BuildStringBytes(object value, int byteLength)
        {
            string text = value == null ? string.Empty : value.ToString();
            byte[] bytes = Encoding.ASCII.GetBytes(text ?? string.Empty);
            byte[] buffer = new byte[byteLength];
            int copyLength = Math.Min(buffer.Length, bytes.Length);
            Array.Copy(bytes, buffer, copyLength);
            return buffer;
        }

        private static byte[] RegistersToBytes(ushort[] registers)
        {
            if (registers == null || registers.Length == 0)
            {
                return Array.Empty<byte>();
            }
            byte[] bytes = new byte[registers.Length * 2];
            for (int i = 0; i < registers.Length; i++)
            {
                bytes[i * 2] = (byte)((registers[i] >> 8) & 0xFF);
                bytes[i * 2 + 1] = (byte)(registers[i] & 0xFF);
            }
            return bytes;
        }

        private static ushort ReadUInt16(byte[] bytes, bool bigEndian)
        {
            if (bytes.Length < 2)
            {
                return 0;
            }
            if (bigEndian)
            {
                return (ushort)((bytes[0] << 8) | bytes[1]);
            }
            return (ushort)((bytes[1] << 8) | bytes[0]);
        }

        private static short ReadInt16(byte[] bytes, bool bigEndian)
        {
            return unchecked((short)ReadUInt16(bytes, bigEndian));
        }

        private static uint ReadUInt32(byte[] bytes, bool bigEndian)
        {
            if (bytes.Length < 4)
            {
                return 0;
            }
            if (bigEndian)
            {
                return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
            }
            return (uint)((bytes[3] << 24) | (bytes[2] << 16) | (bytes[1] << 8) | bytes[0]);
        }

        private static int ReadInt32(byte[] bytes, bool bigEndian)
        {
            return unchecked((int)ReadUInt32(bytes, bigEndian));
        }

        private static float ReadSingle(byte[] bytes, bool bigEndian)
        {
            byte[] buffer = new byte[4];
            if (bytes.Length >= 4)
            {
                Array.Copy(bytes, buffer, 4);
            }
            if (bigEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToSingle(buffer, 0);
        }

        private static double ReadDouble(byte[] bytes, bool bigEndian)
        {
            byte[] buffer = new byte[8];
            if (bytes.Length >= 8)
            {
                Array.Copy(bytes, buffer, 8);
            }
            if (bigEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToDouble(buffer, 0);
        }

        private static byte[] GetBytes(ushort value)
        {
            return new[] { (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF) };
        }

        private static byte[] GetBytes(short value)
        {
            return GetBytes(unchecked((ushort)value));
        }

        private static byte[] GetBytes(uint value)
        {
            return new[]
            {
                (byte)((value >> 24) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            };
        }

        private static byte[] GetBytes(int value)
        {
            return GetBytes(unchecked((uint)value));
        }

        private static byte[] GetBytes(float value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return buffer;
        }

        private static byte[] GetBytes(double value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return buffer;
        }

        private static double ConvertToNumber(object value)
        {
            if (value == null)
            {
                throw new InvalidOperationException("写入值为空");
            }
            if (value is double number)
            {
                return number;
            }
            if (value is float floatValue)
            {
                return floatValue;
            }
            if (value is int intValue)
            {
                return intValue;
            }
            if (value is long longValue)
            {
                return longValue;
            }
            if (value is string text)
            {
                if (double.TryParse(text, out double parsed))
                {
                    return parsed;
                }
                throw new InvalidOperationException("写入值不是有效数字");
            }
            if (value is bool boolValue)
            {
                return boolValue ? 1 : 0;
            }
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        private static bool ConvertToBool(object value)
        {
            if (value == null)
            {
                throw new InvalidOperationException("写入值为空");
            }
            if (value is bool boolValue)
            {
                return boolValue;
            }
            if (value is string text)
            {
                if (bool.TryParse(text, out bool result))
                {
                    return result;
                }
                if (text == "1")
                {
                    return true;
                }
                if (text == "0")
                {
                    return false;
                }
                throw new InvalidOperationException("写入值不是有效布尔" );
            }
            if (value is double number)
            {
                return Math.Abs(number) > double.Epsilon;
            }
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }
    }
}
