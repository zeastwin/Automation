using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using Automation.DeviceSdk;

// 模块：平台内置 HMI / 设备流程消息。
// 职责范围：承接旧 CoreWork 的消息分发语义，通过 IValueStore 读写运行值并投影生产记录。
// 排查入口：执行失败先检查流程中的自定义函数名是否与旧 NotifyInfo.Name 完全一致，
// 再检查消息参数变量及 D:\AutomationLogs\Hmi\Equipment。

namespace Automation.Hmi
{
    internal sealed class EquipmentProductionRecord
    {
        [DisplayName("SN")]
        public string SN { get; set; }

        [DisplayName("时间")]
        public string Time { get; set; }

        [DisplayName("流程信息")]
        public string ProcessInfo { get; set; }

        [DisplayName("结果")]
        public string InfoData { get; set; }

        [DisplayName("模式")]
        public string Mode { get; set; }

        [Browsable(false)]
        public bool IsFailure { get; set; }

        internal EquipmentProductionRecord Clone()
        {
            return (EquipmentProductionRecord)MemberwiseClone();
        }
    }

    internal sealed class EquipmentAlarmRecord
    {
        [DisplayName("时间")]
        public string Time { get; set; }

        [DisplayName("SN")]
        public string SN { get; set; }

        [DisplayName("工位")]
        public string Position { get; set; }

        [DisplayName("异常信息")]
        public string Message { get; set; }

        [DisplayName("处理提示")]
        public string Resolution { get; set; }

        internal EquipmentAlarmRecord Clone()
        {
            return (EquipmentAlarmRecord)MemberwiseClone();
        }
    }

    internal sealed class EquipmentProcessMessageSnapshot
    {
        public long Revision { get; set; }
        public IReadOnlyList<EquipmentProductionRecord> InputRecords { get; set; }
        public IReadOnlyList<EquipmentProductionRecord> OutputRecords { get; set; }
        public IReadOnlyList<EquipmentAlarmRecord> Alarms { get; set; }
        public IReadOnlyList<string> Logs { get; set; }
        public int InputTotal { get; set; }
        public int OutputTotal { get; set; }
        public int GoodTotal { get; set; }
        public int DefectTotal { get; set; }
        public double? LastCycleSeconds { get; set; }
    }

    /// <summary>
    /// 把旧 CoreWork.Notify/PtProcess 的消息分发语义适配为平台自定义函数。
    /// 每个旧 NotifyInfo.Name 都注册成同名函数，不引入中转变量。
    /// </summary>
    internal sealed class EquipmentProcessMessageService
    {
        private static readonly string[] RegisteredFunctionNames =
        {
            "消息 初始化",
            "主流程信息||消息 电批数据本地保存-电批_1",
            "主流程信息||消息 电批数据本地保存-电批_2",
            "主流程信息||消息 绘制电批曲线(SN_Code-电批_1,10003,1)",
            "主流程信息||消息 绘制电批曲线(SN_Code-电批_2,10004,2)",
            "主流程信息||消息 绘制压力曲线(A工位)",
            "主流程信息||消息 绘制压力曲线(B工位)",
            "主流程信息||消息 获取出站数据",
            "主流程信息||消息 获取压力曲线数据(A工位,X轴压力曲线数据-压力曲线_A工位,Y轴压力曲线数据-压力曲线_A工位)",
            "主流程信息||消息 获取压力曲线数据(B工位,X轴压力曲线数据-压力曲线_B工位,Y轴压力曲线数据-压力曲线_B工位)",
            "主流程信息||消息 记录出站数据",
            "主流程信息||消息 清除电批缓存区数据(10003)",
            "主流程信息||消息 清除电批缓存区数据(10004)",
            "主流程信息||消息 数据本地保存-出站位",
            "主流程信息||消息 数据本地保存-进站位",
            "主流程信息||消息 显示出站结果-出站位",
            "主流程信息||消息 显示进站结果-进站位",
            "主流程信息||消息 SN去除符号并赋值(SN_Code-出站位,SN_Code-MES过站)",
            "主流程信息||消息 SN去除符号并赋值(SN_Code-进站位,SN_Code-MES查询)",
            "HIVE流程信息||更新设备状态",
            "HIVE流程信息||HIVE报警监控",
            "HIVE流程信息||HIVE设备状态监控",
            "MES流程信息||消息 进站MES查询",
            "MES流程信息||消息 MES<-->PC心跳",
            "MES流程信息||消息 MES过站",
            "PDCA流程信息||消息 转移PDCA文件并删除",
            "PDCA流程信息||消息 PDCA范围检测",
            "PDCA流程信息||消息 PDCA上传",
            "PDCA流程信息||消息 PDCA数据收集",
            "PDCA流程信息||消息 PDCA图片打包",
            "Video操作信息||结束录制视频(1,SN_Code-Video_1)",
            "Video操作信息||结束录制视频(2,SN_Code-Video_2)",
            "Video操作信息||开始录制视频(1,SN_Code-Video_1,录制结果-Video_1)",
            "Video操作信息||开始录制视频(2,SN_Code-Video_2,录制结果-Video_2)",
            "记录PLC交互流程信息||消息 记录出站位读取PLC触发数据",
            "记录PLC交互流程信息||消息 记录出站位读取PLC数据",
            "记录PLC交互流程信息||消息 记录出站位写入PLC数据",
            "记录PLC交互流程信息||消息 记录电批读取PLC触发数据(1)",
            "记录PLC交互流程信息||消息 记录电批读取PLC触发数据(2)",
            "记录PLC交互流程信息||消息 记录电批写入PLC数据(1)",
            "记录PLC交互流程信息||消息 记录电批写入PLC数据(2)",
            "记录PLC交互流程信息||消息 记录进站位读取PLC触发数据",
            "记录PLC交互流程信息||消息 记录进站位写入PLC数据",
            "记录PLC交互流程信息||消息 记录压力曲线读取PLC触发数据(A工位)",
            "记录PLC交互流程信息||消息 记录压力曲线读取PLC触发数据(B工位)",
            "记录PLC交互流程信息||消息 记录压力曲线写入PLC数据(A工位)",
            "记录PLC交互流程信息||消息 记录压力曲线写入PLC数据(B工位)",
            "记录PLC交互流程信息||消息 记录PLC读写操作异常(PLC_出站位)",
            "记录PLC交互流程信息||消息 记录PLC读写操作异常(PLC_电批-1)",
            "记录PLC交互流程信息||消息 记录PLC读写操作异常(PLC_电批-2)",
            "记录PLC交互流程信息||消息 记录PLC读写操作异常(PLC_进站位)",
            "记录PLC交互流程信息||消息 记录PLC读写操作异常(PLC_其他)",
            "记录PLC交互流程信息||消息 记录PLC读写操作异常(PLC_压力曲线-A工位)",
            "记录PLC交互流程信息||消息 记录PLC读写操作异常(PLC_压力曲线-B工位)",
            "记录PLC交互流程信息||消息 记录PLC读写操作异常(PLC_Video_1)",
            "记录PLC交互流程信息||消息 记录PLC读写操作异常(PLC_Video_2)",
            "记录PLC交互流程信息||消息 记录Video录制读取PLC触发数据-Video_1",
            "记录PLC交互流程信息||消息 记录Video录制读取PLC触发数据-Video_2",
            "记录PLC交互流程信息||消息 记录Video录制写入PLC数据-Video_1",
            "记录PLC交互流程信息||消息 记录Video录制写入PLC数据-Video_2"
        };

