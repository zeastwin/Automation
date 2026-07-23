using Newtonsoft.Json.Linq;
// 模块：引擎 / 指令定义。
// 职责范围：维护原生指令类型、字段、行为和引用元数据的权威事实。

using System;
using System.Linq;
using System.Reflection;

namespace Automation
{
    /// <summary>
    /// 指令行为契约目录。Schema、指南和校验应共同使用这里的结构化规则，
    /// 避免把执行语义分别维护在提示词、界面转换器和校验代码中。
    /// </summary>
    public static class OperationBehaviorCatalog
    {
        public const int ContractVersion = 11;

        public static JObject BuildContract(OperationType operation)
        {
            if (operation == null)
            {
                return null;
            }

            JObject contract;
            switch (operation.OperaType)
            {
                case "回原":
                    contract = CreateContract(
                        "对目标工站的全部有效轴执行回零。",
                        new[] { "由 StationIndex 或 StationName 定位工站", "校验每个有效轴的卡、轴和回零参数", "按 StationHomeType 选择依次回零或同时回零", "ContinueWithoutWaiting=false 时等待全部轴回零且到位" },
                        true);
                    contract["constraints"] = new JArray("StationIndex 非 -1 时优先按索引定位，否则按 StationName", "同步等待使用120000毫秒固定超时", "异步模式只证明动作已下发，后续流程需自行等待或检测");
                    contract["failureModes"] = new JArray("工站、运动控制、轴配置或运动资源无效时报警", "同步等待超时时报警");
                    break;

                case "工站走点":
                    contract = CreateContract(
                        "以协调直线运动驱动目标工站的有效轴运动到预设点位。",
                        new[] { "按 StationName 定位工站", "由 PosIndex 或 PosName 定位点位", "按 IsDisableAxis 选择参与轴", "按 ChangeVel 选择工站默认参数或本次速度参数", "根据各轴位移与限制计算安全向量速度", "向工站坐标系一次下发绝对直线运动并按配置等待、超时和检测到位" },
                        true);
                    AddRequiredField(contract, "StationName", "目标工站精确名称");
                    contract["constraints"] = new JArray("参与轴必须位于同一张控制卡", "工站 CoordinateSystem 必须为控制器支持的坐标系", "PosIndex 非 -1 时优先按索引定位，否则按 PosName", "ChangeVel=改变速度时 Vel/Acc/Dec 分别使用大于0的固定值或对应变量且范围1..100", "ContinueWithoutWaiting=true 时不等待完成", "CheckInPosition 仅在等待模式完成后检查轴到位");
                    contract["failureModes"] = new JArray("工站、点位、同卡约束、坐标系、轴配置、速度、超时或运动资源无效时报警", "组运动启动失败、等待超时或未到位时报警并停止整组");
                    break;

                case "点位修改":
                    contract = CreateContract(
                        "以预设点、当前位置或自定义坐标为参考修改目标工站点位。",
                        new[] { "按 StationName 定位工站与 TargetPosName", "按 RefPosName 读取预设点、当前位置或 CustomX..CustomW", "按 ModifyType 覆盖或叠加目标坐标", "更新工站点位模型" },
                        true);
                    AddRequiredField(contract, "StationName", "目标工站精确名称");
                    AddRequiredField(contract, "RefPosName", "参考点名称、当前位置或自定义坐标");
                    AddRequiredField(contract, "TargetPosName", "被修改的目标点位精确名称");
                    AddRequiredField(contract, "ModifyType", "目标点位的修改方式");
                    contract["failureModes"] = new JArray("工站、参考点、目标点、修改方式或轴配置无效时报警", "读取当前位置失败时报警");
                    break;

                case "获取工站位置":
                    contract = CreateContract(
                        "读取工站当前位置或指定点位坐标，并保存到点位或变量。",
                        new[] { "按 StationName 定位工站", "按 SourceType 读取当前位置或 SourcePosName", "按 SaveType 写入 TargetPosName 或 OutputX..OutputW 变量引用" },
                        true);
                    AddRequiredField(contract, "StationName", "目标工站精确名称");
                    AddRequiredField(contract, "SourceType", "当前位置或指定点位");
                    AddRequiredField(contract, "SaveType", "保存到点位或保存到变量");
                    AddConditionalField(contract, "SourcePosName", "SourceType", new[] { "指定点位" }, "坐标来源点位");
                    AddConditionalField(contract, "TargetPosName", "SaveType", new[] { "保存到点位" }, "坐标写入点位");
                    contract["constraints"] = new JArray("保存到变量时 X..W 至少配置一个有效目标变量", "当前位置模式只保存工站中已配置的有效轴");
                    contract["failureModes"] = new JArray("工站、来源、目标点位或变量引用无效时报警", "读取运动位置或写入结果失败时报警");
                    break;

                case "创建料盘":
                    contract = CreateContract(
                        "根据工站四个参考点生成并缓存规则料盘的全部点位。",
                        new[] { "按 StationName 定位工站", "读取 PX1、PX2、PY1、PY2 四个参考点", "按 RowCount 与 ColCount 进行线性或双线性插值", "以 StationName 和 TrayId 保存料盘点位网格" },
                        true);
                    AddRequiredField(contract, "StationName", "料盘所属工站精确名称");
                    AddRequiredField(contract, "PX1", "左上参考点名称");
                    AddRequiredField(contract, "PX2", "右上参考点名称");
                    AddRequiredField(contract, "PY1", "左下参考点名称");
                    AddRequiredField(contract, "PY2", "右下参考点名称");
                    contract["constraints"] = new JArray("RowCount 和 ColCount 必须大于0", "TrayId 必须大于等于0", "四个参考点必须属于同一工站且包含六轴坐标");
                    contract["failureModes"] = new JArray("工站、参考点、行列数或料盘ID无效时报警", "点位数量溢出或缓存失败时报警");
                    break;

                case "走料盘点":
                    contract = CreateContract(
                        "驱动目标工站运动到已缓存料盘中的指定点位。",
                        new[] { "按 StationName 定位工站", "分别从固定值或变量引用解析 TrayId 与 TrayPos", "读取已缓存料盘网格和目标点", "对有效轴下发绝对运动", "ContinueWithoutWaiting=false 时等待完成" },
                        true);
                    AddRequiredField(contract, "StationName", "料盘所属工站精确名称");
                    contract["constraints"] = new JArray("TrayId 固定值与 TrayIdValue* 变量引用二选一", "TrayPos 固定值与 TrayPosValue* 变量引用二选一且最终位置大于0", "料盘必须先由创建料盘指令生成", "ContinueWithoutWaiting=true 时不等待运动完成");
                    contract["failureModes"] = new JArray("料盘号、位置、缓存、工站、轴配置或运动资源无效时报警", "同步运动失败或等待超时时报警");
                    break;

                case "偏移量":
                    contract = CreateContract(
                        "按六轴固定偏移量或变量偏移量驱动工站执行协调直线相对运动。",
                        new[] { "按 StationName 定位工站", "逐个有效轴从 Axis1..Axis6 固定值或 Axis1V..Axis6V 变量取得偏移", "按 ChangeVel 选择速度参数", "根据各轴偏移与限制计算安全向量速度", "向工站坐标系一次下发相对直线运动并按配置等待、超时和检测到位" },
                        true);
                    AddRequiredField(contract, "StationName", "目标工站精确名称");
                    contract["constraints"] = new JArray("参与轴必须位于同一张控制卡", "工站 CoordinateSystem 必须为控制器支持的坐标系", "每个轴固定偏移为0时可从对应变量读取", "ChangeVel=改变速度时 Vel/Acc/Dec 使用固定值或变量且最终范围1..100", "ContinueWithoutWaiting=true 时不等待完成");
                    contract["failureModes"] = new JArray("工站、同卡约束、坐标系、轴、偏移、速度、超时或运动资源无效时报警", "组运动启动失败、等待超时或未到位时报警并停止整组");
                    break;

                case "设置速度":
                    contract = CreateContract(
                        "设置目标工站全部轴或单个轴后续运动使用的速度、加速和减速百分比。",
                        new[] { "由 StationIndex 或 StationName 定位工站", "分别从 Vel/Acc/Dec 固定值或对应变量解析参数", "按 SetAxisObj 写入整站有效轴或指定轴的运动参数" },
                        true);
                    AddRequiredField(contract, "SetAxisObj", "工站或精确轴名称");
                    contract["constraints"] = new JArray("StationIndex 非 -1 时优先按索引定位，否则按 StationName", "Vel、Acc、Dec 最终值都必须在1..100", "固定值为0时从对应变量读取");
                    contract["failureModes"] = new JArray("工站、轴、变量或百分比无效时报警");
                    break;

                case "停止运动":
                    contract = CreateContract(
                        "停止目标工站全部有效轴或明确选择的轴，并等待停止完成。",
                        new[] { "按 StationName 定位工站", "StopEntireStation=true 时停止整站，否则按 Axis1..Axis6 选择轴", "获取运动资源并发出停止", "等待轴停止" },
                        true);
                    AddRequiredField(contract, "StationName", "目标工站精确名称");
                    contract["constraints"] = new JArray("StopEntireStation=false 时至少应选择一个有效轴", "停止操作使用30000毫秒等待边界");
                    contract["failureModes"] = new JArray("工站、轴配置或运动资源无效时报警", "停止失败或等待超时时报警");
                    break;

                case "等待运动":
                    contract = CreateContract(
                        "等待目标工站全部有效轴停止，或同时满足停止、回零和到位状态。",
                        new[] { "由 StationIndex 或 StationName 定位工站", "解析固定 TimeoutMs 或 TimeoutVariableName", "轮询全部有效轴", "按 WaitForHomeCompleted 决定是否同时要求回零完成" },
                        true);
                    contract["constraints"] = new JArray("StationIndex 非 -1 时优先按索引定位，否则按 StationName", "超时最终值必须大于0毫秒", "WaitForHomeCompleted=true 时要求轴停止、回零完成且到位");
                    contract["failureModes"] = new JArray("工站、轴、超时变量或运动状态无效时报警", "等待超时时报警");
                    break;

                case "自定义方法":
                    contract = CreateContract(
                        "调用设备工程通过平台公开接口注册的无参数自定义函数。",
                        new[] { "按 Name 精确查找当前进程已注册函数", "同步执行函数", "函数返回成功后继续下一条" },
                        true);
                    AddRequiredField(contract, "FunctionName", "已注册自定义函数精确名称");
                    contract["constraints"] = new JArray("函数必须由 HMI 通过 IAutomationPlatform.RegisterCustomFunction 注册", "函数容器未初始化或名称不存在时报警");
                    contract["failureModes"] = new JArray("自定义函数服务未初始化时报警", "函数名称不存在或函数返回失败时报警");
                    break;

                case "逻辑判断":
                    contract = CreateContract(
                        "组合多个数值或字符条件，并根据最终结果跳转。",
                        new[] { "按条件列表顺序读取变量并计算", "使用且/或组合结果", "成立时跳转 TrueGoto，不成立时按 InvalidDelayMs 延时后跳转 FalseGoto" },
                        false);
                    AddRequiredGoto(contract, "TrueGoto", "条件成立时的跳转目标");
                    AddRequiredGoto(contract, "FalseGoto", "条件不成立时的跳转目标；若需继续下一条，也必须明确指向下一条指令");
                    contract["judgeModes"] = new JObject
                    {
                        ["值在区间左"] = "IncludeBoundary=true: value <= Down；IncludeBoundary=false: value < Down；Up 不参与该模式",
                        ["值在区间右"] = "IncludeBoundary=true: value >= Down；IncludeBoundary=false: value > Down；Up 不参与该模式",
                        ["值在区间内"] = "IncludeBoundary=true: Down <= value <= Up；IncludeBoundary=false: Down < value < Up",
                        ["等于特征字符"] = "变量文本与 ExpectedText 做区分大小写的完全相等比较"
                    };
                    contract["failureModes"] = new JArray("条件变量无效时报警", "InvalidDelayMs 为负数时报警", "任一跳转目标为空或无效时报警");
                    break;

                case "IO逻辑跳转":
                    contract = CreateContract(
                        "组合多个输入 IO 状态，并根据逻辑结果跳转。",
                        new[] { "读取 IoParams 中的输入 IO 状态", "使用且/或组合结果", "为真跳转 TrueGoto，为假按 InvalidDelayMs 延时后跳转 FalseGoto" },
                        false);
                    AddRequiredGoto(contract, "TrueGoto", "IO 逻辑为真时的跳转目标");
                    AddRequiredGoto(contract, "FalseGoto", "IO 逻辑为假时的跳转目标");
                    contract["constraints"] = new JArray("IoParams 只接受通用输入 IO");
                    contract["failureModes"] = new JArray("输入 IO 名称或类型无效时报警", "跳转目标为空或无效时报警");
                    break;

                case "IO组":
                    contract = CreateContract(
                        "同步切换同一卡的一组输出 IO，再等待一组输入 IO 全部达到目标状态。",
                        new[] { "校验 OutIoParams 全部为同一卡通用输出", "通过一次端口写入同步切换全部输出", "校验 CheckIoParams 全部为通用输入", "在 Timeout 边界内等待全部输入成立" },
                        true);
                    AddRequiredField(contract, "OutIoParams", "输出 IO 动作集合");
                    AddRequiredField(contract, "CheckIoParams", "输入 IO 检测集合");
                    AddRequiredField(contract, "Timeout", "检测阶段固定超时或超时变量");
                    contract["constraints"] = new JArray("输出阶段完整成功后才进入检测阶段", "输出项只接受同一卡且端口索引为0..31的通用输出", "检测项只接受通用输入", "检测条件按全部成立处理");
                    contract["failureModes"] = new JArray("任一 IO 名称、类型、卡号、端口索引或批量写入无效时报警", "检测超时时报警");
                    break;

                case "IO操作":
                    contract = CreateContract(
                        "通过一次端口写入同步切换同一卡的一个或多个输出IO。",
                        new[] { "按精确名称解析全部输出IO", "校验全部输出位于同一卡且端口索引唯一有效", "合成端口目标值", "一次下发整端口写入" },
                        true);
                    AddRequiredField(contract, "IoParams", "同一卡输出IO动作集合；每项包含IoName和value");
                    contract["stateSemantics"] = "value 是该精确输出IO的运行时逻辑目标值，不统一表示机构安全位、工作位或物理高低电平";
                    contract["constraints"] = new JArray(
                        "每个IoName必须映射到可写输出IO",
                        "全部输出必须位于同一张控制卡且IOIndex在0..31内不重复",
                        "全部目标状态通过一次dmc_write_outport调用提交");
                    contract["semanticKinds"] = new JObject { ["sameCardBatchOutput"] = "io.write" };
                    contract["failureModes"] = new JArray("IO服务未初始化时报警", "IO映射、类型、卡号或端口索引无效时报警", "IO批量写入失败时报警");
                    break;

                case "IO检测":
                    contract = CreateContract(
                        "在超时边界内等待一组输入或输出IO全部达到各自的运行时逻辑目标值。",
                        new[] { "解析固定超时或超时变量", "按精确名称读取每个IO的运行时逻辑状态", "全部条件同时成立时正常完成", "未全部成立时继续轮询，达到超时边界后进入报警策略" },
                        true);
                    AddRequiredField(contract, "IoParams", "IO条件集合；每项包含IoName和期望运行时逻辑值value");
                    AddRequiredField(contract, "Timeout", "固定超时或超时变量；运行时解析结果必须大于0毫秒");
                    contract["stateSemantics"] = "value 是该精确IO的期望运行时逻辑值；机构到位可由目标反馈与必要的对向反馈共同组成条件集合";
                    contract["controlFlow"]["failurePath"] = "超时、映射缺失、类型无效和读取失败均进入当前指令的AlarmType策略；AlarmType=自动处理时通过Goto1进入恢复目标";
                    contract["semanticKinds"] = new JObject { ["waitAll"] = "io.wait" };
                    contract["failureModes"] = new JArray("条件集合为空时报警", "超时配置无效时报警", "IO映射不存在或类型不是通用输入/通用输出时报警", "IO读取失败时报警", "条件在超时边界内未全部成立时报警");
                    break;

                case "跳转":
                    contract = CreateContract(
                        "读取变量并匹配分支，命中后跳转；全部未命中时使用默认跳转。",
                        new[] { "读取 ValueIndex/ValueName 指定的变量", "依次比较 Params 中的匹配值", "命中时跳转到该项 Goto", "未命中时跳转 DefaultGoto" },
                        false);
                    AddGotoRule(contract, "DefaultGoto", false, "未匹配任何分支时的目标；为空且没有分支命中时运行会报警");
                    contract["constraints"] = new JArray("固定匹配值与匹配值变量互斥", "每个分支的 Goto 必须有效", "DefaultGoto 可以为空，但仅当运行时保证存在匹配分支；否则会报警");
                    contract["failureModes"] = new JArray("待匹配变量为空或不存在时报警", "匹配值配置冲突时报警", "未命中且 DefaultGoto 为空时报警");
                    break;

                case "流程操作":
                    contract = CreateContract(
                        "按顺序启动或停止一个或多个目标流程。",
                        new[] { "每项从 ProcName 或 ProcValue 解析目标流程", "value=运行时启动非活动流程，value=停止时停止活动流程", "执行该项操作后等待配置延时" },
                        true);
                    AddRequiredField(contract, "Params", "流程操作集合；每项明确目标来源、运行或停止动作及后延时");
                    contract["constraints"] = new JArray("ProcName 与 ProcValue 表达同一个目标来源，运行时名称优先", "当前流程不能启动自身", "启动目标必须处于 Ready 或 Stopped，停止目标必须处于活动状态");
                    contract["failureModes"] = new JArray("目标流程不存在或状态不允许时报警", "启动流程失败或延时变量无效时报警");
                    break;

                case "等待流程状态":
                    contract = CreateContract(
                        "按工作模式等待目标流程、依据状态跳转，或把完整状态写入变量。",
                        new[] { "等待模式逐项从 Params 的 ProcName 或 ProcValue 解析目标", "等待模式循环检查每项的运行或就绪条件", "状态跳转模式把单一目标当前状态映射到三个互斥分支", "获取状态模式把单一目标枚举状态写入变量" },
                        !(operation is WaitProc waitOperation)
                            || waitOperation.WorkMode != WaitProc.StateJumpMode);
                    AddRequiredField(contract, nameof(WaitProc.WorkMode), "等待就绪、状态跳转或获取状态；同一时刻只执行一个模式");
                    AddConditionalField(contract, nameof(WaitProc.Params), nameof(WaitProc.WorkMode),
                        new[] { WaitProc.WaitReadyMode }, "等待目标集合；每个目标分别选择运行或就绪");
                    AddConditionalField(contract, nameof(WaitProc.Timeout), nameof(WaitProc.WorkMode),
                        new[] { WaitProc.WaitReadyMode }, "固定超时或超时变量；解析结果必须大于0毫秒");
                    ((JObject)contract["fieldRules"])[nameof(WaitProc.DelayAfterMs)] = new JObject
                    {
                        ["visibleWhen"] = new JObject
                        {
                            [nameof(WaitProc.WorkMode)] = new JArray(WaitProc.WaitReadyMode)
                        },
                        ["description"] = "等待条件满足后的固定延时"
                    };
                    ((JObject)contract["fieldRules"])[nameof(WaitProc.DelayAfterVariableName)] = new JObject
                    {
                        ["visibleWhen"] = new JObject
                        {
                            [nameof(WaitProc.WorkMode)] = new JArray(WaitProc.WaitReadyMode)
                        },
                        ["description"] = "固定后延时小于等于0时使用的延时变量"
                    };
                    string[] singleTargetModes = { WaitProc.StateJumpMode, WaitProc.GetStateMode };
                    ((JObject)contract["fieldRules"])[nameof(WaitProc.TargetProcName)] = new JObject
                    {
                        ["visibleWhen"] = new JObject
                        {
                            [nameof(WaitProc.WorkMode)] = new JArray(singleTargetModes)
                        },
                        ["requiredAnyForRun"] = new JArray(nameof(WaitProc.TargetProcName), nameof(WaitProc.TargetProcValue)),
                        ["description"] = "单一目标流程名称；与目标流程变量至少配置一个，名称优先"
                    };
                    ((JObject)contract["fieldRules"])[nameof(WaitProc.TargetProcValue)] = new JObject
                    {
                        ["visibleWhen"] = new JObject
                        {
                            [nameof(WaitProc.WorkMode)] = new JArray(singleTargetModes)
                        },
                        ["requiredAnyForRun"] = new JArray(nameof(WaitProc.TargetProcName), nameof(WaitProc.TargetProcValue)),
                        ["description"] = "单一目标流程名称来源变量；变量类型必须是string"
                    };
                    AddConditionalGotoRule((JObject)contract["fieldRules"], nameof(WaitProc.ReadyGoto),
                        nameof(WaitProc.WorkMode), new[] { WaitProc.StateJumpMode }, "目标自然完成并处于Ready时的跳转目标");
                    AddConditionalGotoRule((JObject)contract["fieldRules"], nameof(WaitProc.AbnormalGoto),
                        nameof(WaitProc.WorkMode), new[] { WaitProc.StateJumpMode }, "目标处于Alarming或Stopped时的跳转目标");
                    AddConditionalGotoRule((JObject)contract["fieldRules"], nameof(WaitProc.RunningGoto),
                        nameof(WaitProc.WorkMode), new[] { WaitProc.StateJumpMode }, "目标处于运行、暂停或过渡状态时的跳转目标");
                    AddConditionalField(contract, nameof(WaitProc.StateVariableName), nameof(WaitProc.WorkMode),
                        new[] { WaitProc.GetStateMode }, "状态输出变量；double写枚举数值，string写枚举名称");
                    contract["constraints"] = new JArray(
                        "三个工作模式互斥，非当前模式字段不会参与运行或持久化",
                        "等待就绪模式的每个目标只允许选择运行或就绪，不允许把停止当作等待目标",
                        "等待多个目标时每项可以选择不同状态，所有目标同时满足才完成",
                        "状态跳转和获取状态使用一个单一目标，TargetProcName与TargetProcValue表达同一来源且名称优先",
                        "状态跳转把Ready映射为就绪、Alarming/Stopped映射为异常，其余活动或过渡状态映射为运行中",
                        "获取状态完整输出ProcRunState；double映射为Stopped=0、Paused=1、SingleStep=2、Running=3、Alarming=4、Pausing=5、Stopping=6、Ready=7，string为枚举英文名称");
                    contract["failureModes"] = new JArray("目标流程不存在、模式字段不完整、变量类型不符、跳转无效、超时配置无效或等待超时时报警");
                    break;

                case "CT探针":
                    contract = CreateContract(
                        "在显式业务位置记录分段CT和自周期起点以来的累计CT。",
                        new[] { "按当前流程runId与TaskKey定位计时任务", "StartNewCycle=true时用单调时钟重置周期", "后续探针计算距上一探针和周期起点的耗时", "按需写入double结果变量", "同步发布内存事件并更新最新样本" },
                        true);
                    AddRequiredField(contract, "TaskKey", "同一流程运行内计时任务的稳定标识");
                    AddRequiredField(contract, "SegmentName", "当前业务分段名称");
                    contract["constraints"] = new JArray("每个TaskKey必须先执行StartNewCycle=true的探针", "结果变量可省略；配置时必须是当前流程可访问的double变量", "使用Stopwatch单调时钟，不依赖系统时间调整", "探针不在执行热路径同步写文件");
                    contract["failureModes"] = new JArray("任务标识或分段名称为空时报警", "未先开始周期时报警", "结果变量不存在、类型不符或写入失败时报警");
                    break;

                case "延时":
                    contract = CreateContract(
                        "按固定毫秒值或变量值暂停当前流程执行。",
                        new[] { "DelayMs 有值时直接使用强类型毫秒值", "否则从 DelayVariableName 读取毫秒值", "等待完成后继续下一条" },
                        true);
                    contract["constraints"] = new JArray("延时值必须是非负整数", "DelayMs 与 DelayVariableName 只能选择一种", "两种来源都为空时延时0毫秒");
                    contract["failureModes"] = new JArray("固定延时为负数或两种来源同时配置时报警", "延时变量不存在或其值不是非负整数时报警");
                    break;

                case "修改变量":
                    contract = CreateContract(
                        "读取源变量，用固定值或另一个变量执行运算，并写入结果变量。",
                        new[] { "读取 ValueSource* 指定的源变量", "从 ChangeValue 固定值或 ChangeValue* 变量中二选一取得操作数", "按 ModifyType 计算", "写入 OutputValue* 指定的结果变量" },
                        true);
                    contract["constraints"] = new JArray(
                        "ValueSourceIndex/ValueSourceName 等源引用必须且只能形成一个有效引用",
                        "ChangeValue 固定值与 ChangeValueIndex/ChangeValueName 等变量引用必须且只能选择一种",
                        "OutputValueIndex/OutputValueName 等结果引用必须且只能形成一个有效引用",
                        "叠加、乘法、除法、求余和绝对值要求源变量与结果变量为 double；非绝对值操作数也必须为 double",
                        "除法和求余的固定操作数不能为0");
                    contract["semanticKinds"] = new JObject
                    {
                        ["fixedSet"] = "variable.set",
                        ["fixedAdd"] = "variable.add",
                        ["variableOrNumericCompute"] = "variable.compute"
                    };
                    contract["failureModes"] = new JArray(
                        "源变量、修改值或结果变量缺失时报警",
                        "固定修改值与修改值变量同时配置时报警",
                        "数值模式的变量类型或固定数值无效时报警");
                    break;

                case "获取变量":
                    contract = CreateContract(
                        "按映射集合把一个或多个源变量的当前值复制到目标变量。",
                        new[] { "逐项解析源 ValueSource* 引用", "读取源变量当前值", "解析目标 OutputValue* 引用", "保持平台变量值语义写入目标" },
                        true);
                    AddRequiredField(contract, "Params", "变量复制映射集合");
                    contract["constraints"] = new JArray("每项源引用和目标引用各自只能形成一个有效变量引用", "多项按 Params 顺序执行");
                    contract["failureModes"] = new JArray("源或目标变量引用无效时报警", "目标变量写入失败时报警");
                    break;

                case "数据拼接":
                    contract = CreateContract(
                        "使用格式模板和按顺序读取的变量生成字符串并写入结果变量。",
                        new[] { "读取 Format", "按 Params 顺序读取格式参数变量", "调用字符串格式化", "写入 OutputValueIndex 或 OutputValueName 指定的目标变量" },
                        true);
                    AddRequiredField(contract, "Format", "符合 .NET 复合格式语法的格式模板");
                    AddRequiredField(contract, "Params", "格式参数变量集合");
                    contract["constraints"] = new JArray("模板占位序号必须落在 Params 范围内", "结果变量引用必须唯一有效");
                    contract["failureModes"] = new JArray("格式模板、参数变量或结果变量无效时报警");
                    break;

                case "字符串分割":
                    contract = CreateContract(
                        "按分隔符切分源字符串，并从指定结果位置开始写入连续变量。",
                        new[] { "读取源字符串变量", "按 SplitStr 分割", "从 StartIndex 指定的分割结果开始", "将 Count 个结果写入从目标变量开始的连续变量" },
                        true);
                    contract["constraints"] = new JArray("StartIndex 必须是非负整数", "Count 有值时必须是非负整数；为 null 时使用分割结果总数", "起始位置加数量不得超过分割结果长度", "源和首个目标变量引用必须有效");
                    contract["failureModes"] = new JArray("分隔符、索引、数量或变量引用无效时报警", "结果范围或连续目标变量越界时报警");
                    break;

                case "字符串替换":
                    contract = CreateContract(
                        "按配置使用内容替换或位置替换生成新字符串并写入结果变量。",
                        new[] { "读取 SourceValue* 源字符串", "按 ReplaceType 解析待替换内容、替换文本或位置范围", "执行替换", "写入 ResultValue* 目标变量" },
                        true);
                    contract["constraints"] = new JArray("固定文本与变量文本来源按字段契约选择", "位置模式的 StartIndex 和 Count 必须是非负整数且范围有效", "结果变量引用必须唯一有效");
                    contract["failureModes"] = new JArray("来源、模式、范围或结果变量无效时报警");
                    break;

                case "弹框":
                    contract = CreateContract(
                        "显示交互弹框，根据按钮结果跳转。",
                        new[] { "根据 InfoType 解析提示内容", "按 PopupType 显示一至三个按钮", "所选按钮的 PopupGoto 非空时跳转，为空时顺序执行下一条" },
                        true);
                    contract["controlFlow"]["description"] = "按钮对应的 PopupGoto 非空时显式跳转；为空时顺序执行下一条。位于流程末尾时会自然结束并进入 Ready。";
                    AddConditionalGoto(contract, "PopupGoto1", "PopupType", new[] { "弹是", "弹是与否", "弹是与否与取消" }, "按钮1可选跳转目标；为空时顺序执行下一条");
                    AddConditionalGoto(contract, "PopupGoto2", "PopupType", new[] { "弹是与否", "弹是与否与取消" }, "按钮2可选跳转目标；为空时顺序执行下一条");
                    AddConditionalGoto(contract, "PopupGoto3", "PopupType", new[] { "弹是与否与取消" }, "按钮3可选跳转目标；为空时顺序执行下一条");
                    AddConditionalField(contract, "PopupMessage", "InfoType", new[] { "自定义提示信息" }, "固定提示文本");
                    AddConditionalField(contract, "PopupMessageValue", "InfoType", new[] { "变量类型" }, "提示内容变量");
                    AddConditionalField(contract, "PopupAlarmInfoId", "InfoType", new[] { "报警信息库" }, "报警信息编号");
                    contract["failureModes"] = new JArray("提示内容或可见按钮文本为空时报警", "提示变量或报警信息不存在时报警", "非空跳转地址无效时报警");
                    break;

                case "设置结构体数据项":
                    contract = CreateContract(
                        "把 Params 中的固定值写入指定结构体项的指定字段。",
                        new[] { "UseNameAddressing=false 时严格按 StructIndex/ItemIndex/FieldIndex 寻址", "UseNameAddressing=true 时严格按 StructName/ItemName/FieldName 寻址", "全部字段解析和校验成功后一次提交固定 Value" },
                        true);
                    AddRequiredField(contract, "UseNameAddressing", "false按索引寻址，true按精确名称寻址");
                    AddConditionalField(contract, "StructIndex", "UseNameAddressing", new[] { "False" }, "目标结构体非负索引");
                    AddConditionalField(contract, "ItemIndex", "UseNameAddressing", new[] { "False" }, "目标结构项非负索引");
                    AddConditionalField(contract, "StructName", "UseNameAddressing", new[] { "True" }, "目标结构体精确名称");
                    AddConditionalField(contract, "ItemName", "UseNameAddressing", new[] { "True" }, "目标数据项精确名称");
                    AddRequiredField(contract, "Params", "字段索引与固定值集合");
                    contract["constraints"] = new JArray("名称模式下每个 Params 项必须填写 FieldName；索引模式下使用 FieldIndex", "名称解析失败不回退索引", "任一字段失败时目标项不产生部分写入");
                    contract["failureModes"] = new JArray("任一索引或名称无效时报警", "结构体字段写入失败时报警");
                    break;

                case "获取结构体数据项":
                    contract = CreateContract(
                        "读取指定结构体项的字段，并批量写入连续变量或按映射写入变量。",
                        new[] { "根据 UseNameAddressing 严格选择索引或名称寻址", "IsAllItem=true 时按真实字段索引排序读取并从 FirstResultVariableName 连续写入", "否则按 Params 的字段索引或名称与结果变量引用逐项写入" },
                        true);
                    AddRequiredField(contract, "UseNameAddressing", "false按索引寻址，true按精确名称寻址");
                    AddConditionalField(contract, "StructIndex", "UseNameAddressing", new[] { "False" }, "目标结构体非负索引");
                    AddConditionalField(contract, "ItemIndex", "UseNameAddressing", new[] { "False" }, "目标结构项非负索引");
                    AddConditionalField(contract, "StructName", "UseNameAddressing", new[] { "True" }, "目标结构体精确名称");
                    AddConditionalField(contract, "ItemName", "UseNameAddressing", new[] { "True" }, "目标数据项精确名称");
                    AddConditionalField(contract, "FirstResultVariableName", "IsAllItem", new[] { "True" }, "批量结果的首个变量");
                    AddConditionalField(contract, "Params", "IsAllItem", new[] { "False" }, "字段索引到目标变量的映射集合");
                    contract["constraints"] = new JArray("名称模式下非批量读取的每个 Params 项必须填写 FieldName", "名称解析失败不回退索引");
                    contract["failureModes"] = new JArray("结构体或变量索引无效时报警", "字段读取或变量写入失败时报警");
                    break;

                case "复制结构体数据项":
                    contract = CreateContract(
                        "在两个结构体项之间复制全部字段或明确字段映射。",
                        new[] { "源和目标分别根据 UseSourceNameAddressing/UseTargetNameAddressing 选择索引或名称寻址", "IsAllValue=true 时复制完整项数据", "否则解析全部 Params 后一次写入目标字段" },
                        true);
                    AddRequiredField(contract, "UseSourceNameAddressing", "源false按索引寻址，true按精确名称寻址");
                    AddRequiredField(contract, "UseTargetNameAddressing", "目标false按索引寻址，true按精确名称寻址");
                    AddConditionalField(contract, "SourceStructIndex", "UseSourceNameAddressing", new[] { "False" }, "源结构体非负索引");
                    AddConditionalField(contract, "SourceItemIndex", "UseSourceNameAddressing", new[] { "False" }, "源结构项非负索引");
                    AddConditionalField(contract, "SourceStructName", "UseSourceNameAddressing", new[] { "True" }, "源结构体精确名称");
                    AddConditionalField(contract, "SourceItemName", "UseSourceNameAddressing", new[] { "True" }, "源数据项精确名称");
                    AddConditionalField(contract, "TargetStructIndex", "UseTargetNameAddressing", new[] { "False" }, "目标结构体非负索引");
                    AddConditionalField(contract, "TargetItemIndex", "UseTargetNameAddressing", new[] { "False" }, "目标结构项非负索引");
                    AddConditionalField(contract, "TargetStructName", "UseTargetNameAddressing", new[] { "True" }, "目标结构体精确名称");
                    AddConditionalField(contract, "TargetItemName", "UseTargetNameAddressing", new[] { "True" }, "目标数据项精确名称");
                    AddConditionalField(contract, "Params", "IsAllValue", new[] { "False" }, "源字段到目标字段的映射集合");
                    contract["constraints"] = new JArray("名称模式下 Params 使用对应的 SourceFieldName/TargetFieldName", "名称解析失败不回退索引", "任一映射失败时目标项不产生部分写入");
                    contract["failureModes"] = new JArray("任一索引或映射无效时报警", "源值为空、读取失败或目标写入失败时报警");
                    break;

                case "插入结构体数据项":
                    contract = CreateContract(
                        "在指定结构体位置插入由 Params 定义的新数据项。",
                        new[] { "根据 UseStructNameAddressing 按结构体索引或名称寻址", "按 Params 顺序创建字段", "Type=double/string 时从变量或固定值严格取值", "按 TargetItemIndex 指定的位置插入" },
                        true);
                    AddRequiredField(contract, "UseStructNameAddressing", "false按结构体索引寻址，true按结构体精确名称寻址");
                    AddConditionalField(contract, "TargetStructIndex", "UseStructNameAddressing", new[] { "False" }, "目标结构体非负索引");
                    AddConditionalField(contract, "TargetStructName", "UseStructNameAddressing", new[] { "True" }, "目标结构体精确名称");
                    AddRequiredField(contract, "TargetItemIndex", "插入位置非负索引");
                    AddRequiredField(contract, "ItemName", "新数据项名称");
                    AddRequiredField(contract, "Params", "新数据项字段集合");
                    contract["constraints"] = new JArray("每个字段的 ValueVariableName 与 Value 表达变量或固定值来源", "字段顺序决定字段索引");
                    contract["failureModes"] = new JArray("数值固定值、变量或目标索引无效时报警", "插入失败时报警");
                    break;

                case "删除结构体数据项":
                    contract = CreateContract(
                        "删除指定结构体中的一个数据项。",
                        new[] { "根据 UseNameAddressing 严格选择索引或名称寻址", "删除解析到的目标项", "不改写任何流程中的其他索引" },
                        true);
                    AddRequiredField(contract, "UseNameAddressing", "false按索引寻址，true按精确名称寻址");
                    AddConditionalField(contract, "TargetStructIndex", "UseNameAddressing", new[] { "False" }, "目标结构体非负索引");
                    AddConditionalField(contract, "TargetItemIndex", "UseNameAddressing", new[] { "False" }, "目标结构项非负索引");
                    AddConditionalField(contract, "TargetStructName", "UseNameAddressing", new[] { "True" }, "目标结构体精确名称");
                    AddConditionalField(contract, "TargetItemName", "UseNameAddressing", new[] { "True" }, "目标数据项精确名称");
                    contract["failureModes"] = new JArray("索引无效、越界或删除失败时报警");
                    break;

                case "查找结构体数据项":
                    contract = CreateContract(
                        "按名称、字符串值或数值在结构体中查找匹配项并保存结果。",
                        new[] { "根据 UseStructNameAddressing 按结构体索引或名称寻址", "按 Type 严格解释 Key 并查找", "命中后把结果写入 ResultVariableName；未命中触发指令报警" },
                        true);
                    AddRequiredField(contract, "UseStructNameAddressing", "false按结构体索引寻址，true按结构体精确名称寻址");
                    AddConditionalField(contract, "TargetStructIndex", "UseStructNameAddressing", new[] { "False" }, "目标结构体非负索引");
                    AddConditionalField(contract, "TargetStructName", "UseStructNameAddressing", new[] { "True" }, "目标结构体精确名称");
                    AddRequiredField(contract, "Type", "名称等于key、字符串等于key或数值等于key");
                    AddRequiredField(contract, "Key", "查找关键字；数值模式必须是有效数值");
                    AddRequiredField(contract, "ResultVariableName", "查找结果保存变量");
                    contract["failureModes"] = new JArray("模式、关键字、结构体或保存变量无效时报警", "没有找到匹配项时报警");
                    break;

                case "获取结构体数量":
                    contract = CreateContract(
                        "读取结构体总数和指定结构体的项数并分别写入变量。",
                        new[] { "根据 UseStructNameAddressing 按结构体索引或名称寻址", "校验两个结果变量", "写入结构体总数和目标结构体项数" },
                        true);
                    AddRequiredField(contract, "UseStructNameAddressing", "false按结构体索引寻址，true按结构体精确名称寻址");
                    AddConditionalField(contract, "TargetStructIndex", "UseStructNameAddressing", new[] { "False" }, "目标结构体非负索引");
                    AddConditionalField(contract, "TargetStructName", "UseStructNameAddressing", new[] { "True" }, "目标结构体精确名称");
                    AddRequiredField(contract, "StructCountVariableName", "结构体总数保存变量");
                    AddRequiredField(contract, "ItemCountVariableName", "目标结构体项数保存变量");
                    contract["failureModes"] = new JArray("索引无效或任一结果变量写入失败时报警");
                    break;

                case "网口通讯操作":
                    contract = CreateContract(
                        "按顺序启动或断开一个或多个 TCP 逻辑通道。启动建立通道生命周期，但不等同于已经建立活动连接。",
                        new[] { "逐项按 Name 读取 TCP 配置", "Ops=启动时Server进入监听，Client进入连接或自动重连", "Ops=断开时取消监听、连接和重连", "校验最终启动状态" },
                        true);
                    AddRequiredField(contract, "Params", "TCP 对象与启动或断开动作集合");
                    contract["constraints"] = new JArray("Name 必须是精确 TCP 配置名称", "Ops 仅允许启动或断开", "多个对象按 Params 顺序处理", "Client启用AutoReconnect时首次连接失败仍保持已启动并进入重连状态", "需要真实连接时必须继续使用等待网口连接");
                    contract["failureModes"] = new JArray("通讯服务、TCP 配置、动作或最终启动状态无效时报警", "Client未启用AutoReconnect且首次连接失败时启动失败");
                    break;

                case "等待网口连接":
                    contract = CreateContract(
                        "按顺序等待一个或多个 TCP 对象进入活动连接状态。",
                        new[] { "逐项校验 TCP 配置", "在该项 TimeoutMs 边界内轮询真实连接状态", "Client连接成功或Server存在匹配远端条件的在线会话后继续等待下一项" },
                        true);
                    AddRequiredField(contract, "Params", "TCP 对象与各自超时集合");
                    contract["constraints"] = new JArray("每项 TimeoutMs 必须大于0毫秒", "Server仅处于监听状态不算连接成功", "Client处于连接中或重连中不算连接成功", "所有对象依次连接成功后指令才完成");
                    contract["failureModes"] = new JArray("通讯服务或配置不存在时报警", "任一对象连接超时时报警");
                    break;

                case "发送TCP通讯消息":
                    contract = CreateContract(
                        "通过指定 TCP 对象发送变量中的文本或十六进制数据。",
                        new[] { "从 Msg 变量读取发送内容", "按 UseHexEncoding 选择文本或十六进制编码", "在 TimeoutMs 边界内通过 ConnectionName 发送" },
                        true);
                    AddRequiredField(contract, "ConnectionName", "TCP 通讯对象精确名称");
                    AddRequiredField(contract, "Msg", "发送内容来源变量");
                    AddRequiredField(contract, "TimeoutMs", "发送超时；必须大于0毫秒");
                    contract["constraints"] = new JArray("通道必须存在真实活动连接", "Server具体远端通道只向已路由会话发送", "Server通配通道向该通道的全部在线会话广播");
                    contract["failureModes"] = new JArray("通讯对象、变量或超时无效时报警", "发送失败或超时时报警");
                    break;

                case "接收TCP通讯消息":
                    contract = CreateContract(
                        "等待指定 TCP 对象接收一条消息，并把文本或十六进制结果写入变量。",
                        new[] { "确认 ConnectionName 处于活动连接", "在 TimeoutMs 边界内接收", "按 UseHexEncoding 选择十六进制或文本结果", "写入 MsgSaveValue" },
                        true);
                    AddRequiredField(contract, "ConnectionName", "TCP 通讯对象精确名称");
                    AddRequiredField(contract, "MsgSaveValue", "接收结果保存变量");
                    AddRequiredField(contract, "TimeoutMs", "接收超时；必须大于0毫秒");
                    contract["constraints"] = new JArray("通道必须存在真实活动连接", "Server只接收被共享监听器路由到该逻辑通道的会话消息");
                    contract["failureModes"] = new JArray("TCP 未连接、接收失败或超时时报警", "结果变量写入失败时报警");
                    break;

                case "串口通讯操作":
                    contract = CreateContract(
                        "按顺序启动或断开一个或多个串口通讯对象。",
                        new[] { "逐项按 Name 读取串口配置", "Ops=启动时打开串口", "Ops=断开时关闭串口", "校验最终打开状态" },
                        true);
                    AddRequiredField(contract, "Params", "串口对象与启动或断开动作集合");
                    contract["constraints"] = new JArray("Name 必须是精确串口配置名称", "Ops 仅允许启动或断开", "多个对象按 Params 顺序处理");
                    contract["failureModes"] = new JArray("通讯服务、串口配置、动作或最终状态无效时报警");
                    break;

                case "等待串口连接":
                    contract = CreateContract(
                        "按顺序等待一个或多个串口进入打开状态。",
                        new[] { "逐项校验串口配置", "在该项 TimeoutMs 边界内轮询打开状态", "当前项打开后继续等待下一项" },
                        true);
                    AddRequiredField(contract, "Params", "串口对象与各自超时集合");
                    contract["constraints"] = new JArray("每项 TimeoutMs 必须大于0毫秒", "所有串口依次打开后指令才完成");
                    contract["failureModes"] = new JArray("通讯服务或配置不存在时报警", "任一串口打开超时时报警");
                    break;

                case "发送串口通讯消息":
                    contract = CreateContract(
                        "通过指定串口发送变量中的文本或十六进制数据。",
                        new[] { "从 Msg 变量读取发送内容", "按 UseHexEncoding 选择文本或十六进制编码", "在 TimeoutMs 边界内通过 ConnectionName 发送" },
                        true);
                    AddRequiredField(contract, "ConnectionName", "串口通讯对象精确名称");
                    AddRequiredField(contract, "Msg", "发送内容来源变量");
                    AddRequiredField(contract, "TimeoutMs", "发送超时；必须大于0毫秒");
                    contract["failureModes"] = new JArray("通讯对象、变量或超时无效时报警", "发送失败或超时时报警");
                    break;

                case "接收串口通讯消息":
                    contract = CreateContract(
                        "等待指定串口接收一条消息，并把文本或十六进制结果写入变量。",
                        new[] { "确认 ConnectionName 已打开", "在 TimeoutMs 边界内接收", "按 UseHexEncoding 选择十六进制或文本结果", "写入 MsgSaveValue" },
                        true);
                    AddRequiredField(contract, "ConnectionName", "串口通讯对象精确名称");
                    AddRequiredField(contract, "MsgSaveValue", "接收结果保存变量");
                    AddRequiredField(contract, "TimeoutMs", "接收超时；必须大于0毫秒");
                    contract["failureModes"] = new JArray("串口未打开、接收失败或超时时报警", "结果变量写入失败时报警");
                    break;

                case "发送与接收":
                    contract = CreateContract(
                        "通过 TCP 或串口完成一次请求与响应，并可把响应写入变量。",
                        new[] { "按 CommType 选择 TCP 或串口", "从 SendMsg 变量读取请求", "按 SendConvert 发送", "在 TimeoutMs 边界内等待响应", "按 ReceiveConvert 解析并写入 ReceiveSaveValue" },
                        true);
                    AddRequiredField(contract, "CommType", "通讯类型；TCP 或串口");
                    AddRequiredField(contract, "ConnectionName", "所选通讯类型下的精确对象名称");
                    AddRequiredField(contract, "SendMsg", "请求内容来源变量");
                    AddRequiredField(contract, "TimeoutMs", "请求响应超时；必须大于0毫秒");
                    contract["constraints"] = new JArray("TCP 对象必须处于活动连接，串口必须已打开", "TCP Server请求响应模式要求该逻辑通道恰好一个在线会话，不能对多客户端通配广播通道执行请求响应", "ReceiveSaveValue 为空时仍接收响应但不保存", "连接自动重连只恢复通道；仅当RetryCount大于0时，本指令才按固定RetryIntervalMs重新执行完整业务请求");
                    contract["failureModes"] = new JArray("通讯类型、对象、变量或超时无效时报警", "发送接收失败或结果保存失败时报警");
                    break;

                case "PLC读写":
                    contract = CreateContract(
                        "按强类型Modbus地址执行按项或连续批量读取/写入。",
                        new[] { "按项模式处理多个独立地址", "连续批量模式通过一次请求处理连续地址", "读取写入变量、写入固定值均严格匹配PLC数据类型", "任一项失败时报警且不执行宽松转换" },
                        true);
                    AddRequiredField(contract, "DeviceName", "PLC设备精确名称；可通过 get_plc_device 或 list_plc_devices 获取");
                    AddMultiConditionalField(contract, "ReadItems", new JObject
                    {
                        ["Action"] = new JArray("Read"),
                        ["Mode"] = new JArray("Items")
                    }, "按项读取配置；数组长度就是读取项数量，范围1..100");
                    AddMultiConditionalField(contract, "ReadBatch", new JObject
                    {
                        ["Action"] = new JArray("Read"),
                        ["Mode"] = new JArray("ContinuousBatch")
                    }, "连续批量读取配置；结果从首保存变量开始按变量索引写入");
                    AddMultiConditionalField(contract, "WriteItems", new JObject
                    {
                        ["Action"] = new JArray("Write"),
                        ["Mode"] = new JArray("Items")
                    }, "按项写入配置；数组长度就是写入项数量，范围1..100，每项独立选择变量或固定值");
                    AddMultiConditionalField(contract, "WriteBatch", new JObject
                    {
                        ["Action"] = new JArray("Write"),
                        ["Mode"] = new JArray("ContinuousBatch")
                    }, "连续批量写入配置；使用连续变量或明确重复固定值");
                    contract["modeMatrix"] = new JArray(
                        new JObject
                        {
                            ["when"] = new JObject { ["Action"] = "Read", ["Mode"] = "Items" },
                            ["use"] = new JArray("DeviceName", "ReadItems"),
                            ["reject"] = new JArray("ReadBatch", "WriteItems", "WriteBatch")
                        },
                        new JObject
                        {
                            ["when"] = new JObject { ["Action"] = "Read", ["Mode"] = "ContinuousBatch" },
                            ["use"] = new JArray("DeviceName", "ReadBatch"),
                            ["reject"] = new JArray("ReadItems", "WriteItems", "WriteBatch")
                        },
                        new JObject
                        {
                            ["when"] = new JObject { ["Action"] = "Write", ["Mode"] = "Items" },
                            ["use"] = new JArray("DeviceName", "WriteItems"),
                            ["reject"] = new JArray("ReadItems", "ReadBatch", "WriteBatch")
                        },
                        new JObject
                        {
                            ["when"] = new JObject { ["Action"] = "Write", ["Mode"] = "ContinuousBatch" },
                            ["use"] = new JArray("DeviceName", "WriteBatch"),
                            ["reject"] = new JArray("ReadItems", "ReadBatch", "WriteItems")
                        });
                    contract["dataRules"] = new JObject
                    {
                        ["address"] = "StartAddress 0..65535，且按数据宽度计算后的访问末地址不得超过65535",
                        ["elementCount"] = "连续批量1..1000；String固定为1；重复固定值允许明确写入全部元素",
                        ["stringByteLength"] = "String 为1..2000，其他 DataType 必须为0",
                        ["areaAndType"] = "Coil/DiscreteInput 仅 Boolean；HoldingRegister/InputRegister 不允许 Boolean",
                        ["writeArea"] = "Write 仅允许 Coil 或 HoldingRegister",
                        ["variableType"] = "String 对应平台 string；Boolean及所有数值类型对应平台 double"
                    };
                    contract["constraints"] = new JArray(
                        "Action只能是Read或Write",
                        "Mode只能是Items或ContinuousBatch，且只允许当前Action/Mode分支字段",
                        "ReadItems与WriteItems数组长度范围1..100",
                        "连续批量读取从ReadBatch.FirstVariableName开始写入连续变量",
                        "连续批量写入从WriteBatch.FirstVariableName读取连续变量，或将WriteBatch.ConstantValue明确重复到全部元素",
                        "Boolean只允许Coil或DiscreteInput，其他类型只允许寄存器区",
                        "DiscreteInput和InputRegister禁止Write",
                        "固定值按目标DataType严格解析");
                    contract["failureModes"] = new JArray("设备未初始化时报警", "参数或变量类型不匹配时报警", "通讯失败时设备进入故障状态");
                    break;

                case "PLC映射控制":
                    contract = CreateContract(
                        "按设备重新初始化、启动或停止PLC变量映射。",
                        new[] { "按DeviceName定位设备", "执行Reinitialize、Start或Stop", "失败时报警" },
                        true);
                    AddRequiredField(contract, "DeviceName", "PLC设备精确名称；可通过 get_plc_device 或 list_plc_devices 获取");
                    contract["actionSemantics"] = new JObject
                    {
                        ["Reinitialize"] = "重建该设备连接，成功后进入Ready，不自动启动映射",
                        ["Start"] = "启动该设备已启用的变量映射",
                        ["Stop"] = "停止该设备变量映射；重复停止仍视为成功"
                    };
                    contract["constraints"] = new JArray(
                        "Reinitialize只重建连接，不自动启动映射",
                        "Start要求设备处于Ready或Stopped",
                        "Stop幂等");
                    contract["failureModes"] = new JArray("设备不存在或状态不允许时报警", "重新初始化失败时保持Faulted");
                    break;

                case "流程结束":
                    contract = CreateContract(
                        "执行到当前位置时正常结束当前流程。",
                        new[] { "请求流程以 Completed 原因结束", "不执行当前位置之后的指令" },
                        false);
                    contract["controlFlow"]["terminal"] = true;
                    contract["controlFlow"]["terminationReason"] = "Completed";
                    break;

                default:
                    contract = CreateUnknownContract();
                    break;
            }

            if (contract["coverage"] == null)
            {
                contract["coverage"] = "specialized";
            }
            contract["source"] = nameof(OperationBehaviorCatalog);
            AddAlarmPolicy(contract);
            AddCommunicationRetryPolicy(contract, operation);
            return contract;
        }

