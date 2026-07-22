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
        private JObject HandleGetMigrationConfiguration(JObject request)
        {
            EnsureRuntimeReady();
            string domain = ReadRequiredString(request, "domain").Trim().ToLowerInvariant();
            switch (domain)
            {
                case "motion_io":
                    return new JObject
                    {
                        ["domain"] = domain,
                        ["definition"] = JObject.FromObject(
                            BuildMotionIoMigrationDefinition(),
                            JsonSerializer.Create(migrationContractJsonSettings))
                    };
                case "io_debug":
                    return new JObject
                    {
                        ["domain"] = domain,
                        ["definition"] = JObject.FromObject(
                            BuildIoDebugMigrationDefinition(),
                            JsonSerializer.Create(migrationContractJsonSettings))
                    };
                case "plc":
                    return new JObject
                    {
                        ["domain"] = domain,
                        ["definition"] = JObject.FromObject(
                            BuildPlcMigrationDefinition(),
                            JsonSerializer.Create(migrationContractJsonSettings))
                    };
                case "communication":
                    return new JObject
                    {
                        ["domain"] = domain,
                        ["definition"] = JObject.FromObject(
                            BuildCommunicationMigrationDefinition(),
                            JsonSerializer.Create(migrationContractJsonSettings))
                    };
                default:
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT",
                        "domain 仅支持 motion_io、io_debug、plc、communication。");
            }
        }

        private MotionIoMigrationDefinition BuildMotionIoMigrationDefinition()
        {
            var definition = new MotionIoMigrationDefinition();
            foreach (ControlCard card in runtime.Stores.Cards?.CardData?.controlCards
                ?? new List<ControlCard>())
            {
                definition.ControlCards.Add(new ControlCardMigrationDefinition
                {
                    CardType = card?.cardHead?.CardType ?? string.Empty,
                    InputCount = card?.cardHead?.InputCount ?? 0,
                    OutputCount = card?.cardHead?.OutputCount ?? 0,
                    Axes = (card?.axis ?? new List<Axis>()).Select(axis =>
                        new AxisMigrationDefinition
                        {
                            Name = axis?.AxisName ?? string.Empty,
                            PulseToMm = axis?.PulseToMM ?? 0,
                            HomeDirection = axis?.HomeDirection ?? string.Empty,
                            HomeSpeed = axis?.HomeSpeed ?? string.Empty,
                            SpeedInfo = axis?.SpeedInfo ?? 0,
                            MaxSpeed = axis?.SpeedMax ?? 0,
                            AccelerationTime = axis?.AccMax ?? 0,
                            DecelerationTime = axis?.DecMax ?? 0
                        }).ToList()
                });
            }
            List<List<IO>> ioMap = runtime.Stores.IoConfiguration?.Map ?? new List<List<IO>>();
            for (int cardIndex = 0; cardIndex < ioMap.Count; cardIndex++)
            {
                foreach (IO io in ioMap[cardIndex] ?? new List<IO>())
                {
                    definition.IoMappings.Add(new IoMigrationDefinition
                    {
                        Name = io?.Name ?? string.Empty,
                        CardIndex = cardIndex,
                        Module = io?.Module ?? 0,
                        IoIndex = io?.IOIndex ?? string.Empty,
                        IoType = io?.IOType ?? string.Empty,
                        UsedType = io?.UsedType ?? string.Empty,
                        EffectLevel = io?.EffectLevel ?? string.Empty,
                        Note = io?.Note ?? string.Empty,
                        IsRemark = io?.IsRemark ?? false
                    });
                }
            }
            return definition;
        }

        private IoDebugMigrationDefinition BuildIoDebugMigrationDefinition()
        {
            IODebugMap map = runtime.EditorUi?.IoDebugMap ?? new IODebugMap();
            return new IoDebugMigrationDefinition
            {
                InputNames = (map.inputs ?? new List<IO>()).Select(io => io?.Name ?? string.Empty).ToList(),
                OutputNames = (map.outputs ?? new List<IO>()).Select(io => io?.Name ?? string.Empty).ToList(),
                Group1 = BuildIoDebugConnectionDefinitions(map.iOConnects),
                Group2 = BuildIoDebugConnectionDefinitions(map.iOConnects2),
                Group3 = BuildIoDebugConnectionDefinitions(map.iOConnects3)
            };
        }

        private static List<IoDebugConnectionMigrationDefinition> BuildIoDebugConnectionDefinitions(
            IEnumerable<IOConnect> connections)
        {
            return (connections ?? Enumerable.Empty<IOConnect>()).Select(connection =>
                new IoDebugConnectionMigrationDefinition
                {
                    Output1 = connection?.Output?.Name ?? string.Empty,
                    Output2 = connection?.Output2?.Name ?? string.Empty,
                    Input1 = connection?.Intput1?.Name ?? string.Empty,
                    Input2 = connection?.Intput2?.Name ?? string.Empty
                }).ToList();
        }

        private PlcMigrationDefinition BuildPlcMigrationDefinition()
        {
            PlcConfiguration snapshot = runtime.Stores.Plc?.GetSnapshot() ?? new PlcConfiguration();
            return new PlcMigrationDefinition
            {
                Devices = (snapshot.Devices ?? new List<PlcDeviceConfig>()).Select(device =>
                    new PlcDeviceMigrationDefinition
                    {
                        Name = device?.Name ?? string.Empty,
                        Profile = device?.Profile.ToString() ?? PlcDeviceProfile.GenericModbusTcp.ToString(),
                        IpAddress = device?.IpAddress ?? string.Empty,
                        Port = device?.Port ?? 502,
                        UnitId = device?.UnitId ?? 1,
                        ConnectTimeoutMs = device?.ConnectTimeoutMs ?? 1000,
                        AutoConnect = device?.AutoConnect ?? true,
                        ScanIntervalMs = device?.ScanIntervalMs ?? 50,
                        DataFormat = device?.DataFormat ?? "CDAB",
                        IsStringReverse = device?.IsStringReverse ?? false,
                        AddressStartWithZero = device?.AddressStartWithZero ?? true,
                        StatusVariableName = device?.StatusVariableName ?? string.Empty,
                        Mappings = (device?.Mappings ?? new List<PlcMapConfig>()).Select(mapping =>
                            new PlcMapMigrationDefinition
                            {
                                Id = mapping?.Id ?? string.Empty,
                                Name = mapping?.Name ?? string.Empty,
                                Enabled = mapping?.Enabled ?? true,
                                Area = mapping?.Area.ToString() ?? PlcArea.HoldingRegister.ToString(),
                                StartAddress = mapping?.StartAddress ?? 0,
                                DataType = mapping?.DataType.ToString() ?? PlcDataType.Float.ToString(),
                                Direction = mapping?.Direction.ToString() ?? PlcMapDirection.ReadFromPlc.ToString(),
                                Priority = mapping?.Priority.ToString() ?? PlcMapPriority.High.ToString(),
                                ElementCount = mapping?.ElementCount ?? 1,
                                StringByteLength = mapping?.StringByteLength ?? 0,
                                VariableNames = mapping?.VariableNames?.ToList() ?? new List<string>(),
                                ChangeTolerance = mapping?.ChangeTolerance ?? 0
                            }).ToList()
                    }).ToList()
            };
        }

        private CommunicationMigrationDefinition BuildCommunicationMigrationDefinition()
        {
            return new CommunicationMigrationDefinition
            {
                Tcp = (runtime.Stores.Communication?.GetSocketSnapshot() ?? new List<SocketInfo>()).Select(item =>
                    new TcpMigrationDefinition
                    {
                        Id = item?.ID ?? 0,
                        Name = item?.Name ?? string.Empty,
                        Type = item?.Type ?? string.Empty,
                        LocalAddress = item?.LocalAddress ?? string.Empty,
                        LocalPort = item?.LocalPort ?? 0,
                        RemoteAddress = item?.RemoteAddress ?? string.Empty,
                        RemotePort = item?.RemotePort ?? 0,
                        AutoReconnect = item?.AutoReconnect ?? false,
                        FrameMode = item?.FrameMode ?? string.Empty,
                        FrameDelimiter = item?.FrameDelimiter ?? string.Empty,
                        EncodingName = item?.EncodingName ?? string.Empty,
                        ConnectTimeoutMs = item?.ConnectTimeoutMs ?? 0
                    }).ToList(),
                Serial = (runtime.Stores.Communication?.GetSerialSnapshot() ?? new List<SerialPortInfo>()).Select(item =>
                    new SerialMigrationDefinition
                    {
                        Id = item?.ID ?? 0,
                        Name = item?.Name ?? string.Empty,
                        Port = item?.Port ?? string.Empty,
                        BitRate = item?.BitRate ?? string.Empty,
                        CheckBit = item?.CheckBit ?? string.Empty,
                        DataBit = item?.DataBit ?? string.Empty,
                        StopBit = item?.StopBit ?? string.Empty,
                        FrameMode = item?.FrameMode ?? string.Empty,
                        FrameDelimiter = item?.FrameDelimiter ?? string.Empty,
                        EncodingName = item?.EncodingName ?? string.Empty
                    }).ToList()
            };
        }

        private JObject HandlePreviewMotionIoConfiguration(JObject request)
        {
            MotionIoMigrationDefinition definition = ReadRequiredObject(request, "definition")
                .ToObject<MotionIoMigrationDefinition>();
            BuildMotionIoCandidate(definition, out Card cards, out List<List<IO>> ioMap);
            var preview = new MigrationConfigurationPreview
            {
                Kind = "motion_io",
                Cards = cards,
                IoMap = ioMap,
                BaseStateHash = ComputeMigrationStateHash("motion_io")
            };
            return RegisterMigrationPreview(preview, new JArray
            {
                $"将控制卡配置替换为 {cards.controlCards.Count} 张卡",
                $"将IO映射替换为 {ioMap.Sum(list => list?.Count ?? 0)} 项"
            });
        }

        private JObject HandlePreviewIoDebugConfiguration(JObject request)
        {
            IoDebugMigrationDefinition definition = ReadRequiredObject(request, "definition")
                .ToObject<IoDebugMigrationDefinition>();
            IODebugMap candidate = BuildIoDebugCandidate(definition);
            var preview = new MigrationConfigurationPreview
            {
                Kind = "io_debug",
                IoDebug = candidate,
                BaseStateHash = ComputeMigrationStateHash("io_debug")
            };
            return RegisterMigrationPreview(preview, new JArray
            {
                $"将IO调试输入显示配置替换为 {candidate.inputs.Count} 项",
                $"将IO调试输出显示配置替换为 {candidate.outputs.Count} 项",
                $"将IO关联配置替换为 {candidate.iOConnects.Count + candidate.iOConnects2.Count + candidate.iOConnects3.Count} 项"
            });
        }

        private JObject HandlePreviewPlcConfiguration(JObject request)
        {
            PlcMigrationDefinition definition = ReadRequiredObject(request, "definition")
                .ToObject<PlcMigrationDefinition>();
            PlcConfiguration candidate = BuildPlcCandidate(definition);
            if (!PlcConfigStore.Validate(candidate, runtime.Stores.Values, out string error))
            {
                throw new BridgeRequestException(400, "PLC_CONFIG_INVALID", error);
            }
            var preview = new MigrationConfigurationPreview
            {
                Kind = "plc",
                Plc = candidate,
                BaseStateHash = ComputeMigrationStateHash("plc")
            };
            return RegisterMigrationPreview(preview, new JArray
            {
                $"将PLC配置替换为 {candidate.Devices.Count} 个设备、{candidate.Devices.Sum(item => item.Mappings?.Count ?? 0)} 项映射"
            });
        }

        private JObject HandlePreviewCommunicationConfiguration(JObject request)
        {
            CommunicationMigrationDefinition definition = ReadRequiredObject(request, "definition")
                .ToObject<CommunicationMigrationDefinition>();
            BuildCommunicationCandidate(definition, out List<SocketInfo> sockets, out List<SerialPortInfo> serialPorts);
            var validator = new CommunicationConfigStore();
            if (!validator.ReplaceSockets(sockets, out string error)
                || !validator.ReplaceSerialPorts(serialPorts, out error))
            {
                throw new BridgeRequestException(400, "COMMUNICATION_CONFIG_INVALID", error);
            }
            var preview = new MigrationConfigurationPreview
            {
                Kind = "communication",
                Sockets = sockets,
                SerialPorts = serialPorts,
                BaseStateHash = ComputeMigrationStateHash("communication")
            };
            return RegisterMigrationPreview(preview, new JArray
            {
                $"将TCP配置替换为 {sockets.Count} 项",
                $"将串口配置替换为 {serialPorts.Count} 项"
            });
        }

        private JObject RegisterMigrationPreview(MigrationConfigurationPreview draft, JArray messages)
        {
            EnsureRuntimeReady();
            JObject patch = new JObject
            {
                ["action"] = "migration_configuration",
                ["domain"] = draft.Kind,
                ["messages"] = messages.DeepClone()
            };
            string previewId = RegisterManagePreview(patch);
            PreviewApprovalRecord record;
            lock (previewLock)
            {
                record = previewRecords[previewId];
                record.MigrationConfigurationPreview = draft;
                record.BaseStateHash = draft.BaseStateHash;
            }
            return new JObject
            {
                ["previewId"] = previewId,
                ["confirmed"] = record.Confirmed,
                ["committed"] = false,
                ["configurationSaved"] = false,
                ["domain"] = draft.Kind,
                ["messages"] = messages,
                ["changes"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "configuration.replace",
                        ["object"] = draft.Kind,
                        ["newValue"] = string.Join("；", messages.Values<string>())
                    }
                }
            };
        }

        private JObject HandleApplyMigrationConfiguration(JObject request)
        {
            string previewId = ReadRequiredString(request, "previewId");
            ValidateConfirmedManagePreview(previewId);
            MigrationConfigurationPreview draft;
            lock (previewLock)
            {
                if (!previewRecords.TryGetValue(previewId, out PreviewApprovalRecord record)
                    || record.MigrationConfigurationPreview == null)
                {
                    throw new BridgeRequestException(404, "PREVIEW_NOT_FOUND", "迁移配置预演不存在或已过期。");
                }
                draft = record.MigrationConfigurationPreview;
            }
            EnsureAllProcsInactiveForAiStructureCommit("提交迁移平台配置");
            if (!string.Equals(draft.BaseStateHash, ComputeMigrationStateHash(draft.Kind), StringComparison.Ordinal))
            {
                throw new BridgeRequestException(409, "PREVIEW_BASE_CHANGED",
                    "预演后的基础配置已经变化，本次提交未执行。",
                    new JObject
                    {
                        ["reason"] = "configuration_changed_after_preview",
                        ["retryableWhen"] = "configuration_previewed_again",
                        ["sideEffects"] = "none"
                    }.ToString(Formatting.None));
            }

            ApplyMigrationConfiguration(draft);
            RemovePreview(previewId);
            return new JObject
            {
                ["previewId"] = previewId,
                ["domain"] = draft.Kind,
                ["committed"] = true,
                ["configurationSaved"] = true
            };
        }

        private void ApplyMigrationConfiguration(MigrationConfigurationPreview draft)
        {
            switch (draft.Kind)
            {
                case "motion_io":
                    using (var batch = new ConfigurationBatchWriter(runtime.Paths.ConfigPath))
                    {
                        batch.AddJson("card.json", draft.Cards);
                        batch.AddJson("IOMap.json", draft.IoMap);
                        batch.Commit();
                    }
                    if (!runtime.Stores.Cards.Load(runtime.Paths.ConfigPath, out string cardLoadError))
                    {
                        runtime.Safety.Lock("迁移后的控制卡配置加载失败：" + cardLoadError);
                    }
                    runtime.EditorUi?.RefreshMotionIo();
                    if (runtime.ProcessEngine?.Context != null) runtime.ProcessEngine.Context.IoMap = runtime.Stores.IoConfiguration.ByName;
                    break;
                case "io_debug":
                    using (var batch = new ConfigurationBatchWriter(runtime.Paths.ConfigPath))
                    {
                        batch.AddJson("IODebugMap.json", draft.IoDebug);
                        batch.Commit();
                    }
                    runtime.EditorUi?.RefreshIoDebug();
                    break;
                case "plc":
                    if (!runtime.Stores.Plc.Save(runtime.Paths.ConfigPath, draft.Plc, runtime.Stores.Values, out string plcError))
                    {
                        throw new BridgeRequestException(500, "PLC_CONFIG_SAVE_FAILED", plcError);
                    }
                    if (runtime.PlcRuntime != null && !runtime.PlcRuntime.ReloadConfiguration(true, out string reloadError))
                    {
                        runtime.ProcessEngine?.Logger?.Log(reloadError, LogLevel.Error);
                    }
                    break;
                case "communication":
                    using (var batch = new ConfigurationBatchWriter(runtime.Paths.ConfigPath))
                    {
                        batch.AddJson("SocketInfo.json", draft.Sockets);
                        batch.AddJson("SerialPortInfo.json", draft.SerialPorts);
                        batch.Commit();
                    }
                    bool socketsLoaded = runtime.Stores.Communication.ReplaceSockets(
                        draft.Sockets, out string socketError);
                    bool serialPortsLoaded = runtime.Stores.Communication.ReplaceSerialPorts(
                        draft.SerialPorts, out string serialError);
                    if (!socketsLoaded || !serialPortsLoaded)
                    {
                        runtime.Safety.Lock(socketError ?? serialError ?? "迁移后的通讯配置加载失败。");
                    }
                    runtime.EditorUi?.RefreshCommunication();
                    if (runtime.ProcessEngine?.Context != null)
                    {
                        runtime.ProcessEngine.Context.SocketInfos = runtime.Stores.Communication.GetSocketSnapshot().ToList();
                        runtime.ProcessEngine.Context.SerialPortInfos = runtime.Stores.Communication.GetSerialSnapshot().ToList();
                    }
                    break;
                default:
                    throw new BridgeRequestException(400, "MIGRATION_DOMAIN_INVALID", $"不支持的迁移领域：{draft.Kind}");
            }
        }

        private JObject HandleValidatePlatformConfiguration()
        {
            EnsureRuntimeReady();
            var domains = new JArray();
            AddMigrationValidation(domains, "motion_io", () =>
            {
                if (!runtime.Stores.Cards.TryValidateAllAxes(out List<string> errors))
                {
                    throw new InvalidOperationException(string.Join("；", errors));
                }
                ValidateIoMapAgainstCards(runtime.Stores.Cards.CardData, runtime.Stores.IoConfiguration.Map);
            });
            AddMigrationValidation(domains, "io_debug", () =>
                ValidateIoDebugMap(runtime.EditorUi?.IoDebugMap ?? new IODebugMap(),
                    runtime.Stores.IoConfiguration.ByName));
            AddMigrationValidation(domains, "plc", () =>
            {
                if (!PlcConfigStore.Validate(runtime.Stores.Plc.GetSnapshot(), runtime.Stores.Values, out string error))
                {
                    throw new InvalidOperationException(error);
                }
            });
            AddMigrationValidation(domains, "communication", () =>
            {
                var validator = new CommunicationConfigStore();
                if (!validator.ReplaceSockets(runtime.Stores.Communication.GetSocketSnapshot(), out string error)
                    || !validator.ReplaceSerialPorts(runtime.Stores.Communication.GetSerialSnapshot(), out error))
                {
                    throw new InvalidOperationException(error);
                }
            });
            return new JObject
            {
                ["valid"] = domains.OfType<JObject>().All(item => item["valid"]?.Value<bool>() == true),
                ["domains"] = domains
            };
        }

        private static void AddMigrationValidation(JArray target, string domain, Action validate)
        {
            try
            {
                validate();
                target.Add(new JObject { ["domain"] = domain, ["valid"] = true });
            }
            catch (Exception ex)
            {
                target.Add(new JObject
                {
                    ["domain"] = domain,
                    ["valid"] = false,
                    ["error"] = ex.Message
                });
            }
        }

        private static void BuildMotionIoCandidate(
            MotionIoMigrationDefinition definition,
            out Card cards,
            out List<List<IO>> ioMap)
        {
            if (definition == null) throw new BridgeRequestException(400, "INVALID_ARGUMENT", "控制卡与IO配置不能为空。");
            cards = new Card();
            foreach (ControlCardMigrationDefinition source
                in definition.ControlCards ?? new List<ControlCardMigrationDefinition>())
            {
                if (source == null) throw new BridgeRequestException(400, "INVALID_ARGUMENT", "控制卡配置包含空项。");
                var card = new ControlCard
                {
                    cardHead = new CardHead
                    {
                        CardType = source.CardType ?? string.Empty,
                        AxisCount = source.Axes?.Count ?? 0,
                        InputCount = source.InputCount,
                        OutputCount = source.OutputCount
                    }
                };
                int axisIndex = 0;
                foreach (AxisMigrationDefinition axis in source.Axes ?? new List<AxisMigrationDefinition>())
                {
                    card.axis.Add(new Axis
                    {
                        AxisNum = axisIndex++,
                        AxisName = axis?.Name ?? string.Empty,
                        PulseToMM = axis?.PulseToMm ?? 0,
                        HomeDirection = axis?.HomeDirection ?? string.Empty,
                        HomeSpeed = axis?.HomeSpeed ?? string.Empty,
                        SpeedInfo = axis?.SpeedInfo ?? 0,
                        SpeedMax = axis?.MaxSpeed ?? 0,
                        AccMax = axis?.AccelerationTime ?? 0,
                        DecMax = axis?.DecelerationTime ?? 0
                    });
                }
                cards.controlCards.Add(card);
            }
            var validator = new CardConfigStore();
            validator.SetCard(cards);
            if (!validator.TryValidateAllAxes(out List<string> axisErrors))
            {
                throw new BridgeRequestException(400, "MOTION_CONFIG_INVALID", string.Join("；", axisErrors));
            }

            ioMap = Enumerable.Range(0, cards.controlCards.Count).Select(_ => new List<IO>()).ToList();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (IoMigrationDefinition source in definition.IoMappings ?? new List<IoMigrationDefinition>())
            {
                if (source == null || source.CardIndex < 0 || source.CardIndex >= ioMap.Count)
                {
                    throw new BridgeRequestException(400, "IO_CONFIG_INVALID", "IO配置引用了无效控制卡索引。");
                }
                string ioName = source.Name?.Trim() ?? string.Empty;
                if (ioName.Length > 0 && !names.Add(ioName))
                {
                    throw new BridgeRequestException(400, "IO_CONFIG_INVALID", $"IO名称重复：{ioName}");
                }
                ioMap[source.CardIndex].Add(new IO
                {
                    Name = ioName,
                    CardNum = source.CardIndex,
                    Module = source.Module,
                    IOIndex = source.IoIndex ?? string.Empty,
                    IOType = source.IoType ?? string.Empty,
                    UsedType = source.UsedType ?? string.Empty,
                    EffectLevel = source.EffectLevel ?? string.Empty,
                    Note = source.Note ?? string.Empty,
                    IsRemark = source.IsRemark
                });
            }
            foreach (List<IO> cardIo in ioMap)
            {
                cardIo.Sort((left, right) => string.Equals(left.IOType, "通用输入", StringComparison.Ordinal)
                    == string.Equals(right.IOType, "通用输入", StringComparison.Ordinal) ? 0
                    : string.Equals(left.IOType, "通用输入", StringComparison.Ordinal) ? -1 : 1);
                for (int i = 0; i < cardIo.Count; i++) cardIo[i].Index = i;
            }
            ValidateIoMapAgainstCards(cards, ioMap);
        }

        private static void ValidateIoMapAgainstCards(Card cards, List<List<IO>> ioMap)
        {
            if (cards?.controlCards == null || ioMap == null || cards.controlCards.Count != ioMap.Count)
            {
                throw new InvalidOperationException("控制卡数量与IO卡分组数量不一致。");
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int cardIndex = 0; cardIndex < cards.controlCards.Count; cardIndex++)
            {
                CardHead head = cards.controlCards[cardIndex]?.cardHead
                    ?? throw new InvalidOperationException($"{cardIndex}号控制卡配置为空。");
                List<IO> items = ioMap[cardIndex] ?? new List<IO>();
                int inputs = items.Count(item => item != null && item.IOType == "通用输入");
                int outputs = items.Count(item => item != null && item.IOType == "通用输出");
                if (inputs != head.InputCount || outputs != head.OutputCount)
                {
                    throw new InvalidOperationException($"{cardIndex}号卡IO数量不一致：输入{inputs}/{head.InputCount}，输出{outputs}/{head.OutputCount}。");
                }
                foreach (IO item in items)
                {
                    if (item == null || item.CardNum != cardIndex)
                    {
                        throw new InvalidOperationException($"{cardIndex}号卡包含空IO或错误卡号。");
                    }
                    if (!string.IsNullOrWhiteSpace(item.Name) && !names.Add(item.Name))
                    {
                        throw new InvalidOperationException($"IO名称重复：{item.Name}");
                    }
                    if (item.IOType != "通用输入" && item.IOType != "通用输出")
                    {
                        throw new InvalidOperationException($"IO[{item.Name}]类型无效：{item.IOType}");
                    }
                }
            }
        }

        private IODebugMap BuildIoDebugCandidate(IoDebugMigrationDefinition definition)
        {
            if (definition == null) throw new BridgeRequestException(400, "INVALID_ARGUMENT", "IO调试配置不能为空。");
            Dictionary<string, IO> ioByName = runtime.Stores.IoConfiguration?.ByName
                ?? throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "IO配置未初始化。");
            var result = new IODebugMap
            {
                inputs = ResolveIoNames(definition.InputNames, "通用输入", ioByName),
                outputs = ResolveIoNames(definition.OutputNames, "通用输出", ioByName),
                iOConnects = BuildIoConnections(definition.Group1, ioByName),
                iOConnects2 = BuildIoConnections(definition.Group2, ioByName),
                iOConnects3 = BuildIoConnections(definition.Group3, ioByName)
            };
            ValidateIoDebugMap(result, ioByName);
            return result;
        }

        private static List<IO> ResolveIoNames(IEnumerable<string> names, string expectedType, Dictionary<string, IO> ioByName)
        {
            var result = new List<IO>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in names ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name) || !unique.Add(name.Trim())
                    || !ioByName.TryGetValue(name.Trim(), out IO io) || io == null
                    || !string.Equals(io.IOType, expectedType, StringComparison.Ordinal))
                {
                    throw new BridgeRequestException(400, "IO_DEBUG_CONFIG_INVALID",
                        $"IO调试配置引用无效或重复的{expectedType}：{name}");
                }
                result.Add(io.CloneForDebug());
            }
            return result;
        }

        private static List<IOConnect> BuildIoConnections(
            IEnumerable<IoDebugConnectionMigrationDefinition> definitions,
            Dictionary<string, IO> ioByName)
        {
            return (definitions ?? Enumerable.Empty<IoDebugConnectionMigrationDefinition>())
                .Select(item => new IOConnect
                {
                    Output = ResolveOptionalIo(item?.Output1, "通用输出", ioByName),
                    Output2 = ResolveOptionalIo(item?.Output2, "通用输出", ioByName),
                    Intput1 = ResolveOptionalIo(item?.Input1, "通用输入", ioByName),
                    Intput2 = ResolveOptionalIo(item?.Input2, "通用输入", ioByName)
                }).ToList();
        }

        private static IO ResolveOptionalIo(string name, string expectedType, Dictionary<string, IO> ioByName)
        {
            if (string.IsNullOrWhiteSpace(name)) return new IO();
            if (!ioByName.TryGetValue(name.Trim(), out IO io) || io == null
                || !string.Equals(io.IOType, expectedType, StringComparison.Ordinal))
            {
                throw new BridgeRequestException(400, "IO_DEBUG_CONFIG_INVALID", $"IO关联引用无效：{name}");
            }
            return io.CloneForDebug();
        }

        private static void ValidateIoDebugMap(IODebugMap candidate, Dictionary<string, IO> ioByName)
        {
            if (candidate == null) throw new InvalidOperationException("IO调试配置为空。");
            foreach (IO io in (candidate.inputs ?? new List<IO>()).Concat(candidate.outputs ?? new List<IO>()))
            {
                if (io == null || string.IsNullOrWhiteSpace(io.Name) || !ioByName.ContainsKey(io.Name))
                {
                    throw new InvalidOperationException($"IO调试显示项引用不存在：{io?.Name}");
                }
            }
            foreach (IOConnect connection in (candidate.iOConnects ?? new List<IOConnect>())
                .Concat(candidate.iOConnects2 ?? new List<IOConnect>())
                .Concat(candidate.iOConnects3 ?? new List<IOConnect>()))
            {
                foreach (IO io in new[] { connection?.Output, connection?.Output2, connection?.Intput1, connection?.Intput2 })
                {
                    if (io != null && !string.IsNullOrWhiteSpace(io.Name) && !ioByName.ContainsKey(io.Name))
                    {
                        throw new InvalidOperationException($"IO关联引用不存在：{io.Name}");
                    }
                }
            }
        }

        private static PlcConfiguration BuildPlcCandidate(PlcMigrationDefinition definition)
        {
            if (definition == null) throw new BridgeRequestException(400, "INVALID_ARGUMENT", "PLC配置不能为空。");
            var result = new PlcConfiguration();
            foreach (PlcDeviceMigrationDefinition source in definition.Devices ?? new List<PlcDeviceMigrationDefinition>())
            {
                if (!Enum.TryParse(source?.Profile, true, out PlcDeviceProfile profile))
                    throw new BridgeRequestException(400, "PLC_CONFIG_INVALID", $"PLC Profile无效：{source?.Profile}");
                var device = new PlcDeviceConfig
                {
                    Name = source.Name ?? string.Empty,
                    Profile = profile,
                    IpAddress = source.IpAddress ?? string.Empty,
                    Port = source.Port,
                    UnitId = source.UnitId,
                    ConnectTimeoutMs = source.ConnectTimeoutMs,
                    AutoConnect = source.AutoConnect,
                    ScanIntervalMs = source.ScanIntervalMs,
                    DataFormat = source.DataFormat ?? string.Empty,
                    IsStringReverse = source.IsStringReverse,
                    AddressStartWithZero = source.AddressStartWithZero,
                    StatusVariableName = source.StatusVariableName ?? string.Empty
                };
                foreach (PlcMapMigrationDefinition mapSource in source.Mappings ?? new List<PlcMapMigrationDefinition>())
                {
                    if (!Enum.TryParse(mapSource?.Area, true, out PlcArea area)
                        || !Enum.TryParse(mapSource?.DataType, true, out PlcDataType dataType)
                        || !Enum.TryParse(mapSource?.Direction, true, out PlcMapDirection direction)
                        || !Enum.TryParse(mapSource?.Priority, true, out PlcMapPriority priority))
                    {
                        throw new BridgeRequestException(400, "PLC_CONFIG_INVALID", $"PLC映射枚举无效：{mapSource?.Name}");
                    }
                    device.Mappings.Add(new PlcMapConfig
                    {
                        Id = string.IsNullOrWhiteSpace(mapSource.Id) ? Guid.NewGuid().ToString("N") : mapSource.Id,
                        Name = mapSource.Name ?? string.Empty,
                        Enabled = mapSource.Enabled,
                        Area = area,
                        StartAddress = mapSource.StartAddress,
                        DataType = dataType,
                        Direction = direction,
                        Priority = priority,
                        ElementCount = mapSource.ElementCount,
                        StringByteLength = mapSource.StringByteLength,
                        VariableNames = mapSource.VariableNames?.ToList() ?? new List<string>(),
                        ChangeTolerance = mapSource.ChangeTolerance
                    });
                }
                result.Devices.Add(device);
            }
            return result;
        }

        private static void BuildCommunicationCandidate(
            CommunicationMigrationDefinition definition,
            out List<SocketInfo> sockets,
            out List<SerialPortInfo> serialPorts)
        {
            if (definition == null) throw new BridgeRequestException(400, "INVALID_ARGUMENT", "通讯配置不能为空。");
            sockets = (definition.Tcp ?? new List<TcpMigrationDefinition>()).Select(item => new SocketInfo
            {
                ID = item.Id,
                Name = item.Name ?? string.Empty,
                Type = item.Type ?? string.Empty,
                LocalAddress = item.LocalAddress ?? string.Empty,
                LocalPort = item.LocalPort,
                RemoteAddress = item.RemoteAddress ?? string.Empty,
                RemotePort = item.RemotePort,
                AutoReconnect = item.AutoReconnect,
                FrameMode = item.FrameMode ?? string.Empty,
                FrameDelimiter = item.FrameDelimiter ?? string.Empty,
                EncodingName = item.EncodingName ?? string.Empty,
                ConnectTimeoutMs = item.ConnectTimeoutMs
            }).ToList();
            serialPorts = (definition.Serial ?? new List<SerialMigrationDefinition>()).Select(item => new SerialPortInfo
            {
                ID = item.Id,
                Name = item.Name ?? string.Empty,
                Port = item.Port ?? string.Empty,
                BitRate = item.BitRate ?? string.Empty,
                CheckBit = item.CheckBit ?? string.Empty,
                DataBit = item.DataBit ?? string.Empty,
                StopBit = item.StopBit ?? string.Empty,
                FrameMode = item.FrameMode ?? string.Empty,
                FrameDelimiter = item.FrameDelimiter ?? string.Empty,
                EncodingName = item.EncodingName ?? string.Empty
            }).ToList();
        }

        private string ComputeMigrationStateHash(string kind)
        {
            var state = new JObject { ["domain"] = kind };
            switch (kind)
            {
                case "motion_io":
                    state["cards"] = JToken.FromObject(runtime.Stores.Cards?.CardData ?? new Card());
                    state["io"] = JToken.FromObject(runtime.Stores.IoConfiguration?.Map ?? new List<List<IO>>());
                    break;
                case "io_debug":
                    state["ioDebug"] = JToken.FromObject(
                        runtime.EditorUi?.IoDebugMap ?? new IODebugMap());
                    break;
                case "plc":
                    state["plc"] = JToken.FromObject(runtime.Stores.Plc?.GetSnapshot() ?? new PlcConfiguration());
                    break;
                case "communication":
                    state["tcp"] = JToken.FromObject(runtime.Stores.Communication?.GetSocketSnapshot() ?? new List<SocketInfo>());
                    state["serial"] = JToken.FromObject(runtime.Stores.Communication?.GetSerialSnapshot() ?? new List<SerialPortInfo>());
                    break;
            }
            return ComputePatchHash(state);
        }

        // ===================== IO 操作 =====================

        [System.Diagnostics.DebuggerNonUserCode]
        private sealed class MigrationConfigurationPreview
        {
            public string Kind { get; set; }

            public string BaseStateHash { get; set; }

            public Card Cards { get; set; }

            public List<List<IO>> IoMap { get; set; }

            public IODebugMap IoDebug { get; set; }

            public PlcConfiguration Plc { get; set; }

            public List<SocketInfo> Sockets { get; set; }

            public List<SerialPortInfo> SerialPorts { get; set; }
        }

    }
}