        private const int MaximumProductionRecords = 1000;
        private const int MaximumAlarmRecords = 300;
        private const int MaximumLogRecords = 500;

        private readonly IValueStore values;
        private readonly string logRoot;
        private readonly LegacyEquipmentServices equipmentServices;
        private readonly object executionGate = new object();
        private readonly object stateGate = new object();
        private readonly List<EquipmentProductionRecord> inputRecords =
            new List<EquipmentProductionRecord>();
        private readonly List<EquipmentProductionRecord> outputRecords =
            new List<EquipmentProductionRecord>();
        private readonly List<EquipmentAlarmRecord> alarms = new List<EquipmentAlarmRecord>();
        private readonly List<string> logs = new List<string>();
        private readonly Dictionary<string, DateTime> inputStartedAt =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private long revision;
        private int inputTotal;
        private int outputTotal;
        private int goodTotal;
        private int defectTotal;
        private double? lastCycleSeconds;

        internal EquipmentProcessMessageService(
            IValueStore values,
            string logRoot = null,
            LegacyEquipmentServices equipmentServices = null)
        {
            this.values = values ?? throw new ArgumentNullException(nameof(values));
            this.logRoot = string.IsNullOrWhiteSpace(logRoot)
                ? Path.Combine(@"D:\AutomationLogs", "Hmi", "Equipment")
                : Path.GetFullPath(logRoot);
            this.equipmentServices = equipmentServices;
        }

        internal IReadOnlyList<string> GetRegisteredFunctionNames()
        {
            return RegisteredFunctionNames;
        }

        internal void ExecuteMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new InvalidOperationException("流程处理消息不能为空。");
            }

