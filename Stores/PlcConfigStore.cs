using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Automation
{
    public sealed class PlcConfigStore
    {
        public const string ConfigFileName = "PlcConfig.json";
        private readonly object syncRoot = new object();
        private PlcConfiguration configuration = new PlcConfiguration();

        public bool Faulted { get; private set; }
        public string FaultReason { get; private set; } = string.Empty;

        public PlcConfiguration GetSnapshot()
        {
            lock (syncRoot)
            {
                return PlcModelClone.Clone(configuration);
            }
        }

        public bool Load(string configRoot, ValueConfigStore valueStore, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(configRoot);
                string path = Path.Combine(configRoot, ConfigFileName);
                if (!File.Exists(path))
                {
                    var empty = new PlcConfiguration();
                    if (!Save(configRoot, empty, valueStore, out error))
                    {
                        SetFault(error);
                        return false;
                    }
                    ClearFault();
                    return true;
                }

                JsonSerializerSettings settings = CreateJsonSettings();
                PlcConfiguration loaded = JsonConvert.DeserializeObject<PlcConfiguration>(
                    File.ReadAllText(path), settings);
                if (!Validate(loaded, valueStore, out error))
                {
                    SetFault(error);
                    return false;
                }
                lock (syncRoot)
                {
                    configuration = PlcModelClone.Clone(loaded);
                }
                ClearFault();
                return true;
            }
            catch (Exception ex)
            {
                error = $"PLC配置加载失败:{ex.Message}";
                SetFault(error);
                return false;
            }
        }

        public bool Save(string configRoot, PlcConfiguration candidate, ValueConfigStore valueStore, out string error)
        {
            error = null;
            if (!Validate(candidate, valueStore, out error))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(configRoot);
                string path = Path.Combine(configRoot, ConfigFileName);
                string json = JsonConvert.SerializeObject(candidate, Formatting.Indented, CreateJsonSettings());
                WriteAtomic(path, json);
                lock (syncRoot)
                {
                    configuration = PlcModelClone.Clone(candidate);
                }
                ClearFault();
                return true;
            }
            catch (Exception ex)
            {
                error = $"PLC配置保存失败:{ex.Message}";
                return false;
            }
        }

        public static bool Validate(PlcConfiguration candidate, ValueConfigStore valueStore, out string error)
        {
            error = null;
            if (candidate == null)
            {
                error = "PLC配置为空。";
                return false;
            }
            if (candidate.SchemaVersion != 1)
            {
                error = $"PLC配置版本无效:{candidate.SchemaVersion}";
                return false;
            }
            if (candidate.Devices == null)
            {
                error = "PLC设备列表为空引用。";
                return false;
            }

            var deviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var localWriters = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int deviceIndex = 0; deviceIndex < candidate.Devices.Count; deviceIndex++)
            {
                PlcDeviceConfig device = candidate.Devices[deviceIndex];
                string prefix = $"PLC设备第{deviceIndex + 1}项";
                if (!ValidateDevice(device, prefix, deviceNames, valueStore, out error)) return false;

                var plcWriteRanges = new List<Tuple<int, int, PlcArea, string>>();
                for (int mapIndex = 0; mapIndex < device.Mappings.Count; mapIndex++)
                {
                    PlcMapConfig map = device.Mappings[mapIndex];
                    string mapPrefix = $"{prefix}映射第{mapIndex + 1}项";
                    if (!ValidateMap(map, mapPrefix, valueStore, out error)) return false;

                    if (map.Direction == PlcMapDirection.ReadFromPlc
                        || map.Direction == PlcMapDirection.Bidirectional)
                    {
                        foreach (string variableName in map.VariableNames)
                        {
                            if (localWriters.TryGetValue(variableName, out string existing))
                            {
                                error = $"变量[{variableName}]存在多个PLC写入来源:{existing}、{device.Name}/{map.Name}";
                                return false;
                            }
                            localWriters.Add(variableName, $"{device.Name}/{map.Name}");
                        }
                    }

                    if (map.Direction == PlcMapDirection.WriteToPlc
                        || map.Direction == PlcMapDirection.Bidirectional)
                    {
                        int span = GetAddressSpan(map);
                        int end = checked(map.StartAddress + span - 1);
                        foreach (Tuple<int, int, PlcArea, string> range in plcWriteRanges)
                        {
                            if (range.Item3 == map.Area && map.StartAddress <= range.Item2 && end >= range.Item1)
                            {
                                error = $"设备[{device.Name}]映射[{map.Name}]与[{range.Item4}]写入地址重叠。";
                                return false;
                            }
                        }
                        plcWriteRanges.Add(Tuple.Create(map.StartAddress, end, map.Area, map.Name));
                    }
                }
            }
            return true;
        }

        private static bool ValidateDevice(PlcDeviceConfig device, string prefix, HashSet<string> names,
            ValueConfigStore valueStore, out string error)
        {
            error = null;
            if (device == null) { error = prefix + "为空。"; return false; }
            if (string.IsNullOrWhiteSpace(device.Name)) { error = prefix + "名称为空。"; return false; }
            if (!names.Add(device.Name)) { error = $"PLC名称重复:{device.Name}"; return false; }
            if (!Enum.IsDefined(typeof(PlcDeviceProfile), device.Profile)) { error = prefix + "类型无效。"; return false; }
            if (!IPAddress.TryParse(device.IpAddress, out IPAddress ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            { error = prefix + "IPv4地址无效。"; return false; }
            if (device.Port < 1 || device.Port > 65535) { error = prefix + "端口超出范围。"; return false; }
            if (device.UnitId < 0 || device.UnitId > 255) { error = prefix + "站号超出范围。"; return false; }
            if (device.ConnectTimeoutMs < 100 || device.ConnectTimeoutMs > 60000) { error = prefix + "连接超时必须为100..60000ms。"; return false; }
            if (device.ScanIntervalMs < 50 || device.ScanIntervalMs > 60000) { error = prefix + "扫描周期必须为50..60000ms。"; return false; }
            if (!new[] { "ABCD", "BADC", "CDAB", "DCBA" }.Contains(device.DataFormat, StringComparer.Ordinal))
            { error = prefix + "字节序无效。"; return false; }
            if (device.Mappings == null) { error = prefix + "映射列表为空引用。"; return false; }
            if (!string.IsNullOrWhiteSpace(device.StatusVariableName))
            {
                if (valueStore == null || !valueStore.TryGetValueByName(device.StatusVariableName, out DicValue status)
                    || !string.Equals(status?.Type, "double", StringComparison.OrdinalIgnoreCase))
                { error = $"{prefix}状态变量必须是已存在的double变量:{device.StatusVariableName}"; return false; }
            }
            var mapIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlcMapConfig map in device.Mappings)
            {
                if (map != null && !string.IsNullOrWhiteSpace(map.Id) && !mapIds.Add(map.Id))
                { error = $"{prefix}映射ID重复:{map.Id}"; return false; }
            }
            return true;
        }

        private static bool ValidateMap(PlcMapConfig map, string prefix, ValueConfigStore valueStore, out string error)
        {
            error = null;
            if (map == null) { error = prefix + "为空。"; return false; }
            if (string.IsNullOrWhiteSpace(map.Id) || !Guid.TryParseExact(map.Id, "N", out _)) { error = prefix + "ID无效。"; return false; }
            if (string.IsNullOrWhiteSpace(map.Name)) { error = prefix + "名称为空。"; return false; }
            if (!Enum.IsDefined(typeof(PlcArea), map.Area)
                || !Enum.IsDefined(typeof(PlcDataType), map.DataType)
                || !Enum.IsDefined(typeof(PlcMapDirection), map.Direction)
                || !Enum.IsDefined(typeof(PlcMapPriority), map.Priority))
            { error = prefix + "枚举字段无效。"; return false; }
            if (map.StartAddress < 0 || map.StartAddress > 65535) { error = prefix + "起始地址超出范围。"; return false; }
            if (map.ElementCount < 1 || map.ElementCount > 1000) { error = prefix + "元素数量必须为1..1000。"; return false; }
            if (double.IsNaN(map.ChangeTolerance) || double.IsInfinity(map.ChangeTolerance) || map.ChangeTolerance < 0)
            { error = prefix + "变化容差必须是非负有限数。"; return false; }
            if (map.ChangeTolerance != 0d && map.DataType != PlcDataType.Float && map.DataType != PlcDataType.Double)
            { error = prefix + "只有Float和Double允许配置变化容差。"; return false; }
            if ((map.Area == PlcArea.DiscreteInput || map.Area == PlcArea.InputRegister)
                && map.Direction != PlcMapDirection.ReadFromPlc)
            { error = prefix + "只读地址区禁止写入或双向映射。"; return false; }
            if ((map.Area == PlcArea.Coil || map.Area == PlcArea.DiscreteInput) && map.DataType != PlcDataType.Boolean)
            { error = prefix + "线圈地址区只允许Boolean。"; return false; }
            if ((map.Area == PlcArea.HoldingRegister || map.Area == PlcArea.InputRegister) && map.DataType == PlcDataType.Boolean)
            { error = prefix + "Boolean只允许映射到线圈地址区。"; return false; }
            if (map.DataType == PlcDataType.String)
            {
                if (map.ElementCount != 1 || map.StringByteLength < 1 || map.StringByteLength > 2000)
                { error = prefix + "字符串必须为单元素且字节长度为1..2000。"; return false; }
            }
            else if (map.StringByteLength != 0)
            { error = prefix + "非字符串映射的字符串字节长度必须为0。"; return false; }
            if (map.VariableNames == null || map.VariableNames.Count != map.ElementCount)
            { error = prefix + "变量数量必须等于元素数量。"; return false; }

            var uniqueVariables = new HashSet<string>(StringComparer.Ordinal);
            foreach (string variableName in map.VariableNames)
            {
                if (string.IsNullOrWhiteSpace(variableName) || !uniqueVariables.Add(variableName))
                { error = prefix + "变量名称为空或重复。"; return false; }
                if (valueStore == null || !valueStore.TryGetValueByName(variableName, out DicValue value))
                { error = $"{prefix}变量不存在:{variableName}"; return false; }
                string expected = map.DataType == PlcDataType.String ? "string" : "double";
                if (!string.Equals(value?.Type, expected, StringComparison.OrdinalIgnoreCase))
                { error = $"{prefix}变量[{variableName}]必须是{expected}类型。"; return false; }
            }
            int span = GetAddressSpan(map);
            if ((long)map.StartAddress + span - 1 > 65535)
            { error = prefix + "映射范围超过65535。"; return false; }
            return true;
        }

        public static int GetAddressSpan(PlcMapConfig map)
        {
            if (map.Area == PlcArea.Coil || map.Area == PlcArea.DiscreteInput) return map.ElementCount;
            if (map.DataType == PlcDataType.String) return (map.StringByteLength + 1) / 2;
            int registersPerElement;
            switch (map.DataType)
            {
                case PlcDataType.Byte: return (map.ElementCount + 1) / 2;
                case PlcDataType.UShort:
                case PlcDataType.Short: registersPerElement = 1; break;
                case PlcDataType.UInt:
                case PlcDataType.Int:
                case PlcDataType.Float: registersPerElement = 2; break;
                case PlcDataType.Double: registersPerElement = 4; break;
                default: throw new InvalidOperationException($"不支持的数据类型:{map.DataType}");
            }
            return checked(registersPerElement * map.ElementCount);
        }

        private static JsonSerializerSettings CreateJsonSettings()
        {
            var settings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                NullValueHandling = NullValueHandling.Include,
                TypeNameHandling = TypeNameHandling.None,
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };
            settings.Converters.Add(new StringEnumConverter());
            return settings;
        }

        private static void WriteAtomic(string path, string content)
        {
            string temp = path + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, content);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private void SetFault(string reason)
        {
            lock (syncRoot)
            {
                configuration = new PlcConfiguration();
                Faulted = true;
                FaultReason = reason ?? "PLC配置异常。";
            }
        }

        private void ClearFault()
        {
            Faulted = false;
            FaultReason = string.Empty;
        }
    }
}
