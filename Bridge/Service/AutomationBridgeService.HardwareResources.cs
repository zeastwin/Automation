using Newtonsoft.Json;
// 模块：Bridge / 服务。
// 职责范围：实现 Named Pipe 请求的路由、投影、诊断、预演和事务提交。

using Newtonsoft.Json.Linq;
using Automation.Protocol;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using static System.ComponentModel.TypeConverter;

namespace Automation.Bridge
{
    internal sealed partial class AutomationBridgeService
    {
        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListPlcDevices(JObject request)
        {
            EnsureRuntimeReady();
            PlcConfigStore store = runtime.Stores.Plc;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "PLC 存储未初始化。");
            }
            bool includeMaps = request["includeMaps"]?.Value<bool>() ?? false;
            string exactName = ReadOptionalString(request, "name");
            PlcConfiguration configuration = store.GetSnapshot();
            var devices = configuration.Devices;
            IReadOnlyDictionary<string, PlcDeviceRuntimeSnapshot> runtimeByName =
                (runtime.PlcRuntime?.GetSnapshots() ?? new List<PlcDeviceRuntimeSnapshot>())
                .ToDictionary(item => item.DeviceName, StringComparer.OrdinalIgnoreCase);
            var items = new List<JObject>();
            foreach (PlcDeviceConfig dev in devices)
            {
                if (dev == null) continue;
                if (!string.IsNullOrWhiteSpace(exactName)
                    && !string.Equals(dev.Name, exactName, StringComparison.Ordinal)) continue;
                runtimeByName.TryGetValue(dev.Name ?? string.Empty, out PlcDeviceRuntimeSnapshot runtime);
                JObject obj = new JObject
                {
                    ["name"] = dev.Name ?? string.Empty,
                    ["protocol"] = "ModbusTcp",
                    ["profile"] = dev.Profile.ToString(),
                    ["ip"] = dev.IpAddress ?? string.Empty,
                    ["port"] = dev.Port,
                    ["unitId"] = dev.UnitId,
                    ["connectTimeoutMs"] = dev.ConnectTimeoutMs,
                    ["autoConnect"] = dev.AutoConnect,
                    ["scanIntervalMs"] = dev.ScanIntervalMs,
                    ["dataFormat"] = dev.DataFormat,
                    ["isStringReverse"] = dev.IsStringReverse,
                    ["addressStartWithZero"] = dev.AddressStartWithZero,
                    ["statusVariableName"] = dev.StatusVariableName ?? string.Empty,
                    ["runtimeState"] = runtime?.State.ToString() ?? PlcRuntimeState.Uninitialized.ToString(),
                    ["lastError"] = runtime?.LastError ?? string.Empty
                };
                if (includeMaps)
                {
                    var deviceMaps = new JArray();
                    foreach (PlcMapConfig map in dev.Mappings)
                    {
                        if (map == null) continue;
                        deviceMaps.Add(new JObject
                        {
                            ["id"] = map.Id ?? string.Empty,
                            ["name"] = map.Name ?? string.Empty,
                            ["enabled"] = map.Enabled,
                            ["area"] = map.Area.ToString(),
                            ["startAddress"] = map.StartAddress,
                            ["dataType"] = map.DataType.ToString(),
                            ["direction"] = map.Direction.ToString(),
                            ["priority"] = map.Priority.ToString(),
                            ["elementCount"] = map.ElementCount,
                            ["stringByteLength"] = map.StringByteLength,
                            ["variableNames"] = new JArray(map.VariableNames ?? new List<string>()),
                            ["changeTolerance"] = map.ChangeTolerance
                        });
                    }
                    obj["maps"] = deviceMaps;
                }
                items.Add(obj);
            }
            if (!string.IsNullOrWhiteSpace(exactName) && items.Count == 0)
            {
                throw new BridgeRequestException(404, "PLC_DEVICE_NOT_FOUND", $"未找到PLC设备：{exactName}");
            }
            return new JObject
            {
                ["total"] = items.Count,
                ["items"] = new JArray(items)
            };
        }

        // ===================== 控制卡/轴清单 =====================

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListCards(JObject request)
        {
            EnsureRuntimeReady();
            CardConfigStore store = runtime.Stores.Cards;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "控制卡存储未初始化。");
            }
            bool includeAxes = request["includeAxes"]?.Value<bool>() ?? true;
            var items = new List<JObject>();
            int cardCount = store.GetControlCardCount();
            for (int ci = 0; ci < cardCount; ci++)
            {
                if (!store.TryGetControlCard(ci, out ControlCard card) || card == null) continue;
                JObject obj = new JObject
                {
                    ["cardIndex"] = ci,
                    ["cardType"] = card.cardHead?.CardType ?? string.Empty,
                    ["axisCount"] = card.cardHead?.AxisCount ?? 0,
                    ["inputCount"] = card.cardHead?.InputCount ?? 0,
                    ["outputCount"] = card.cardHead?.OutputCount ?? 0
                };
                if (includeAxes && card.axis != null)
                {
                    var axes = new JArray();
                    for (int ai = 0; ai < card.axis.Count; ai++)
                    {
                        Axis axis = card.axis[ai];
                        if (axis == null) continue;
                        axes.Add(new JObject
                        {
                            ["axisIndex"] = ai,
                            ["axisName"] = axis.AxisName ?? string.Empty,
                            ["axisNum"] = axis.AxisNum,
                            ["pulseToMM"] = axis.PulseToMM,
                            ["homeDirection"] = axis.HomeDirection ?? string.Empty,
                            ["homeMode"] = "一次回零加回找",
                            ["homeSpeed"] = axis.HomeSpeed ?? string.Empty,
                            ["speedInfo"] = axis.SpeedInfo,
                            ["speedMax"] = axis.SpeedMax,
                            ["accMax"] = axis.AccMax,
                            ["decMax"] = axis.DecMax
                        });
                    }
                    obj["axes"] = axes;
                }
                items.Add(obj);
            }
            return new JObject
            {
                ["total"] = items.Count,
                ["items"] = new JArray(items)
            };
        }

        // ===================== 托盘点位清单 =====================

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListTrayPoints(JObject request)
        {
            // TrayPointStore 是运行时缓存，无持久化枚举 API，这里返回空列表提示 AI 当前无缓存。
            // 流程通过指令写入 TrayPointStore，AI 若需查询需先知道 stationName + trayId。
            string stationName = request["stationName"]?.Value<string>();
            int trayId = request["trayId"]?.Value<int>() ?? -1;
            var store = runtime.Stores.TrayPoints;
            if (store == null)
            {
                return new JObject
                {
                    ["available"] = false,
                    ["message"] = "TrayPointStore 未初始化。",
                    ["items"] = new JArray()
                };
            }
            if (string.IsNullOrWhiteSpace(stationName) || trayId < 0)
            {
                return new JObject
                {
                    ["available"] = true,
                    ["message"] = "需提供 stationName 和 trayId 才能读取已缓存的料盘点位。",
                    ["items"] = new JArray()
                };
            }
            if (!store.TryGet(stationName, trayId, out TrayPointGrid grid) || grid == null)
            {
                return new JObject
                {
                    ["available"] = true,
                    ["stationName"] = stationName,
                    ["trayId"] = trayId,
                    ["message"] = "该料盘尚未缓存点位。",
                    ["items"] = new JArray()
                };
            }
            var points = new JArray();
            foreach (TrayPoint pt in grid.Points)
            {
                points.Add(new JObject
                {
                    ["order"] = pt.Order,
                    ["row"] = pt.Row,
                    ["col"] = pt.Col,
                    ["x"] = pt.X,
                    ["y"] = pt.Y,
                    ["z"] = pt.Z,
                    ["u"] = pt.U,
                    ["v"] = pt.V,
                    ["w"] = pt.W
                });
            }
            return new JObject
            {
                ["available"] = true,
                ["stationName"] = stationName,
                ["trayId"] = trayId,
                ["rowCount"] = grid.RowCount,
                ["colCount"] = grid.ColCount,
                ["total"] = points.Count,
                ["items"] = points
            };
        }

        // ===================== 通讯清单 =====================

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListCommunications(JObject request)
        {
            EnsureRuntimeReady();
            bool includeStatus = request["includeStatus"]?.Value<bool>() ?? true;
            string exactName = ReadOptionalString(request, "name");
            string kind = ReadOptionalString(request, "kind");
            if (!string.IsNullOrWhiteSpace(kind)
                && !string.Equals(kind, "tcp", StringComparison.Ordinal)
                && !string.Equals(kind, "serial", StringComparison.Ordinal))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "kind 只能是 tcp 或 serial。");
            }
            IReadOnlyList<SocketInfo> socketInfos = runtime.Stores.Communication?.GetSocketSnapshot() ?? Array.Empty<SocketInfo>();
            IReadOnlyList<SerialPortInfo> serialPortInfos = runtime.Stores.Communication?.GetSerialSnapshot() ?? Array.Empty<SerialPortInfo>();
            var tcpItems = new JArray();
            foreach (SocketInfo sock in socketInfos)
            {
                if (sock == null) continue;
                if (!string.IsNullOrWhiteSpace(exactName)
                    && !string.Equals(sock.Name, exactName, StringComparison.Ordinal)) continue;
                if (string.Equals(kind, "serial", StringComparison.Ordinal)) continue;
                JObject obj = new JObject
                {
                    ["name"] = sock.Name ?? string.Empty,
                    ["type"] = sock.Type ?? string.Empty,
                    ["localAddress"] = sock.LocalAddress ?? string.Empty,
                    ["localPort"] = sock.LocalPort,
                    ["remoteAddress"] = sock.RemoteAddress ?? string.Empty,
                    ["remotePort"] = sock.RemotePort,
                    ["autoReconnect"] = sock.AutoReconnect,
                    ["isServer"] = string.Equals(sock.Type, "Server", StringComparison.Ordinal),
                    ["encodingName"] = sock.EncodingName ?? string.Empty,
                    ["connectTimeoutMs"] = sock.ConnectTimeoutMs
                };
                if (includeStatus && runtime.Communication != null)
                {
                    TcpStatus status = runtime.Communication.GetTcpStatus(sock.Name);
                    obj["isStarted"] = status.IsStarted;
                    obj["isConnected"] = status.IsConnected;
                    obj["connectionState"] = status.ConnectionState.ToString();
                    obj["lastError"] = status.LastError;
                    obj["clientCount"] = status.ClientCount;
                    obj["droppedFrames"] = status.DroppedFrames;
                }
                tcpItems.Add(obj);
            }
            var serialItems = new JArray();
            foreach (SerialPortInfo sp in serialPortInfos)
            {
                if (sp == null) continue;
                if (!string.IsNullOrWhiteSpace(exactName)
                    && !string.Equals(sp.Name, exactName, StringComparison.Ordinal)) continue;
                if (string.Equals(kind, "tcp", StringComparison.Ordinal)) continue;
                JObject obj = new JObject
                {
                    ["name"] = sp.Name ?? string.Empty,
                    ["port"] = sp.Port ?? string.Empty,
                    ["bitRate"] = sp.BitRate ?? string.Empty,
                    ["checkBit"] = sp.CheckBit ?? string.Empty,
                    ["dataBit"] = sp.DataBit ?? string.Empty,
                    ["stopBit"] = sp.StopBit ?? string.Empty,
                    ["encodingName"] = sp.EncodingName ?? string.Empty
                };
                if (includeStatus && runtime.Communication != null)
                {
                    SerialStatus status = runtime.Communication.GetSerialStatus(sp.Name);
                    obj["isOpen"] = status.IsOpen;
                    obj["droppedFrames"] = status.DroppedFrames;
                }
                serialItems.Add(obj);
            }
            if (!string.IsNullOrWhiteSpace(exactName) && tcpItems.Count + serialItems.Count == 0)
            {
                throw new BridgeRequestException(404, "COMMUNICATION_NOT_FOUND",
                    $"未找到通讯对象：{exactName}" + (string.IsNullOrWhiteSpace(kind) ? string.Empty : $" ({kind})"));
            }
            if (!string.IsNullOrWhiteSpace(exactName) && string.IsNullOrWhiteSpace(kind)
                && tcpItems.Count + serialItems.Count > 1)
            {
                throw new BridgeRequestException(409, "COMMUNICATION_AMBIGUOUS",
                    $"通讯名称同时存在于 TCP 和串口配置：{exactName}，请指定 kind。");
            }
            return new JObject
            {
                ["tcpCount"] = tcpItems.Count,
                ["serialCount"] = serialItems.Count,
                ["tcp"] = tcpItems,
                ["serial"] = serialItems
            };
        }

    }
}