        public static JObject BuildFieldRule(OperationType operation, string fieldName)
        {
            JObject contract = BuildContract(operation);
            return contract?["fieldRules"]?[fieldName] as JObject;
        }

        public static bool IsFieldRequired(OperationType operation, string fieldName)
        {
            JObject rule = BuildFieldRule(operation, fieldName);
            if (rule == null)
            {
                return false;
            }
            if (rule["requiredForRun"]?.Value<bool>() == true)
            {
                return true;
            }

            JObject requiredWhen = rule["requiredWhenForRun"] as JObject;
            if (requiredWhen != null && MatchesAllConditions(operation, requiredWhen))
            {
                return true;
            }
            if (rule["requiredWhenAnyForRun"] is JArray alternatives)
            {
                foreach (JObject alternative in alternatives.OfType<JObject>())
                {
                    if (MatchesAllConditions(operation, alternative)) return true;
                }
            }
            return false;
        }

        private static bool MatchesAllConditions(OperationType operation, JObject conditions)
        {
            foreach (JProperty condition in conditions.Properties())
            {
                PropertyInfo property = operation.GetType().GetProperty(condition.Name);
                string currentValue = property?.GetValue(operation)?.ToString();
                if (!(condition.Value is JArray candidates)
                    || !ContainsString(candidates, currentValue)) return false;
            }
            return true;
        }

