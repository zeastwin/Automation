using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Automation
{
    public class PlcConfigStore
    {
        private readonly PlcHub hub;
        private readonly object dataLock = new object();
        private readonly List<PlcDevice> devices = new List<PlcDevice>();
        private readonly List<PlcMapItem> maps = new List<PlcMapItem>();
        private readonly Dictionary<string, PlcDevice> deviceByName = new Dictionary<string, PlcDevice>(StringComparer.OrdinalIgnoreCase);

        public PlcConfigStore()
        {
            hub = new PlcHub();
        }

        public IReadOnlyList<PlcDevice> Devices
        {
            get
            {
                lock (dataLock)
                {
                    return devices.ToList();
                }
            }
        }

        public IReadOnlyList<PlcMapItem> Maps
        {
            get
            {
                lock (dataLock)
                {
                    return maps.ToList();
                }
            }
        }

        public bool Load(string configPath)
        {
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }

            bool loadedDevices = LoadDevices(configPath);
            bool loadedMaps = LoadMaps(configPath);
            return loadedDevices || loadedMaps;
        }

        public bool LoadDevices(string configPath)
        {
            string filePath = Path.Combine(configPath, "PlcDevice.json");
            if (!File.Exists(filePath))
            {
                ReplaceDevices(new List<PlcDevice>());
                SaveDevices(configPath);
                return false;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All,
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };
                List<PlcDevice> temp = JsonConvert.DeserializeObject<List<PlcDevice>>(json, settings) ?? new List<PlcDevice>();
                if (!ValidateDevices(temp, out string error))
                {
                    MessageBox.Show(error);
                    ReplaceDevices(new List<PlcDevice>());
                    SaveDevices(configPath);
                    return false;
                }
                ReplaceDevices(temp);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                ReplaceDevices(new List<PlcDevice>());
                SaveDevices(configPath);
                return false;
            }
        }

        public bool LoadMaps(string configPath)
        {
            string filePath = Path.Combine(configPath, "PlcMap.json");
            if (!File.Exists(filePath))
            {
                ReplaceMaps(new List<PlcMapItem>());
                SaveMaps(configPath);
                return false;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All,
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };
                List<PlcMapItem> temp = JsonConvert.DeserializeObject<List<PlcMapItem>>(json, settings) ?? new List<PlcMapItem>();
                if (!ValidateMaps(temp, out string error))
                {
                    MessageBox.Show(error);
                    ReplaceMaps(new List<PlcMapItem>());
                    SaveMaps(configPath);
                    return false;
                }
                ReplaceMaps(temp);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                ReplaceMaps(new List<PlcMapItem>());
                SaveMaps(configPath);
                return false;
            }
        }

        public bool SaveDevices(string configPath)
        {
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            string filePath = Path.Combine(configPath, "PlcDevice.json");
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            };
            List<PlcDevice> snapshot;
            lock (dataLock)
            {
                snapshot = devices.Select(device => CloneDevice(device)).ToList();
            }
            string output = JsonConvert.SerializeObject(snapshot, settings);
            File.WriteAllText(filePath, output);
            return true;
        }

        public bool SaveMaps(string configPath)
        {
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            string filePath = Path.Combine(configPath, "PlcMap.json");
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            };
            List<PlcMapItem> snapshot;
            lock (dataLock)
            {
                snapshot = maps.Select(map => CloneMap(map)).ToList();
            }
            string output = JsonConvert.SerializeObject(snapshot, settings);
            File.WriteAllText(filePath, output);
            return true;
        }

        public void ReplaceDevices(List<PlcDevice> newDevices)
        {
            lock (dataLock)
            {
                devices.Clear();
                deviceByName.Clear();
                if (newDevices == null)
                {
                    return;
                }
                foreach (PlcDevice device in newDevices)
                {
                    if (device == null)
                    {
                        continue;
                    }
                    devices.Add(device);
                    if (!string.IsNullOrWhiteSpace(device.Name))
                    {
                        deviceByName[device.Name] = device;
                    }
                }
            }
        }

        public void ReplaceMaps(List<PlcMapItem> newMaps)
        {
            lock (dataLock)
            {
                maps.Clear();
                if (newMaps == null)
                {
                    return;
                }
                foreach (PlcMapItem map in newMaps)
                {
                    if (map == null)
                    {
                        continue;
                    }
                    maps.Add(map);
                }
            }
        }

        public bool TryGetDevice(string name, out PlcDevice device)
        {
            device = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }
            lock (dataLock)
            {
                if (!deviceByName.TryGetValue(name, out PlcDevice found))
                {
                    return false;
                }
                device = CloneDevice(found);
                return true;
            }
        }

        public bool TryReconnect(PlcDevice device, out string error)
        {
            return hub.Reconnect(device, out error);
        }

        public void DisconnectAll()
        {
            hub.DisconnectAll();
        }

        public bool TryReadValue(PlcDevice device, PlcMapItem map, out object value, out string error)
        {
            return hub.TryReadValue(device, map, out value, out error);
        }

        public bool TryWriteValue(PlcDevice device, PlcMapItem map, object inputValue, out string error)
        {
            return hub.TryWriteValue(device, map, inputValue, out error);
        }

        public bool TryReadValue(string plcName, string dataType, string address, int quantity, out object value, out string error)
        {
            value = null;
            error = null;
            if (!TryGetDevice(plcName, out PlcDevice device))
            {
                error = $"PLC设备不存在:{plcName}";
                return false;
            }
            return hub.TryReadValue(device, dataType, address, quantity, out value, out error);
        }

        public bool TryWriteValue(string plcName, string dataType, string address, int quantity, object inputValue, out string error)
        {
            error = null;
            if (!TryGetDevice(plcName, out PlcDevice device))
            {
                error = $"PLC设备不存在:{plcName}";
                return false;
            }
            return hub.TryWriteValue(device, dataType, address, quantity, inputValue, out error);
        }

        public bool HasDevice(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }
            lock (dataLock)
            {
                return deviceByName.ContainsKey(name);
            }
        }

        private static PlcDevice CloneDevice(PlcDevice device)
        {
            if (device == null)
            {
                return null;
            }
            return new PlcDevice
            {
                Name = device.Name,
                Protocol = device.Protocol,
                CpuType = device.CpuType,
                Ip = device.Ip,
                Port = device.Port,
                Rack = device.Rack,
                Slot = device.Slot,
                TimeoutMs = device.TimeoutMs,
                UnitId = device.UnitId
            };
        }

        private static PlcMapItem CloneMap(PlcMapItem map)
        {
            if (map == null)
            {
                return null;
            }
            return new PlcMapItem
            {
                PlcName = map.PlcName,
                DataType = map.DataType,
                Direction = map.Direction,
                PlcAddress = map.PlcAddress,
                ValueName = map.ValueName,
                Quantity = map.Quantity,
                WriteConst = map.WriteConst
            };
        }

        public static bool ValidateDevices(List<PlcDevice> source, out string error)
        {
            error = null;
            if (source == null)
            {
                error = "PLC设备列表为空";
                return false;
            }

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlcDevice device in source)
            {
                if (device == null)
                {
                    error = "PLC设备项为空";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(device.Name))
                {
                    error = "PLC设备名称不能为空";
                    return false;
                }
                if (!names.Add(device.Name))
                {
                    error = $"PLC设备名称重复:{device.Name}";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(device.Protocol))
                {
                    error = $"PLC协议不能为空:{device.Name}";
                    return false;
                }
                if (!PlcConstants.Protocols.Contains(device.Protocol))
                {
                    error = $"PLC协议不支持:{device.Protocol}";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(device.Ip))
                {
                    error = $"PLC IP不能为空:{device.Name}";
                    return false;
                }
                if (!IPAddress.TryParse(device.Ip, out _))
                {
                    error = $"PLC IP格式无效:{device.Name}";
                    return false;
                }
                if (device.Port <= 0 || device.Port > 65535)
                {
                    error = $"PLC端口非法:{device.Name}";
                    return false;
                }
                if (device.TimeoutMs <= 0)
                {
                    error = $"PLC超时配置无效:{device.Name}";
                    return false;
                }
                if (string.Equals(device.Protocol, "S7", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(device.CpuType))
                    {
                        error = $"PLC CPU类型不能为空:{device.Name}";
                        return false;
                    }
                    if (!PlcConstants.CpuTypes.Contains(device.CpuType))
                    {
                        error = $"PLC CPU类型不支持:{device.CpuType}";
                        return false;
                    }
                    if (device.Rack < 0 || device.Slot < 0)
                    {
                        error = $"PLC机架或槽位无效:{device.Name}";
                        return false;
                    }
                }
                else
                {
                    if (device.UnitId < 0 || device.UnitId > 255)
                    {
                        error = $"PLC站号无效:{device.Name}";
                        return false;
                    }
                }
            }
            return true;
        }

        public static bool ValidateMaps(List<PlcMapItem> source, out string error)
        {
            error = null;
            if (source == null)
            {
                error = "PLC映射列表为空";
                return false;
            }

            for (int i = 0; i < source.Count; i++)
            {
                PlcMapItem map = source[i];
                if (map == null)
                {
                    error = $"PLC映射项为空: 行{i + 1}";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(map.PlcName))
                {
                    error = $"PLC名称不能为空: 行{i + 1}";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(map.DataType) || !PlcConstants.DataTypes.Contains(map.DataType))
                {
                    error = $"PLC数据类型无效: 行{i + 1}";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(map.Direction) || !PlcConstants.Directions.Contains(map.Direction))
                {
                    error = $"PLC读写方向无效: 行{i + 1}";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(map.PlcAddress))
                {
                    error = $"PLC地址不能为空: 行{i + 1}";
                    return false;
                }
                if (map.Quantity <= 0)
                {
                    error = $"PLC数据数量无效: 行{i + 1}";
                    return false;
                }
                bool needValueName = map.Direction == "读PLC" || map.Direction == "读写";
                if (needValueName && string.IsNullOrWhiteSpace(map.ValueName))
                {
                    error = $"PLC读操作变量不能为空: 行{i + 1}";
                    return false;
                }
                if (map.Direction == "写PLC" || map.Direction == "读写")
                {
                    if (string.IsNullOrWhiteSpace(map.WriteConst) && string.IsNullOrWhiteSpace(map.ValueName))
                    {
                        error = $"PLC写操作需配置变量或常量: 行{i + 1}";
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
