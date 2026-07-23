using System;
// 模块：持久化 / 设备配置。
// 职责范围：管理控制卡、通讯、PLC、IO、工站和点位配置，不执行设备动作。

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

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
            string directory = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            return AtomicJsonFileStore.Read<List<T>>(directory, name)
                ?? throw new InvalidDataException($"主配置及备份均无法读取：{Path.GetFileName(path)}");
        }

        private static void WriteAtomic<T>(string path, List<T> value)
        {
            string directory = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            if (!AtomicJsonFileStore.Save(directory, name, value))
            {
                throw new IOException($"配置耐久化失败：{Path.GetFileName(path)}");
            }
        }

        private static bool ValidateSockets(List<SocketInfo> source, out string error)
        {
            error = null;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<int>();
            var serverCatchAllEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fixedServerSelectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var serverListenerEndpoints = new List<IPEndPoint>();
            var clientLocalEndpoints = new List<IPEndPoint>();
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
                    var localEndpoint = new IPEndPoint(IPAddress.Parse(item.LocalAddress), item.LocalPort);
                    IPEndPoint sameEndpoint = serverListenerEndpoints.FirstOrDefault(existing =>
                        existing.Port == localEndpoint.Port && Equals(existing.Address, localEndpoint.Address));
                    if (sameEndpoint == null)
                    {
                        IPEndPoint overlapping = serverListenerEndpoints.FirstOrDefault(existing =>
                            existing.Port == localEndpoint.Port
                            && (Equals(existing.Address, IPAddress.Any) || Equals(localEndpoint.Address, IPAddress.Any)));
                        if (overlapping != null)
                        {
                            error = $"TCP服务端监听地址重叠：{overlapping} 与 {localEndpoint}";
                            return false;
                        }
                        serverListenerEndpoints.Add(localEndpoint);
                    }

                    string endpoint = localEndpoint.ToString();
                    string remoteAddress = string.Equals(item.RemoteAddress, "*", StringComparison.Ordinal)
                        ? "*"
                        : IPAddress.Parse(item.RemoteAddress).ToString();
                    bool catchAll = string.Equals(item.RemoteAddress, "*", StringComparison.Ordinal)
                        && item.RemotePort == 0;
                    if (catchAll && !serverCatchAllEndpoints.Add(endpoint))
                    {
                        error = $"TCP服务端监听地址只能配置一个未匹配客户端接收通道：{endpoint}";
                        return false;
                    }
                    if (!catchAll && item.RemotePort > 0)
                    {
                        string selector = $"{endpoint}|{remoteAddress}:{item.RemotePort}";
                        if (!fixedServerSelectors.Add(selector))
                        {
                            error = $"TCP服务端远端选择条件重复：{remoteAddress}:{item.RemotePort}";
                            return false;
                        }
                    }
                    continue;
                }

                if (item.LocalPort > 0)
                {
                    var localEndpoint = new IPEndPoint(IPAddress.Parse(item.LocalAddress), item.LocalPort);
                    IPEndPoint conflicting = clientLocalEndpoints.FirstOrDefault(existing =>
                        existing.Port == localEndpoint.Port
                        && (Equals(existing.Address, localEndpoint.Address)
                            || Equals(existing.Address, IPAddress.Any)
                            || Equals(localEndpoint.Address, IPAddress.Any)));
                    if (conflicting != null)
                    {
                        error = $"TCP客户端本地绑定地址冲突：{conflicting} 与 {localEndpoint}";
                        return false;
                    }
                    clientLocalEndpoints.Add(localEndpoint);
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
                LocalAddress = item.LocalAddress,
                LocalPort = item.LocalPort,
                RemoteAddress = item.RemoteAddress,
                RemotePort = item.RemotePort,
                AutoReconnect = string.Equals(item.Type, "Client", StringComparison.Ordinal) && item.AutoReconnect,
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
                EncodingName = item.EncodingName
            };
        }
    }
}