        private static bool ContainsString(JArray values, string target)
        {
            foreach (JToken value in values)
            {
                if (string.Equals(value?.ToString(), target, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static JObject CreateContract(string purpose, string[] execution, bool fallThrough)
        {
            return new JObject
            {
                ["contractVersion"] = ContractVersion,
                ["purpose"] = purpose,
                ["execution"] = new JArray(execution),
                ["controlFlow"] = new JObject
                {
                    ["fallThrough"] = fallThrough,
                    ["description"] = fallThrough
                        ? "正常完成后自动执行下一条指令"
                        : "该指令显式决定后续流向，不自动执行下一条"
                },
                ["fieldRules"] = new JObject()
            };
        }

        private static JObject CreateUnknownContract()
        {
            return new JObject
            {
                ["contractVersion"] = ContractVersion,
                ["coverage"] = "unknown",
                ["purpose"] = "该原生指令的字段结构可以严格读取和写入，但尚无专用运行行为契约。",
                ["execution"] = new JArray(),
                ["controlFlow"] = new JObject
                {
                    ["known"] = false,
                    ["description"] = "未提供控制流结论；不得由通用默认值推断是否顺序执行。"
                },
                ["fieldRules"] = new JObject()
            };
        }

        private static void AddRequiredGoto(JObject contract, string fieldName, string description)
        {
            AddGotoRule(contract, fieldName, true, description);
        }

        private static void AddGotoRule(JObject contract, string fieldName, bool required, string description)
        {
            ((JObject)contract["fieldRules"])[fieldName] = new JObject
            {
                ["requiredForRun"] = required,
                ["referenceType"] = "proc.goto",
                ["format"] = "{operationId} | {operationKey} | {stepId,operationKey} | {stepKey,operationKey}",
                ["allowDisplayText"] = false,
                ["description"] = description
            };
        }

        private static void AddConditionalGoto(JObject contract, string fieldName, string dependsOn, string[] values, string description)
        {
            AddGotoRule(contract, fieldName, false, description);
            JObject rule = (JObject)contract["fieldRules"][fieldName];
            rule["visibleWhen"] = new JObject { [dependsOn] = new JArray(values) };
        }

        private static void AddConditionalField(JObject contract, string fieldName, string dependsOn, string[] values, string description)
        {
            ((JObject)contract["fieldRules"])[fieldName] = new JObject
            {
                ["visibleWhen"] = new JObject { [dependsOn] = new JArray(values) },
                ["requiredWhenForRun"] = new JObject { [dependsOn] = new JArray(values) },
                ["description"] = description
            };
        }

        private static void AddRequiredField(JObject contract, string fieldName, string description)
        {
            ((JObject)contract["fieldRules"])[fieldName] = new JObject
            {
                ["requiredForRun"] = true,
                ["description"] = description
            };
        }

        private static void AddMultiConditionalField(
            JObject contract, string fieldName, JObject conditions, string description)
        {
            ((JObject)contract["fieldRules"])[fieldName] = new JObject
            {
                ["visibleWhen"] = conditions.DeepClone(),
                ["requiredWhenForRun"] = conditions.DeepClone(),
                ["description"] = description
            };
        }

        private static void AddAlarmPolicy(JObject contract)
        {
            JObject rules = (JObject)contract["fieldRules"];
            AddConditionalRule(rules, "AlarmInfoId", "AlarmType", new[] { "弹框确定", "弹框确定与否", "弹框确定与否与取消" }, "弹框报警使用的报警信息编号");
            AddConditionalGotoRule(rules, "Goto1", "AlarmType", new[] { "自动处理", "弹框确定", "弹框确定与否", "弹框确定与否与取消" }, "报警确认或自动处理分支");
            AddConditionalGotoRule(rules, "Goto2", "AlarmType", new[] { "弹框确定与否", "弹框确定与否与取消" }, "报警否定分支");
            AddConditionalGotoRule(rules, "Goto3", "AlarmType", new[] { "弹框确定与否与取消" }, "报警取消分支");
            contract["alarmPolicy"] = new JObject
            {
                ["description"] = "AlarmType 决定异常后的停止、忽略、自动处理或弹框分支行为",
                ["safeDefault"] = "报警停止",
                ["gotoScope"] = "Goto1/Goto2/Goto3 是异常处理分支，与指令自身的业务跳转字段相互独立",
                ["missingResourceBehavior"] = "报警信息编号可以先保存；对应资源未配置时流程状态为 incomplete，启动闸门会拒绝运行"
            };
        }

        private static void AddCommunicationRetryPolicy(JObject contract, OperationType operation)
        {
            if (!(operation is CommunicationOperationType communication))
            {
                return;
            }
            bool responseValidation = communication is ResponseCommunicationOperationType response
                && response.ShouldEvaluateResponseConditions;
            if (responseValidation)
            {
                ((JObject)contract["fieldRules"])[nameof(ResponseCommunicationOperationType.ResponseConditions)] =
                    new JObject
                    {
                        ["enabledWhen"] = operation is PlcReadWrite
                            ? "RetryCount > 0 && Action == Read"
                            : "RetryCount > 0",
                        ["ignoredWhen"] = operation is PlcReadWrite
                            ? "RetryCount == 0 || Action == Write"
                            : "RetryCount == 0",
                        ["maxItems"] = 20,
                        ["judgeModes"] = new JArray("非空", "字段存在", "等于特征字符", "包含特征字符",
                            "值在区间左", "值在区间右", "值在区间内"),
                        ["combination"] = "第一条作为初值，后续按Operator=且/或组合",
                        ["jsonFieldPath"] = "可选点分隔精确路径；字段不存在、JSON无效或值不满足均属于可重试的接收结果失败"
                    };
            }
            contract["communicationRetry"] = new JObject
            {
                ["supported"] = true,
                ["retryCountRange"] = "0..10，表示首次通信失败后的额外重试次数",
                ["retryIntervalMsRange"] = "0..60000，每次重试使用同一固定间隔，不退避",
                ["retryableFailures"] = responseValidation
                    ? new JArray("通信掉线、超时、无回应或通讯运行时异常", "已收到数据但结果判定不满足")
                    : new JArray("通信掉线、超时、无回应或通讯运行时异常"),
                ["responseValidation"] = responseValidation
                    ? "仅RetryCount大于0时执行；0时跳过ResponseConditions"
                    : "该指令没有接收结果判定",
                ["finalFailure"] = "仅重试耗尽后的最终失败进入指令AlarmType策略"
            };
        }

        private static void AddConditionalRule(JObject rules, string fieldName, string dependsOn, string[] values, string description)
        {
            rules[fieldName] = new JObject
            {
                ["visibleWhen"] = new JObject { [dependsOn] = new JArray(values) },
                ["requiredWhenForRun"] = new JObject { [dependsOn] = new JArray(values) },
                ["description"] = description
            };
        }

        private static void AddConditionalGotoRule(JObject rules, string fieldName, string dependsOn, string[] values, string description)
        {
            AddConditionalRule(rules, fieldName, dependsOn, values, description);
            JObject rule = (JObject)rules[fieldName];
            rule["referenceType"] = "proc.goto";
            rule["format"] = "{operationId} | {operationKey} | {stepId,operationKey} | {stepKey,operationKey}";
            rule["allowDisplayText"] = false;
        }
    }
}
