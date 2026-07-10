using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Automation
{
    public sealed class CommunicationConfigStore
    {
        private readonly object sync = new object();
        private List<SocketInfo> sockets = new List<SocketInfo>();
        private List<SerialPortInfo> serialPorts = new List<SerialPortInfo>();

        public IReadOnlyList<SocketInfo> GetSocketSnapshot()
        {
            lock (sync)
            {
                return sockets.Select(CloneSocket).ToList();
            }
        }

        public IReadOnlyList<SerialPortInfo> GetSerialSnapshot()
        {
            lock (sync)
            {
                return serialPorts.Select(CloneSerial).ToList();
            }
        }

        public bool TryGetSocket(string name, out SocketInfo info)
        {
            lock (sync)
            {
                SocketInfo found = sockets.FirstOrDefault(item => item != null
                    && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                info = found == null ? null : CloneSocket(found);
                return info != null;
            }
        }

        public bool TryGetSerial(string name, out SerialPortInfo info)
        {
            lock (sync)
            {
                SerialPortInfo found = serialPorts.FirstOrDefault(item => item != null
                    && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                info = found == null ? null : CloneSerial(found);
                return info != null;
            }
        }

        public bool Load(string configPath, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(configPath);
                string socketPath = Path.Combine(configPath, "SocketInfo.json");
                string serialPath = Path.Combine(configPath, "SerialPortInfo.json");
                bool socketMissing = !File.Exists(socketPath);
                bool serialMissing = !File.Exists(serialPath);
                List<SocketInfo> loadedSockets = ReadList<SocketInfo>(socketPath);
                List<SerialPortInfo> loadedSerial = ReadList<SerialPortInfo>(serialPath);
                if (!ValidateSockets(loadedSockets, out error) || !ValidateSerialPorts(loadedSerial, out error))
                {
                    return false;
                }
                lock (sync)
                {
                    sockets = loadedSockets.Select(CloneSocket).ToList();
                    serialPorts = loadedSerial.Select(CloneSerial).ToList();
                }
                if (socketMissing)
                {
                    WriteAtomic(socketPath, loadedSockets);
                }
                if (serialMissing)
                {
                    WriteAtomic(serialPath, loadedSerial);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"通讯配置加载失败：{ex.Message}";
                return false;
            }
        }

        public bool ReplaceSockets(IEnumerable<SocketInfo> source, out string error)
        {
            List<SocketInfo> candidate = source?.Select(CloneSocket).ToList() ?? new List<SocketInfo>();
            if (!ValidateSockets(candidate, out error))
            {
                return false;
            }
            lock (sync)
            {
                sockets = candidate;
            }
            return true;
        }

        public bool ReplaceSerialPorts(IEnumerable<SerialPortInfo> source, out string error)
        {
            List<SerialPortInfo> candidate = source?.Select(CloneSerial).ToList() ?? new List<SerialPortInfo>();
            if (!ValidateSerialPorts(candidate, out error))
            {
                return false;
            }
            lock (sync)
            {
                serialPorts = candidate;
            }
            return true;
        }

        public bool TryReplaceSocketsAndSave(IEnumerable<SocketInfo> source, string configPath, out string error)
        {
            List<SocketInfo> candidate = source?.Select(CloneSocket).ToList() ?? new List<SocketInfo>();
            if (!ValidateSockets(candidate, out error))
            {
                return false;
            }
            try
            {
                Directory.CreateDirectory(configPath);
                WriteAtomic(Path.Combine(configPath, "SocketInfo.json"), candidate);
                lock (sync)
                {
                    sockets = candidate;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"TCP配置保存失败：{ex.Message}";
                return false;
            }
        }

        public bool TryReplaceSerialPortsAndSave(IEnumerable<SerialPortInfo> source, string configPath, out string error)
        {
            List<SerialPortInfo> candidate = source?.Select(CloneSerial).ToList() ?? new List<SerialPortInfo>();
            if (!ValidateSerialPorts(candidate, out error))
            {
                return false;
            }
            try
            {
                Directory.CreateDirectory(configPath);
                WriteAtomic(Path.Combine(configPath, "SerialPortInfo.json"), candidate);
                lock (sync)
                {
                    serialPorts = candidate;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"串口配置保存失败：{ex.Message}";
                return false;
            }
        }

        public void Save(string configPath)
        {
            List<SocketInfo> socketSnapshot;
            List<SerialPortInfo> serialSnapshot;
            lock (sync)
            {
                socketSnapshot = sockets.Select(CloneSocket).ToList();
                serialSnapshot = serialPorts.Select(CloneSerial).ToList();
            }
            Directory.CreateDirectory(configPath);
            WriteAtomic(Path.Combine(configPath, "SocketInfo.json"), socketSnapshot);
            WriteAtomic(Path.Combine(configPath, "SerialPortInfo.json"), serialSnapshot);
        }

        private static List<T> ReadList<T>(string path)
        {
            if (!File.Exists(path))
            {
                return new List<T>();
            }
            string json = File.ReadAllText(path);
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                ObjectCreationHandling = ObjectCreationHandling.Replace
            };
            return JsonConvert.DeserializeObject<List<T>>(json, settings)
                ?? throw new InvalidDataException($"配置内容为空：{Path.GetFileName(path)}");
        }

        private static void WriteAtomic<T>(string path, List<T> value)
        {
            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };
            string json = JsonConvert.SerializeObject(value, settings);
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }

        private static bool ValidateSockets(List<SocketInfo> source, out string error)
        {
            error = null;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<int>();
            var serverEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SocketInfo item in source)
            {
                if (!CommunicationHub.TryValidateSocketInfo(item, out error))
                {
                    return false;
                }
                if (!names.Add(item.Name))
                {
                    error = $"TCP名称重复：{item.Name}";
                    return false;
                }
                if (item.ID <= 0 || !ids.Add(item.ID))
                {
                    error = $"TCP编号无效或重复：{item.ID}";
                    return false;
                }
                if (string.Equals(item.Type, "Server", StringComparison.Ordinal))
                {
                    string endpoint = $"{item.Address}:{item.Port}";
                    if (!serverEndpoints.Add(endpoint))
                    {
                        error = $"TCP服务端监听地址重复：{endpoint}";
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool ValidateSerialPorts(List<SerialPortInfo> source, out string error)
        {
            error = null;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<int>();
            var ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SerialPortInfo item in source)
            {
                if (!CommunicationHub.TryValidateSerialPortInfo(item, out error))
                {
                    return false;
                }
                if (!names.Add(item.Name))
                {
                    error = $"串口名称重复：{item.Name}";
                    return false;
                }
                if (item.ID <= 0 || !ids.Add(item.ID))
                {
                    error = $"串口编号无效或重复：{item.ID}";
                    return false;
                }
                if (!ports.Add(item.Port))
                {
                    error = $"串口号重复：{item.Port}";
                    return false;
                }
            }
            return true;
        }

        private static SocketInfo CloneSocket(SocketInfo item)
        {
            if (item == null) return null;
            return new SocketInfo
            {
                ID = item.ID,
                Name = item.Name,
                Type = item.Type,
                Port = item.Port,
                Address = item.Address,
                FrameMode = item.FrameMode,
                FrameDelimiter = item.FrameDelimiter,
                EncodingName = item.EncodingName,
                ConnectTimeoutMs = item.ConnectTimeoutMs
            };
        }

        private static SerialPortInfo CloneSerial(SerialPortInfo item)
        {
            if (item == null) return null;
            return new SerialPortInfo
            {
                ID = item.ID,
                Name = item.Name,
                Port = item.Port,
                BitRate = item.BitRate,
                CheckBit = item.CheckBit,
                DataBit = item.DataBit,
                StopBit = item.StopBit,
                FrameMode = item.FrameMode,
                FrameDelimiter = item.FrameDelimiter,
                EncodingName = item.EncodingName
            };
        }
    }
}