            lock (executionGate)
            {
                string normalizedMessage = message.Trim();
                AddLog("开始执行：" + normalizedMessage);
                try
                {
                    ParsedProcessMessage parsed = ParsedProcessMessage.Parse(normalizedMessage);
                    Dispatch(parsed);
                    AddLog("执行完成：" + normalizedMessage);
                }
                catch (Exception ex)
                {
                    AddAlarm(
                        TryReadString("SN_Code-进站位", out string sn) ? sn : string.Empty,
                        "流程消息执行",
                        ex.Message,
                        "检查消息名称、参数变量及平台原生资源配置。");
                    AddLog("执行失败：" + normalizedMessage + "；" + ex.Message);
                    throw new InvalidOperationException(
                        $"流程消息执行失败：{normalizedMessage}。{ex.Message}", ex);
                }
            }
        }

        internal EquipmentProcessMessageSnapshot GetSnapshot()
        {
            lock (stateGate)
            {
                return new EquipmentProcessMessageSnapshot
                {
                    Revision = revision,
                    InputRecords = inputRecords.Select(item => item.Clone()).ToList(),
                    OutputRecords = outputRecords.Select(item => item.Clone()).ToList(),
                    Alarms = alarms.Select(item => item.Clone()).ToList(),
                    Logs = logs.ToList(),
                    InputTotal = inputTotal,
                    OutputTotal = outputTotal,
                    GoodTotal = goodTotal,
                    DefectTotal = defectTotal,
                    LastCycleSeconds = lastCycleSeconds
                };
            }
        }

        internal void ResetCounters()
        {
            lock (stateGate)
            {
                inputRecords.Clear();
                outputRecords.Clear();
                inputStartedAt.Clear();
                inputTotal = 0;
                outputTotal = 0;
                goodTotal = 0;
                defectTotal = 0;
                lastCycleSeconds = null;
                revision++;
            }
            AddLog("操作员已重新计数。");
        }

        private void Dispatch(ParsedProcessMessage message)
        {
            if (string.Equals(message.Action, "消息 初始化", StringComparison.Ordinal))
            {
                AddLog("旧项目 CoreWork 消息适配器初始化完成。");
                return;
            }

            switch (message.Category)
            {
                case "主流程信息":
                    DispatchMain(message);
                    return;
                case "MES流程信息":
                    DispatchMes(message);
                    return;
                case "PDCA流程信息":
                    DispatchPdca(message);
                    return;
                case "HIVE流程信息":
                    DispatchHive(message);
                    return;
                case "Video操作信息":
                    DispatchVideo(message);
                    return;
                case "记录PLC交互流程信息":
                    DispatchPlcLog(message);
                    return;
                default:
                    throw new NotSupportedException($"未知的旧 CoreWork 消息类别“{message.Category}”。");
            }
        }

        private void DispatchMain(ParsedProcessMessage message)
        {
            switch (message.Action)
            {
                case "检测通讯对象":
                    DetectCommunication();
                    return;
                case "消息 SN去除符号并赋值":
                    TrimSerialNumber(message.Arguments);
                    return;
                case "消息 数据本地保存-进站位":
                    SaveInputRecord();
                    return;
                case "消息 显示进站结果-进站位":
                    ShowInputResult();
                    return;
                case "消息 数据本地保存-出站位":
                    SaveOutputRecord();
                    return;
                case "消息 显示出站结果-出站位":
                    ShowOutputResult();
                    return;
                case "消息 记录出站数据":
                    SaveRawOutputData();
                    return;
                case "消息 获取出站数据":
                    GatherOutputData();
                    return;
                case "消息 获取压力曲线数据":
                    ValidatePressureData(message.Arguments);
                    return;
                case "消息 绘制压力曲线":
                    AddLog($"压力曲线已就绪：{RequireArgument(message.Arguments[0], "工位")}。");
                    return;
                case "消息 清除电批缓存区数据":
                    ClearScrewBuffer(message.Arguments);
                    return;
                case "消息 电批数据本地保存-电批_1":
                    SaveScrewData(1);
                    return;
                case "消息 电批数据本地保存-电批_2":
                    SaveScrewData(2);
                    return;
                case "消息 绘制电批曲线":
                    PrepareScrewCurve(message.Arguments);
                    return;
                default:
                    throw new NotSupportedException($"未知的主流程消息“{message.Action}”。");
            }
        }

        private void DispatchMes(ParsedProcessMessage message)
        {
            switch (message.Action)
            {
                case "消息 进站MES查询":
                    ExecuteMesQuery();
                    return;
                case "消息 MES过站":
                    ExecuteMesPass();
                    return;
                case "消息 MES<-->PC心跳":
                    ExecuteMesHeartbeat();
                    return;
                default:
                    throw new NotSupportedException($"未知的 MES 消息“{message.Action}”。");
            }
        }

        private void DispatchPdca(ParsedProcessMessage message)
        {
            switch (message.Action)
            {
                case "消息 PDCA图片打包":
                    PackagePdcaImages();
                    return;
                case "消息 PDCA范围检测":
                    ValidatePdcaRange();
                    return;
                case "消息 PDCA数据收集":
                    CollectPdcaData();
                    return;
                case "消息 PDCA上传":
                    EvaluatePdcaUpload();
                    return;
                case "消息 转移PDCA文件并删除":
                    ArchivePdcaFiles();
                    return;
                default:
                    throw new NotSupportedException($"未知的 PDCA 消息“{message.Action}”。");
            }
        }

        private void DispatchHive(ParsedProcessMessage message)
        {
            switch (message.Action)
            {
                case "更新设备状态":
                    UpdateEquipmentState();
                    return;
                case "HIVE报警监控":
                    MonitorHiveAlarm();
                    return;
                case "HIVE设备状态监控":
                    AddLog("HIVE 设备状态监控：" + ReadOptionalString("设备状态", "0"));
                    return;
                default:
                    throw new NotSupportedException($"未知的 HIVE 消息“{message.Action}”。");
            }
        }

        private void DispatchVideo(ParsedProcessMessage message)
        {
            int channel = ReadMessageInteger(message.Arguments, 0, "视频通道");
            if (channel != 1 && channel != 2)
            {
                throw new InvalidOperationException("视频通道只支持 1 或 2。");
            }

            if (message.Action == "开始录制视频")
            {
                string resultVariable = message.Arguments.Count > 2
                    ? RequireArgument(message.Arguments[2], "录制结果变量")
                    : $"录制结果-Video_{channel}";
                if (ReadOptionalInteger("禁用Video", 0) == 1)
                {
                    SetRequiredValue(resultVariable, "OK");
                    AddLog($"Video_{channel} 已禁用，按旧项目规则跳过录制。");
                    return;
                }
                if (equipmentServices?.Video == null)
                {
                    SetOptionalValue(resultVariable, "NG");
                    throw new InvalidOperationException("旧项目视频服务未装载。");
                }
                string serialNumber = ReadArgumentVariable(message.Arguments, 1);
                if (!equipmentServices.Video.IsOpen(channel))
                {
                    string moniker = ReadRequiredString("摄像头设备" + channel);
                    if (!equipmentServices.Video.Open(channel, moniker, out string openError))
                    {
                        SetOptionalValue(resultVariable, "NG");
                        throw new InvalidOperationException(openError);
                    }
                }
                string recordPath = ResolveVideoPath(channel, serialNumber);
                if (!equipmentServices.Video.StartRecording(
                    channel,
                    recordPath,
                    out string recordError))
                {
                    SetOptionalValue(resultVariable, "NG");
                    throw new InvalidOperationException(recordError);
                }
                SetRequiredValue(resultVariable, "OK");
                AddLog(
                    $"Video_{channel} 开始录制，SN={serialNumber}，文件={recordPath}。");
                return;
            }

            if (message.Action == "结束录制视频")
            {
                if (equipmentServices?.Video == null)
                {
                    throw new InvalidOperationException("旧项目视频服务未装载。");
                }
                if (!equipmentServices.Video.StopRecording(channel, out string stopError))
                {
                    throw new InvalidOperationException(stopError);
                }
                AddLog(
                    $"Video_{channel} 结束录制，SN={ReadArgumentVariable(message.Arguments, 1)}。");
                return;
            }

            throw new NotSupportedException($"未知的视频消息“{message.Action}”。");
        }

        private string ResolveVideoPath(int channel, string serialNumber)
        {
            string template = TryReadString(
                "视频保存路径-Video_" + channel,
                out string configured)
                && !string.IsNullOrWhiteSpace(configured)
                    ? configured
                    : Path.Combine(
                        logRoot,
                        "Video",
                        "日期",
                        "Video_ID_SN_Code_时间.avi");
            string machineName = ReadOptionalString(
                "MachineName",
                ReadOptionalString("设备名称", "Automation"));
            string resolved = template
                .Replace(
                    "日期",
                    DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + Path.DirectorySeparatorChar
                    + SanitizePathToken(machineName))
                .Replace(
                    "时间",
                    DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture))
                .Replace("SN_Code", SanitizePathToken(serialNumber))
                .Replace("ID", channel.ToString(CultureInfo.InvariantCulture));
            if (string.IsNullOrWhiteSpace(Path.GetExtension(resolved)))
            {
                resolved += ".avi";
            }
            return Path.GetFullPath(resolved);
        }

        private void SaveInputToConfiguredDatabase(
            string sn,
            string mesResult,
            DateTime time)
        {
            LegacyDatabaseService database = equipmentServices?.Database;
            if (database == null || database.GetProfiles().Count == 0)
            {
                AddLog("未配置 MySQL，进站记录已按设备项目规则写入 CSV。");
                return;
            }
            if (!database.TrySaveInput(sn, mesResult, time, out string error))
            {
                throw new InvalidOperationException("进站记录写入 MySQL 失败：" + error);
            }
        }

        private void SaveOutputToConfiguredDatabase(
            string sn,
            string mesResult,
            string materialStatus,
            string outputData,
            DateTime time)
        {
            LegacyDatabaseService database = equipmentServices?.Database;
            if (database == null || database.GetProfiles().Count == 0)
            {
                AddLog("未配置 MySQL，出站记录已按设备项目规则写入 CSV。");
                return;
            }
            if (!database.TrySaveOutput(
                sn,
                mesResult,
                materialStatus,
                outputData,
                time,
                out string error))
            {
                throw new InvalidOperationException("出站记录写入 MySQL 失败：" + error);
            }
        }

        private static string SanitizePathToken(string value)
        {
            string result = value ?? string.Empty;
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalid, '_');
            }
            return string.IsNullOrWhiteSpace(result) ? "Unknown" : result.Trim();
        }

        private void DispatchPlcLog(ParsedProcessMessage message)
        {
            string text = message.Action;
            AddLog("PLC交互：" + text);
            TryAppendLine(
                Path.Combine(logRoot, "PLC", DateTime.Now.ToString("yyyyMMdd") + ".log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                + " " + text);

            if (text.Contains("异常"))
            {
                AddAlarm(
                    ReadOptionalString("产品信息(SN)", string.Empty),
                    ExtractParenthesized(text, "PLC交互"),
                    ReadOptionalString("报警信息", text),
                    "检查 PLC 连接、点位配置及本次读写返回值。");
            }
        }

        private void TrimSerialNumber(IReadOnlyList<string> arguments)
        {
            if (arguments.Count < 1 || arguments.Count > 2)
            {
                throw new InvalidOperationException(
                    "SN 去除符号消息格式应为：主流程信息||消息 SN去除符号并赋值(源变量,目标变量)。");
            }

            string sourceName = RequireArgument(arguments[0], "源变量");
            string targetName = arguments.Count == 2
                ? RequireArgument(arguments[1], "目标变量")
                : sourceName;
            string serialNumber = ReadRequiredString(sourceName).Trim();
            SetRequiredValue(sourceName, serialNumber);
            if (!string.Equals(sourceName, targetName, StringComparison.Ordinal))
            {
                SetRequiredValue(targetName, serialNumber);
            }
        }

        private void SaveInputRecord()
        {
            string sn = ReadRequiredString("SN_Code-进站位");
            string mesResult = TryReadString("MES查询备注-进站位", out string result)
                ? result
                : string.Empty;
            string info = "进站数据本地保存成功";
            DateTime now = DateTime.Now;

            try
            {
                AddProductionRecord(
                    inputRecords,
                    "Input",
                    new EquipmentProductionRecord
                    {
                        SN = sn,
                        Time = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        ProcessInfo = "数据本地保存-进站",
                        InfoData = string.IsNullOrWhiteSpace(mesResult)
                            ? info
                            : info + "；" + mesResult,
                        Mode = GetWorkModeText(),
                        IsFailure = false
                    });
                AppendCsv(
                    Path.Combine(
                        logRoot,
                        "Production",
                        now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                        + "_input_data.csv"),
                    "Time,SN,MesQueryResult",
                    now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    sn,
                    mesResult);
                SaveInputToConfiguredDatabase(sn, mesResult, now);
                SetRequiredValue("数据本地保存结果-进站位", 11);
                lock (stateGate)
                {
                    inputStartedAt[sn] = now;
                }
            }
            catch
            {
                SetOptionalValue("数据本地保存结果-进站位", 15);
                throw;
            }
        }

        private void ShowInputResult()
        {
            int dataResult = ReadRequiredInteger("数据本地保存结果-进站位");
            int mesResult = ReadRequiredInteger("MES查询结果-进站位");
            string sn = ReadRequiredString("SN_Code-进站位");
            string info;
            int plcResult;

            if (mesResult == 11 && dataResult == 11)
            {
                info = "进站OK";
                plcResult = 11;
            }
            else if (mesResult == 12)
            {
                info = "进站NG：MES网络连接超时";
                plcResult = 12;
            }
            else if (mesResult == 13)
            {
                info = "进站NG：MES查询反馈异常";
                plcResult = 13;
            }
            else if (mesResult == 14)
            {
                info = "进站NG：产品不在当前MES站点";
                plcResult = 14;
            }
            else if (dataResult == 15)
            {
                info = "进站NG：本地保存异常";
                plcResult = 15;
            }
            else if (dataResult == 16)
            {
                info = "进站NG：本地保存失败";
                plcResult = 16;
            }
            else
            {
                info = $"进站NG：MES结果={mesResult}，保存结果={dataResult}";
                plcResult = 12;
            }

            SetRequiredValue("PLC结果位-进站位", plcResult);
            AddProductionRecord(
                inputRecords,
                "Input",
                CreateProductionRecord(sn, "进站结果", info),
                countAsResult: true);
        }

        private void SaveOutputRecord()
        {
            string sn = ReadRequiredString("SN_Code-出站位");
            string rawData = TryReadString("出站数据-出站位", out string data)
                ? data
                : string.Empty;
            int materialStatus = ReadRequiredInteger("物料状态-出站位");
            string materialResult = GetMaterialStatusText(materialStatus);

            try
            {
                AddProductionRecord(
                    outputRecords,
                    "Output",
                    CreateProductionRecord(sn, "物料结果", materialResult));
                AddProductionRecord(
                    outputRecords,
                    "Output",
                    CreateProductionRecord(sn, "数据本地保存", "出站数据本地保存成功"));
                AppendCsv(
                    Path.Combine(logRoot, "Production", DateTime.Now.ToString("yyyyMMdd") + "_output_data.csv"),
                    "Time,SN,Data",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    sn,
                    rawData);
                SaveOutputToConfiguredDatabase(
                    sn,
                    ReadOptionalString("MES过站备注-出站位", string.Empty),
                    materialResult,
                    rawData,
                    DateTime.Now);
                SetRequiredValue("数据本地保存结果-出站位", 11);
                SetOptionalValue("数据本地保存备注-出站位", "出站数据本地保存成功");
            }
            catch
            {
                SetOptionalValue("数据本地保存结果-出站位", 12);
                SetOptionalValue("数据本地保存备注-出站位", "出站数据本地保存异常");
                throw;
            }
        }

        private void ShowOutputResult()
        {
            int mesResult = ReadRequiredInteger("MES过站结果-出站位");
            string sn = ReadRequiredString("SN_Code-出站位");
            string mesNote = TryReadString("MES过站备注-出站位", out string note)
                ? note
                : string.Empty;
            int workMode = ReadRequiredInteger("工作模式");
            string info;
            int plcResult;

            if (workMode == 1)
            {
                if (mesResult == 11)
                {
                    info = "出站OK";
                    plcResult = 11;
                }
                else
                {
                    info = string.IsNullOrWhiteSpace(mesNote)
                        ? $"出站NG：MES结果={mesResult}"
                        : "出站NG：" + mesNote;
                    plcResult = 12;
                }
            }
            else
            {
                info = "单机模式，出站结果默认OK";
                plcResult = 11;
            }

            SetRequiredValue("PLC结果位-出站位", plcResult);
            AddProductionRecord(
                outputRecords,
                "Output",
                CreateProductionRecord(sn, "出站结果", info),
                countAsResult: true);
        }

        private void SaveRawOutputData()
        {
            string sn = ReadRequiredString("SN_Code-出站位");
            string rawData = ReadRequiredString("出站数据-出站位");
            AppendCsv(
                Path.Combine(logRoot, "Production", DateTime.Now.ToString("yyyyMMdd") + "_raw_output.csv"),
                "Time,SN,Data",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                sn,
                rawData);
            AddLog($"已记录出站数据：SN={sn}。");
        }

        private void DetectCommunication()
        {
            string configuredTargets = ReadRequiredString("检测通讯对象IP");
            string[] parts = configuredTargets.Split(
                new[] { '[', ']', ',' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Length % 2 != 0)
            {
                throw new InvalidOperationException(
                    "“检测通讯对象IP”格式错误，应为 [名称,IP],[名称,IP]。");
            }

            for (int index = 0; index < parts.Length; index += 2)
            {
                string name = parts[index].Trim();
                string address = parts[index + 1].Trim();
                using (var ping = new Ping())
                {
                    PingReply reply = ping.Send(address, 120);
                    if (reply == null || reply.Status != IPStatus.Success)
                    {
                        throw new InvalidOperationException(
                            $"{name}（{address}）通讯断开，请检查网络连接。");
                    }
                }
            }
        }

        private void GatherOutputData()
        {
            var fields = new List<string>();
            AddOutputField(fields, "物料状态-出站位");
            AddOutputField(fields, "组装工位-出站位");
            if (fields.Count == 0)
            {
                throw new InvalidOperationException("没有找到可汇总的出站位变量。");
            }
            SetRequiredValue("出站数据-出站位", string.Join(";", fields) + ";");
            AddLog("出站数据汇总完成。");
        }

        private void AddOutputField(ICollection<string> fields, string variableName)
        {
            if (TryReadString(variableName, out string value))
            {
                fields.Add(variableName + ":" + value);
            }
        }

        private void ValidatePressureData(IReadOnlyList<string> arguments)
        {
            if (arguments.Count != 3)
            {
                throw new InvalidOperationException(
                    "压力曲线采集消息应包含工位、X 轴变量和 Y 轴变量。");
            }

            string station = RequireArgument(arguments[0], "工位");
            IReadOnlyList<double> x = ParseNumbers(ReadRequiredString(
                RequireArgument(arguments[1], "X 轴变量")));
            IReadOnlyList<double> y = ParseNumbers(ReadRequiredString(
                RequireArgument(arguments[2], "Y 轴变量")));
            if (x.Count == 0 || x.Count != y.Count)
            {
                throw new InvalidOperationException(
                    $"{station}压力曲线 X/Y 数据为空或长度不一致。");
            }
            SetOptionalValue($"PLC结果位-压力曲线_{station}", 11);
            AddLog($"{station}压力曲线采集完成，共 {x.Count} 点。");
        }

        private void ClearScrewBuffer(IReadOnlyList<string> arguments)
        {
            int communicationId = ReadMessageInteger(arguments, 0, "电批通讯对象 ID");
            int screwId = communicationId == 10003 ? 1
                : communicationId == 10004 ? 2
                : throw new InvalidOperationException(
                    $"未识别的电批通讯对象 ID：{communicationId}。");
            SetOptionalValue($"电批接受数据-电批_{screwId}", string.Empty);
            AddLog($"电批_{screwId}缓存区已清除。");
        }

        private void SaveScrewData(int screwId)
        {
            string sn = ReadRequiredString($"SN_Code-电批_{screwId}");
            string raw = ReadOptionalString($"电批接受数据-电批_{screwId}", string.Empty);
            AppendCsv(
                Path.Combine(
                    logRoot,
                    "ScrewData",
                    DateTime.Now.ToString("yyyyMMdd"),
                    sn + $"_Screw{screwId}.csv"),
                "Time,SN,Screw,RawData",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                sn,
                screwId.ToString(CultureInfo.InvariantCulture),
                raw);
            AddLog($"电批_{screwId}数据本地保存完成，SN={sn}。");
        }

        private void PrepareScrewCurve(IReadOnlyList<string> arguments)
        {
            if (arguments.Count != 3)
            {
                throw new InvalidOperationException(
                    "绘制电批曲线消息应包含 SN 变量、通讯对象 ID 和电批 ID。");
            }
            int screwId = ReadMessageInteger(arguments, 2, "电批 ID");
            string sn = ReadArgumentVariable(arguments, 0);
            string raw = ReadOptionalString($"电批接受数据-电批_{screwId}", string.Empty);
            if (ParseNumbers(raw).Count < 2
                && !TryReadDouble($"电批最大扭力-电批_{screwId}", out _))
            {
                SetOptionalValue($"PLC结果位-电批_{screwId}", 12);
                throw new InvalidOperationException($"电批_{screwId}没有可绘制的扭力数据。");
            }
            SetOptionalValue($"PLC结果位-电批_{screwId}", 11);
            AddLog($"电批_{screwId}扭力曲线已就绪，SN={sn}。");
        }

        private void ExecuteMesQuery()
        {
            string sn = ReadRequiredString("SN_Code-进站位");
            if (ReadRequiredInteger("工作模式") != 1)
            {
                SetRequiredValue("MES查询结果-进站位", 11);
                SetRequiredValue("MES查询备注-进站位", "检查结果：11(单机模式默认OK)");
            }
            else if (ReadOptionalInteger("禁用MES", 0) == 1)
            {
                SetRequiredValue("MES查询结果-进站位", 11);
                SetRequiredValue("MES查询备注-进站位", "检查结果：11(禁用MES默认OK)");
            }
            else
            {
                throw new InvalidOperationException(
                    "MES 已启用，但新平台尚未配置“MES查询”的通讯动作；不能伪造查询成功。");
            }

            AddProductionRecord(
                inputRecords,
                "Input",
                CreateProductionRecord(
                    sn,
                    "MES查询",
                    ReadRequiredString("MES查询备注-进站位")));
            AddLog("MES进站检查：" + ReadRequiredString("MES查询备注-进站位"));
        }

        private void ExecuteMesPass()
        {
            string sn = ReadRequiredString("SN_Code-出站位");
            if (ReadRequiredInteger("物料状态-出站位") != 1)
            {
                SetRequiredValue("MES过站结果-出站位", 12);
                SetRequiredValue("MES过站备注-出站位", "过站结果:12(物料NG不过PDCA和MES)");
            }
            else if (ReadRequiredInteger("工作模式") != 1)
            {
                SetRequiredValue("MES过站结果-出站位", 11);
                SetRequiredValue("MES过站备注-出站位", "过站结果:11(单机模式默认OK)");
            }
            else if (ReadOptionalInteger("禁用MES", 0) == 1)
            {
                SetRequiredValue("MES过站结果-出站位", 11);
                SetRequiredValue("MES过站备注-出站位", "过站结果:11(禁用MES默认OK)");
            }
            else if (string.Equals(
                ReadOptionalString("PDCA上传结果", string.Empty),
                "NG",
                StringComparison.OrdinalIgnoreCase))
            {
                SetRequiredValue("MES过站结果-出站位", 12);
                SetRequiredValue("MES过站备注-出站位", "过站结果:12(PDCA上传NG不过MES)");
            }
            else
            {
                throw new InvalidOperationException(
                    "MES 已启用，但新平台尚未配置“MES过站”的通讯动作；不能伪造过站成功。");
            }

            AddProductionRecord(
                outputRecords,
                "Output",
                CreateProductionRecord(
                    sn,
                    "MES过站",
                    ReadRequiredString("MES过站备注-出站位")));
            AddLog("MES出站过站：" + ReadRequiredString("MES过站备注-出站位"));
        }

        private void ExecuteMesHeartbeat()
        {
            if (ReadOptionalInteger("禁用MES", 0) == 1)
            {
                AddLog("MES 已禁用，跳过心跳。");
                return;
            }
            AddLog("MES 心跳由新平台通讯流程维护。");
        }

        private void PackagePdcaImages()
        {
            string sn = ReadRequiredPathToken("PDCA上传SN");
            string source = ResolveConfiguredPath(
                ReadRequiredString("PDCA图片集合路径"),
                sn);
            Directory.CreateDirectory(source);
            int minimumCount = Math.Max(0, ReadOptionalInteger("PDCA保存文件正常数量", 0));
            int actualCount = Directory.GetFiles(source).Length;
            if (actualCount < minimumCount)
            {
                SetRequiredValue("PDCA打包图片结果", "NG");
                AddAlarm(
                    sn,
                    "PDCA图片打包",
                    $"图片数量不足：{actualCount}/{minimumCount}",
                    "检查视觉图片保存结果及 PDCA 图片集合路径。");
                AddLog($"PDCA：SN={sn}，图片数量不足 {actualCount}/{minimumCount}。");
                return;
            }

            string destination = source + ".zip";
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
            System.IO.Compression.ZipFile.CreateFromDirectory(
                source,
                destination,
                System.IO.Compression.CompressionLevel.Optimal,
                false);
            SetRequiredValue("PDCA打包图片结果", "OK");
            SetOptionalValue("PDCA上传压缩文件名", destination);
            AddLog($"PDCA：SN={sn}，图片打包完成。");
        }

        private void ValidatePdcaRange()
        {
            // 原项目的上下限来自 PDCA Regiter.csv；新平台运行变量已承接检测结果。
            int result = ReadOptionalInteger("PDCA范围检测结果", 0);
            AddLog(result == 0 ? "PDCA：范围检测通过。" : "PDCA：范围检测存在超限项。");
            if (result != 0)
            {
                throw new InvalidOperationException("PDCA 范围检测存在超限项。");
            }
        }

        private void CollectPdcaData()
        {
            string raw = ReadRequiredString("出站数据-出站位");
            string[] fields = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string field in fields)
            {
                int separator = field.IndexOf(':');
                if (separator <= 0 || separator == field.Length - 1)
                {
                    continue;
                }
                string name = field.Substring(0, separator).Trim();
                string value = field.Substring(separator + 1).Trim();
                if (values.TryGet(name, out ValueSnapshot snapshot, out _) && snapshot != null)
                {
                    SetRequiredValue(name, value);
                }
            }
            AddLog("PDCA：出站数据收集完成。");
        }

        private void EvaluatePdcaUpload()
        {
            string sn = ReadRequiredPathToken("PDCA上传SN");
            if (ReadOptionalInteger("禁用PDCA", 0) == 1)
            {
                SetRequiredValue("PDCA上传结果", "OK");
                AddLog($"PDCA：SN={sn}，PDCA 已禁用，按旧项目调试规则跳过上传。");
                return;
            }
            if (string.Equals(
                ReadOptionalString("PDCA打包图片结果", string.Empty),
                "NG",
                StringComparison.OrdinalIgnoreCase))
            {
                SetRequiredValue("PDCA上传结果", "NG");
                throw new InvalidOperationException("PDCA 图片打包异常，不执行上传。");
            }
            if (ReadOptionalInteger("PDCA范围检测结果", 0) != 0)
            {
                SetRequiredValue("PDCA上传结果", "NG");
                throw new InvalidOperationException("PDCA 范围检测异常，不执行上传。");
            }

            string response = ReadOptionalString("PDCA接收Buff区", string.Empty);
            bool success = response.IndexOf("ok@{success}@", StringComparison.OrdinalIgnoreCase) >= 0
                || (response.IndexOf("ok@", StringComparison.OrdinalIgnoreCase) >= 0
                    && response.IndexOf("err", StringComparison.OrdinalIgnoreCase) < 0
                    && response.IndexOf("bad", StringComparison.OrdinalIgnoreCase) < 0);
            SetRequiredValue("PDCA上传结果", success ? "OK" : "NG");
            AddLog($"PDCA：SN={sn}，上传结果={(success ? "OK" : "NG")}；回复={response}");
            if (!success)
            {
                AddAlarm(sn, "PDCA上传", "PDCA上传失败", response);
                throw new InvalidOperationException("PDCA 上传回复未表示成功。");
            }
        }

        private void ArchivePdcaFiles()
        {
            string sn = ReadRequiredPathToken("PDCA上传SN");
            if (!string.Equals(
                ReadOptionalString("PDCA上传结果", string.Empty),
                "OK",
                StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"PDCA：SN={sn}，上传未成功，保留本地文件。");
                return;
            }

            string pathTemplate = ReadRequiredString("PDCA图片集合路径");
            if (!string.Equals(
                Path.GetFileName(pathTemplate.TrimEnd('\\', '/')),
                "SN",
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "PDCA 清理路径必须以独立的 SN 占位目录结尾。");
            }
            string source = ResolveConfiguredPath(pathTemplate, sn);
            if (Directory.Exists(source))
            {
                Directory.Delete(source, true);
            }
            AddLog($"PDCA：SN={sn}，上传成功，本地图片目录已清理。");
        }

        private void UpdateEquipmentState()
        {
            int systemStatus = ReadOptionalInteger("系统状态", 0);
            int equipmentState = systemStatus == 3 ? 1
                : systemStatus == 1 ? 2
                : systemStatus >= 4 ? 3
                : 0;
            SetOptionalValue("设备历史状态", ReadOptionalInteger("设备当前状态", 0));
            SetOptionalValue("设备当前状态", equipmentState);
            SetOptionalValue("设备状态", equipmentState);
            SetOptionalValue(
                "设备状态改变时间",
                DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)
                    .Replace(":", string.Empty));
            AddLog($"HIVE：设备状态更新为 {equipmentState}。");
        }

        private void MonitorHiveAlarm()
        {
            int alarmState = ReadOptionalInteger("报警状态", 0);
            if (alarmState == 0)
            {
                AddLog("HIVE：当前无活动报警。");
                return;
            }
            AddAlarm(
                ReadOptionalString("产品信息(SN)", string.Empty),
                ReadOptionalString("报警编码", "设备报警"),
                ReadOptionalString("报警信息", "设备报警"),
                ReadOptionalString("报警处理措施", string.Empty));
            AddLog("HIVE：活动报警已投影到主页面。");
        }

        private int ReadOptionalInteger(string name, int fallback)
        {
            return TryReadDouble(name, out double value)
                ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
                : fallback;
        }

        private string ReadOptionalString(string name, string fallback)
        {
            return TryReadString(name, out string value) ? value : fallback;
        }

        private string ReadRequiredPathToken(string name)
        {
            string value = ReadRequiredString(name).Trim();
            if (string.IsNullOrWhiteSpace(value)
                || value == "."
                || value == ".."
                || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || value.IndexOf('\\') >= 0
                || value.IndexOf('/') >= 0)
            {
                throw new InvalidOperationException(
                    $"变量“{name}”不是安全的单段文件名。");
            }
            return value;
        }

        private static int ReadMessageInteger(
            IReadOnlyList<string> arguments,
            int index,
            string displayName)
        {
            if (arguments == null || index < 0 || index >= arguments.Count
                || !int.TryParse(
                    arguments[index],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value))
            {
                throw new InvalidOperationException(displayName + "必须为整数。");
            }
            return value;
        }

        private string ReadArgumentVariable(IReadOnlyList<string> arguments, int index)
        {
            if (arguments == null || index < 0 || index >= arguments.Count)
            {
                throw new InvalidOperationException("消息缺少变量参数。");
            }
            return ReadRequiredString(RequireArgument(arguments[index], "变量名"));
        }

        private static string ExtractParenthesized(string value, string fallback)
        {
            int start = value.IndexOf('(');
            int end = value.LastIndexOf(')');
            return start >= 0 && end > start
                ? value.Substring(start + 1, end - start - 1)
                : fallback;
        }

        private static IReadOnlyList<double> ParseNumbers(string value)
        {
            var result = new List<double>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return result;
            }
            foreach (string token in value.Split(
                new[] { ',', ';', '|', '\r', '\n', '\t', ' ' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (double.TryParse(
                        token,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double number)
                    || double.TryParse(
                        token,
                        NumberStyles.Float,
                        CultureInfo.CurrentCulture,
                        out number))
                {
                    result.Add(number);
                }
            }
            return result;
        }

        private static string ResolveConfiguredPath(string template, string sn)
        {
            string resolved = template
                .Replace("日期", DateTime.Now.ToString("yyyy-MM-dd"))
                .Replace("SN", sn ?? string.Empty);
            string fullPath = Path.GetFullPath(resolved);
            string root = Path.GetPathRoot(fullPath);
            if (string.Equals(fullPath.TrimEnd('\\'), root?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("业务目录不能指向磁盘根目录。");
            }
            return fullPath;
        }

        private EquipmentProductionRecord CreateProductionRecord(
            string sn,
            string processInfo,
            string info)
        {
            return new EquipmentProductionRecord
            {
                SN = sn,
                Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ProcessInfo = processInfo,
                InfoData = info,
                Mode = GetWorkModeText(),
                IsFailure = IsFailureInfo(info)
            };
        }

        private void AddProductionRecord(
            List<EquipmentProductionRecord> target,
            string fileName,
            EquipmentProductionRecord record,
            bool countAsResult = false)
        {
            AppendCsv(
                Path.Combine(
                    logRoot,
                    "Production",
                    DateTime.Now.ToString("yyyyMMdd") + "_" + fileName + ".csv"),
                "Time,SN,ProcessInfo,InfoData,Mode",
                record.Time,
                record.SN,
                record.ProcessInfo,
                record.InfoData,
                record.Mode);

            lock (stateGate)
            {
                target.Add(record);
                TrimOldest(target, MaximumProductionRecords);
                if (countAsResult)
                {
                    if (ReferenceEquals(target, inputRecords))
                    {
                        inputTotal++;
                        if (!inputStartedAt.ContainsKey(record.SN))
                        {
                            inputStartedAt[record.SN] = DateTime.Now;
                        }
                    }
                    else
                    {
                        outputTotal++;
                        if (record.IsFailure)
                        {
                            defectTotal++;
                        }
                        else
                        {
                            goodTotal++;
                        }
                        UpdateCycleTime(record.SN);
                    }
                }
                revision++;
            }
        }

        private void UpdateCycleTime(string sn)
        {
            if (!string.IsNullOrWhiteSpace(sn)
                && inputStartedAt.TryGetValue(sn, out DateTime startedAt))
            {
                lastCycleSeconds = Math.Max(0, (DateTime.Now - startedAt).TotalSeconds);
                inputStartedAt.Remove(sn);
                SetOptionalValue("产品信息(CT)", lastCycleSeconds.Value);
                return;
            }

            if (TryReadDouble("产品信息(CT)", out double configuredCycle))
            {
                lastCycleSeconds = configuredCycle;
            }
        }

        private void AddAlarm(string sn, string position, string message, string resolution)
        {
            var alarm = new EquipmentAlarmRecord
            {
                Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                SN = sn ?? string.Empty,
                Position = position ?? string.Empty,
                Message = message ?? string.Empty,
                Resolution = resolution ?? string.Empty
            };
            lock (stateGate)
            {
                alarms.Add(alarm);
                TrimOldest(alarms, MaximumAlarmRecords);
                revision++;
            }
            TryAppendCsv(
                Path.Combine(logRoot, "Alarm", DateTime.Now.ToString("yyyyMMdd") + ".csv"),
                "Time,SN,Position,Message,Resolution",
                alarm.Time,
                alarm.SN,
                alarm.Position,
                alarm.Message,
                alarm.Resolution);
        }

        private void AddLog(string message)
        {
            string line = DateTime.Now.ToString(
                "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture) + " " + message;
            lock (stateGate)
            {
                logs.Add(line);
                TrimOldest(logs, MaximumLogRecords);
                revision++;
            }
            TryAppendLine(
                Path.Combine(logRoot, "Process", DateTime.Now.ToString("yyyyMMdd") + ".log"),
                line);
        }

        private string GetWorkModeText()
        {
            return TryReadDouble("工作模式", out double mode) && Math.Abs(mode - 1) < 0.000001
                ? "工单模式"
                : "单机模式";
        }

        private string ReadRequiredString(string name)
        {
            if (!values.TryGet(name, out ValueSnapshot snapshot, out string error)
                || snapshot == null)
            {
                throw new InvalidOperationException(
                    $"读取变量“{name}”失败：{NormalizeError(error)}");
            }
            return snapshot.Value ?? string.Empty;
        }

        private int ReadRequiredInteger(string name)
        {
            if (!TryReadDouble(name, out double value)
                || Math.Abs(value - Math.Round(value)) > 0.000001)
            {
                throw new InvalidOperationException($"变量“{name}”不是有效整数。");
            }
            return checked((int)Math.Round(value));
        }

        private bool TryReadString(string name, out string value)
        {
            value = null;
            if (!values.TryGet(name, out ValueSnapshot snapshot, out _)
                || snapshot == null)
            {
                return false;
            }
            value = snapshot.Value ?? string.Empty;
            return true;
        }

        private bool TryReadDouble(string name, out double value)
        {
            value = 0;
            return TryReadString(name, out string raw)
                && (double.TryParse(
                        raw,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out value)
                    || double.TryParse(
                        raw,
                        NumberStyles.Float,
                        CultureInfo.CurrentCulture,
                        out value));
        }

        private void SetRequiredValue(string name, object value)
        {
            if (!values.Set(name, value, out string error))
            {
                throw new InvalidOperationException(
                    $"写入变量“{name}”失败：{NormalizeError(error)}");
            }
        }

        private void SetOptionalValue(string name, object value)
        {
            if (!values.TryGet(name, out ValueSnapshot snapshot, out _)
                || snapshot == null)
            {
                return;
            }
            values.Set(name, value, out _);
        }

        private void AppendCsv(string path, string header, params string[] fields)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            bool writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                if (writeHeader)
                {
                    writer.WriteLine(header);
                }
                writer.WriteLine(string.Join(",", fields.Select(EscapeCsvField)));
            }
        }

        private void TryAppendCsv(string path, string header, params string[] fields)
        {
            try
            {
                AppendCsv(path, header, fields);
            }
            catch
            {
                // 报警落盘失败不覆盖原始流程异常；内存快照仍保留该报警。
            }
        }

        private void TryAppendLine(string path, string line)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch
            {
                // 运行日志是非致命辅助能力，不改变流程消息执行结果。
            }
        }

        private static string EscapeCsvField(string value)
        {
            string normalized = value ?? string.Empty;
            if (normalized.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return normalized;
            }
            return "\"" + normalized.Replace("\"", "\"\"") + "\"";
        }

        private static void TrimOldest<T>(List<T> items, int maximum)
        {
            if (items.Count > maximum)
            {
                items.RemoveRange(0, items.Count - maximum);
            }
        }

        private static bool IsFailureInfo(string info)
        {
            if (string.IsNullOrWhiteSpace(info))
            {
                return false;
            }
            return info.IndexOf("NG", StringComparison.OrdinalIgnoreCase) >= 0
                || info.Contains("异常")
                || info.Contains("失败")
                || info.Contains("无反馈");
        }

        private static string GetMaterialStatusText(int code)
        {
            return code == 1
                ? "1:合盖OK"
                : $"999:未配置NG项,接收到:{code}";
        }

        private static string RequireArgument(string argument, string displayName)
        {
            string value = argument?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(displayName + "不能为空。");
            }
            return value;
        }

        private static string NormalizeError(string error)
        {
            return string.IsNullOrWhiteSpace(error) ? "未提供错误详情" : error;
        }

        private sealed class ParsedProcessMessage
        {
            private ParsedProcessMessage()
            {
            }

            public string Category { get; private set; }
            public string Action { get; private set; }
            public IReadOnlyList<string> Arguments { get; private set; }

            public static ParsedProcessMessage Parse(string message)
            {
                string category = string.Empty;
                string payload = message;
                int categorySeparator = message.IndexOf("||", StringComparison.Ordinal);
                if (categorySeparator >= 0)
                {
                    category = message.Substring(0, categorySeparator).Trim();
                    payload = message.Substring(categorySeparator + 2).Trim();
                }

                string action = payload;
                var arguments = new List<string>();
                int argumentStart = payload.IndexOf('(');
                if (argumentStart >= 0)
                {
                    int argumentEnd = payload.LastIndexOf(')');
                    if (argumentEnd < argumentStart
                        || payload.Substring(argumentEnd + 1).Trim().Length != 0)
                    {
                        throw new InvalidOperationException("流程消息参数括号未正确闭合。");
                    }
                    action = payload.Substring(0, argumentStart).Trim();
                    string argumentText = payload.Substring(
                        argumentStart + 1,
                        argumentEnd - argumentStart - 1);
                    if (!string.IsNullOrWhiteSpace(argumentText))
                    {
                        arguments.AddRange(argumentText.Split(',').Select(item => item.Trim()));
                    }
                }

                if (string.IsNullOrWhiteSpace(action))
                {
                    throw new InvalidOperationException("流程消息动作不能为空。");
                }

                return new ParsedProcessMessage
                {
                    Category = category,
                    Action = action,
                    Arguments = arguments
                };
            }
        }
    }
}
