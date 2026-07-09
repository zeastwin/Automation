using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Automation.McpServer
{
    [McpServerToolType]
    public static class AutomationMcpTools
    {
        [McpServerTool, Description(
            "列出 Automation 当前全部流程的基础信息。用于意图定位的第一步；不要假设流程名称唯一。" +
            "Automation 领域层次：程序 > 流程(Proc) > 步骤(Step) > 指令(Operation)，详见 get_patch_contract 的 domainConcepts。" +
            "重要语义：用户口语中的\"N号流程\"即 procIndex=N（不是第N个流程，索引从0开始）。例如\"3号流程\"就是 procIndex=3。返回列表中的 procIndex 字段就是用户口中的\"流程号\"。")]
        public static async Task<string> ListProcs(
            [Description("是否附带步骤级摘要。true 会返回更多定位信息，但响应更长。")] bool includeStepSummary = false)
        {
            return await ExecuteAsync(
                toolName: nameof(ListProcs),
                args: new { includeStepSummary },
                action: client => client.ListProcsAsync(includeStepSummary)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "读取指定流程的摘要视图，返回步骤、指令、摘要信息与稳定标识。适合在细化修改目标前使用。")]
        public static async Task<string> GetProcOverview(
            [Description("流程索引，从 0 开始。")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(GetProcOverview),
                args: new { procIndex },
                action: client => client.GetProcOverviewAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "读取指定流程的完整详情视图，返回 head、steps、ops、fields 与稳定标识。修改前必须先读取。")]
        public static async Task<string> GetProcDetail(
            [Description("流程索引，从 0 开始。")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(GetProcDetail),
                args: new { procIndex },
                action: client => client.GetProcDetailAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "列出 Automation 当前支持的全部指令类型。插入新指令前应先调用。")]
        public static async Task<string> ListOperationTypes()
        {
            return await ExecuteAsync(
                toolName: nameof(ListOperationTypes),
                args: new { },
                action: client => client.ListOperationTypesAsync()).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "读取指令可编辑 Schema。优先按现有指令实例定位；新建指令时可只传 operaType。不要在未读取 Schema 的情况下猜字段名或枚举值。")]
        public static async Task<string> GetOperationSchema(
            [Description("流程索引。读取现有指令实例时必填；按指令类型查询时可为空。")] int? procIndex = null,
            [Description("步骤 ID。读取现有指令实例时必填；按指令类型查询时可为空。")] string? stepId = null,
            [Description("指令 ID。读取现有指令实例时必填；按指令类型查询时可为空。")] string? opId = null,
            [Description("指令类型。新建指令前按类型查询时必填，必须使用 list_operation_types 返回的 operaType，例如 IO检测。")] string? operaType = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetOperationSchema),
                args: new { procIndex, stepId, opId, operaType },
                action: client => client.GetOperationSchemaAsync(procIndex, stepId, opId, operaType)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "读取全部指令类型的调用说明（用途、关键字段、约束、常见误用）。" +
            "编写或修改指令前必须调用，避免字段功能理解偏差。" +
            "返回 JSON：key 是指令类型中文名，value 含 purpose/keyFields/constraints/commonMistakes。")]
        public static Task<string> GetOperationGuide()
        {
            return Task.FromResult(OperationGuideJson);
        }

        // 43 种指令类型的调用说明，基于执行代码（ProcessEngine.Operations.*.cs）和字段定义（OperationType.cs）编写。
        // AI 创建/修改指令前应读取本指南，避免字段功能理解偏差。
        private const string OperationGuideJson = """
{
  "_流程编号语义": {
    "purpose": "明确用户口语中\"N号流程\"与 procIndex 的对应关系，避免定位偏差",
    "keyFields": {
      "规则": "用户口语中的\"N号流程\"即 procIndex=N，索引从0开始",
      "示例": "\"3号流程\" = procIndex=3（不是第3个流程procIndex=2）",
      "list_procs返回": "返回列表中的 procIndex 字段就是用户口中的\"流程号\"，直接对应"
    },
    "constraints": "不要把\"N号流程\"理解为\"第N个流程\"从而误算为 procIndex=N-1；N号就是 procIndex=N",
    "commonMistakes": "常见错误：把\"3号流程\"理解为第3个流程(procIndex=2)，实际应是 procIndex=3；定位时务必以 list_procs 返回的 procIndex 为准，与用户口中的流程号一一对应"
  },
  "_通用字段说明": {
    "purpose": "所有指令继承自 OperationType 基类，以下字段对全部指令通用",
    "keyFields": {
      "Name": "指令显示名称，仅用于编辑识别与日志，不影响执行",
      "OperaType": "指令类型标识（只读），决定引擎执行分支，创建时由构造函数自动设置",
      "AlarmType": "异常处理策略，取值：报警停止/报警忽略/自动处理/弹框确定/弹框确定与否/弹框确定与否与取消",
      "AlarmInfoID": "报警信息库编号，仅 AlarmType 为弹框类时生效",
      "Goto1/Goto2/Goto3": "报警分支跳转目标，格式为 procIndex-stepIndex-opIndex（见_跳转编码说明）；AlarmType=自动处理仅用 Goto1；弹框确定与否用 Goto1/Goto2；弹框确定与否与取消用全部三个",
      "Disable": "true 时该指令被跳过执行",
      "isStopPoint": "true 时运行到该指令进入断点"
    },
    "constraints": "AlarmType 与 Goto1/2/3 联动：报警停止/报警忽略时不显示任何 Goto；自动处理仅 Goto1；弹框类按按钮数量显示",
    "commonMistakes": "AlarmType 选了弹框类但未填 AlarmInfoID 或对应 Goto 会导致跳转失败；Goto 格式必须是 procIndex-stepIndex-opIndex 三段式"
  },
  "_跳转编码说明": {
    "purpose": "所有跳转类字段（Goto1/Goto2/Goto3、goto1/goto2、TrueGoto/FalseGoto、Goto、DefaultGoto、PopupGoto1/2/3）的统一编码格式",
    "keyFields": {
      "格式": "procIndex-stepIndex-opIndex，三段用连字符-分隔，全部必填",
      "示例": "3-0-1 表示流程索引3、步骤0、指令1",
      "procIndex约束": "必须等于当前流程索引，不支持跨流程跳转；不一致会报\"流程索引不一致\"",
      "空值行为": "跳转类指令（逻辑判断/IO逻辑跳转）的 goto 字段为空时报\"跳转位置为空\"并报警，不会自动跳到下一条"
    },
    "constraints": "编辑器通过下拉框选择有效目标，值由系统自动生成为 procIndex-stepIndex-opIndex 格式；AI 填写时必须包含三段，不可省略 procIndex",
    "commonMistakes": "常见错误：写成 stepIndex-opIndex（两段，缺 procIndex）会导致解析失败；把空 goto2 当作\"不跳转/正常结束\"会导致报警；跨流程填 procIndex 会导致\"流程索引不一致\""
  },
  "自定义方法": {
    "purpose": "调用预先在自定义函数库中注册的方法",
    "keyFields": {"Name": "方法名，必须与 CustomFunc 注册的方法名完全一致"},
    "constraints": "Name 必填；运行时调用 Context.CustomFunc.RunFunc(Name)",
    "commonMistakes": "Name 写错或未注册会报\"找不到自定义函数\"；该方法无参数无返回值"
  },
  "IO操作": {
    "purpose": "批量输出IO点状态（开/关），每项支持前后延时",
    "keyFields": {"IOCount": "子项数量(1-6)", "IoParams": "IO输出项列表，每项含 IOName(输出点名)、value(开/关)、delayBefore/delayBeforeV、delayAfter/delayAfterV"},
    "constraints": "IOName 必填且必须是\"通用输出\"类型；delayBefore<=0 时读 delayBeforeV 变量；delayAfter<=0 时读 delayAfterV 变量；列表为空时报错",
    "commonMistakes": "IOName 填输入点会报\"IO输出失败\"；value 是 bool(true=开,false=关)不是字符串"
  },
  "IO检测": {
    "purpose": "检测输入IO点是否达到期望状态，所有项为\"与\"关系，超时未满足报警",
    "keyFields": {"IOCount": "子项数量(1-6)", "timeOutC": "超时参数组，含 TimeOut(固定ms) 和 TimeOutValue(变量名)", "IoParams": "检测项列表，每项含 IOName(输入/输出点名) 和 value(期望状态)"},
    "constraints": "IOName 必填；timeOutC.TimeOut<=0 时读 TimeOutValue；两者都<=0 报\"超时配置无效\"；循环检测直到全部满足或超时",
    "commonMistakes": "IOName 必须是\"通用输入\"或\"通用输出\"；多项任一不满足即继续等待；value 是 bool"
  },
  "IO组": {
    "purpose": "组合指令：先执行输出动作，再检测输入状态",
    "keyFields": {"OutIOCount": "输出子项数量", "CheckIOCount": "检测子项数量", "OutIoParams": "输出IO列表(同 IO操作)", "CheckIoParams": "检测IO列表(同 IO检测)", "timeOutC": "检测阶段超时参数组"},
    "constraints": "OutIoParams 必须全为\"通用输出\"；CheckIoParams 必须全为\"通用输入\"；类型不符直接报错；两阶段均不能为空",
    "commonMistakes": "输出和检测的IO类型不能混；timeOutC 仅作用于检测阶段"
  },
  "IO逻辑跳转": {
    "purpose": "根据多个IO状态逻辑组合结果跳转",
    "keyFields": {"IOCount": "IO条件数量", "InvalidDelayMs": "首次判断失败后的重试延时(ms)", "IoParams": "条件项列表，每项含 IOName、Target(目标bool)、Logic(与/或)", "TrueGoto": "逻辑为真时跳转目标(procIndex-stepIndex-opIndex)", "FalseGoto": "逻辑为假时跳转目标(procIndex-stepIndex-opIndex)"},
    "constraints": "IOName 必填；Logic 仅\"与\"/\"或\"；InvalidDelayMs<0 报错；第一项的 Logic 不生效(默认 isFirst)；失败且 InvalidDelayMs>0 时延时后重试一次；TrueGoto 和 FalseGoto 均必填，格式 procIndex-stepIndex-opIndex",
    "commonMistakes": "Logic 不能填\"且\"/\"或\"以外的值；第一项 Logic 字段会被忽略；TrueGoto/FalseGoto 留空会报\"跳转位置为空\"并报警，不是\"不跳转\"；格式必须是三段式 procIndex-stepIndex-opIndex"
  },
  "流程操作": {
    "purpose": "启动或停止其他流程",
    "keyFields": {"ProcCount": "子项数量", "procParams": "流程项列表，每项含 ProcName(流程名)、ProcValue(流程变量)、value(运行/停止)、delayAfter/delayAfterV"},
    "constraints": "ProcName 非空时优先，否则读 ProcValue 变量；value 仅\"运行\"/\"停止\"；运行要求目标流程处于 Stopped，停止要求非 Stopped",
    "commonMistakes": "启动未停止的流程会报\"流程未停止\"；停止已停止的流程会报\"流程已停止\""
  },
  "等待流程状态": {
    "purpose": "等待其他流程达到指定状态(运行/停止)",
    "keyFields": {"ProcCount": "子项数量", "Params": "等待项列表，每项含 ProcName/ProcValue、value(运行/停止)", "timeOutC": "超时参数组", "delayAfter": "完成后附加延时(ms)", "delayAfterV": "附加延时变量名(delayAfter<=0 时读取)"},
    "constraints": "timeOut<=0 时读 TimeOutValue；两者均<=0 报\"超时配置无效\"；所有项同时满足才通过",
    "commonMistakes": "value 必须是\"运行\"或\"停止\"；找不到流程会报错"
  },
  "跳转": {
    "purpose": "根据变量值匹配跳转目标(类似 switch)",
    "keyFields": {"ValueIndex/ValueName": "待匹配变量(必填)", "Count": "匹配分支数量", "Params": "分支列表，每项含 MatchValue/MatchValueIndex/MatchValueV(匹配值)、Goto(跳转目标，格式 procIndex-stepIndex-opIndex)", "DefaultGoto": "未匹配时的默认跳转(格式 procIndex-stepIndex-opIndex)"},
    "constraints": "MatchValue(固定值)与 MatchValueIndex/MatchValueV(变量)互斥，同时填报\"匹配值配置冲突\"；待匹配变量值为空报错；命中即跳转；全未命中用 DefaultGoto；DefaultGoto 为空时不跳转（继续执行下一条指令）",
    "commonMistakes": "匹配值固定值和变量只能填一个；Goto 和 DefaultGoto 格式必须是 procIndex-stepIndex-opIndex 三段式；DefaultGoto 为空是合法的（表示不跳转，继续下一条）"
  },
  "逻辑判断": {
    "purpose": "多条件数值/字符判断后跳转",
    "keyFields": {"goto1": "条件成立跳转目标(格式 procIndex-stepIndex-opIndex，必填)", "goto2": "条件不成立跳转目标(格式 procIndex-stepIndex-opIndex，必填)", "failDelay": "失败时重试延时(ms，字符串)", "Count": "条件分支数量", "Params": "条件项列表，含 ValueIndex/ValueName(判断变量)、JudgeMode、Up/Down、keyString、equal、Operator"},
    "constraints": "JudgeMode 仅\"值在区间左\"/\"值在区间右\"/\"值在区间内\"/\"等于特征字符\"；前三种按数值解析(用 Up/Down)，第四种按字符(用 keyString)；Operator 仅\"且\"/\"或\"；第一项 Operator 忽略；goto1 和 goto2 均必填，为空会报\"跳转位置为空\"",
    "commonMistakes": "JudgeMode 选\"等于特征字符\"时 Up/Down 不生效；数值模式变量不能解析为 double 会报错；goto2 为空不是\"不跳转/正常结束\"而是报警——若条件不成立时需继续执行下一条，应将 goto2 指向下一条指令的位置；格式必须三段式 procIndex-stepIndex-opIndex，不可省略 procIndex"
  },
  "延时": {
    "purpose": "流程暂停指定时长",
    "keyFields": {"timeMiniSecond": "固定延时时长(ms，字符串)", "timeMiniSecondV": "延时变量名"},
    "constraints": "timeMiniSecond 非空时优先(需非负整数)；为空时读 timeMiniSecondV 变量值(需非负整数)；两者都空则延时0",
    "commonMistakes": "timeMiniSecond 是字符串字段不是数字；负值或非数字会报\"延时时间无效\""
  },
  "弹框": {
    "purpose": "弹出对话框供用户选择，支持报警灯/蜂鸣器联动和延时自动关闭",
    "keyFields": {"PopupType": "弹框样式：弹是/弹是与否/弹是与否与取消", "InfoType": "提示信息来源：自定义提示信息/变量类型/报警信息库", "PopupMessage": "固定提示文本", "PopupMessageValue": "提示变量名", "PopupAlarmInfoID": "报警信息编号", "Btn1Text/Btn2Text/Btn3Text": "按钮文本", "PopupGoto1/2/3": "跳转目标(格式 procIndex-stepIndex-opIndex)", "DelayClose/DelayCloseTimeMs": "延时自动关闭", "AlarmLightEnable": "启用报警灯", "BuzzerIo/RedLightIo/YellowLightIo/GreenLightIo": "报警灯IO"},
    "constraints": "PopupType 决定按钮数量；InfoType 决定提示文本来源；DelayClose=true 时 DelayCloseTimeMs 必须>0；AlarmLightEnable=启用 时蜂鸣器/灯IO才生效；PopupGoto 格式 procIndex-stepIndex-opIndex",
    "commonMistakes": "InfoType=报警信息库时按钮文本从报警信息读取，自定义的 Btn*Text 会被忽略；PopupGoto 格式必须三段式，不可省略 procIndex"
  },
  "获取变量": {
    "purpose": "批量复制变量值(源→存储)，支持二级索引嵌套",
    "keyFields": {"Count": "子项数量", "Params": "复制项列表，每项含 ValueSourceIndex/ValueSourceName(源) 和 ValueSaveIndex/ValueSaveName(存储)"},
    "constraints": "源变量和存储变量都必须能解析；按项逐一复制 Value 字段",
    "commonMistakes": "源/存储变量任一不存在都会报错；索引和名称二选一"
  },
  "修改变量": {
    "purpose": "对源变量进行运算后保存到结果变量",
    "keyFields": {"ModifyType": "修改模式：替换/叠加/乘法/除法/求余/绝对值", "ValueSourceIndex/ValueSourceName": "源变量", "ChangeValue/ChangeValueIndex/ChangeValueName": "修改值(固定值或变量二选一)", "OutputValueIndex/OutputValueName": "结果保存变量"},
    "constraints": "ChangeValue(固定)与 ChangeValueIndex/Name(变量)互斥；除\"替换\"和\"绝对值\"外变量必须是 double；\"绝对值\"模式不需要修改值",
    "commonMistakes": "运算类模式变量类型不是 double 会报\"变量类型不匹配\"；修改值固定值和变量不能同时填"
  },
  "数据拼接": {
    "purpose": "按 string.Format 模板拼接多个变量值",
    "keyFields": {"Format": "拼接格式模板(如 \"{0}-{1}\")", "OutputValueIndex/OutputValueName": "结果保存变量", "Count": "源变量数量", "Params": "源变量列表"},
    "constraints": "Format 不能为 null(可空串)；按 Params 顺序填充占位符；源变量值 null 转空字符串",
    "commonMistakes": "占位符数量应与 Params 数量匹配；使用 .NET string.Format 语法"
  },
  "字符串分割": {
    "purpose": "按分隔符分割字符串，将结果连续写入多个变量",
    "keyFields": {"SplitMark": "分隔符(char)", "startIndex": "分割结果起始下标(字符串)", "Count": "提取数量(字符串，空则取全部)", "SourceValueIndex/SourceValue": "源变量", "OutputIndex/Output": "结果起始保存变量"},
    "constraints": "startIndex 需非负整数；Count 为空时取全部分段；从 OutputIndex 开始连续写入 Count 个变量；越界报错",
    "commonMistakes": "结果写入从 Output 变量索引开始连续 Count 个变量；SplitMark 是单个 char"
  },
  "字符串替换": {
    "purpose": "替换字符串内容(按字符或按区间)",
    "keyFields": {"ReplaceType": "替换类型：替换指定字符/替换指定区间", "ReplaceStr/ReplaceStrIndex/ReplaceStrV": "被替换字符串", "NewStr/NewStrIndex/NewStrV": "新字符串", "StartIndex/Count": "区间模式下的起始位置和长度", "SourceValueIndex/SourceValue": "源变量", "OutputIndex/Output": "结果保存变量"},
    "constraints": "ReplaceType=替换指定字符时用 ReplaceStr 系列；=替换指定区间时用 StartIndex/Count；固定值与变量互斥",
    "commonMistakes": "模式选错会导致字段不显示；被替换字符/新字符任一为空报错"
  },
  "设置结构体数据项": {
    "purpose": "设置结构体中某数据项的多个字段值",
    "keyFields": {"StructIndex": "结构体索引", "ItemIndex": "数据项索引", "Count": "设置的字段数量", "Params": "字段列表，每项含 valueIndex(字段索引) 和 value(固定值)"},
    "constraints": "StructIndex/ItemIndex/Count 必须为有效整数；value 是字符串固定值不是变量名",
    "commonMistakes": "value 是字符串固定值；Count 不能超过 Params 实际数量"
  },
  "获取结构体数据项": {
    "purpose": "读取结构体数据项字段值并写入变量",
    "keyFields": {"IsAllItem": "true 时批量读取全部字段", "StartValue": "批量模式结果起始保存变量", "StructIndex": "结构体索引", "ItemIndex": "数据项索引", "Count": "非批量模式的读取数量", "Params": "字段列表"},
    "constraints": "IsAllItem=true 时按 StartValue 起始连续写全部字段；false 时按 Params 逐项读取",
    "commonMistakes": "批量模式与非批量模式字段使用不同"
  },
  "复制结构体数据项": {
    "purpose": "复制结构体数据项(整项或部分字段)",
    "keyFields": {"IsAllValue": "true 时整体复制", "SourceStructIndex/SourceItemIndex": "源索引", "TargetStructIndex/TargetItemIndex": "目标索引", "Count": "字段数量", "Params": "字段列表"},
    "constraints": "IsAllValue=true 时仅用四个索引整体复制；false 时按 Params 逐字段复制",
    "commonMistakes": "目标字段索引字段名是 Targetvalue(注意拼写)"
  },
  "插入结构体数据项": {
    "purpose": "在指定位置插入新数据项",
    "keyFields": {"Name": "新数据项名称", "TargetStructIndex": "目标结构体索引", "TargetItemIndex": "目标数据项索引", "Count": "字段数量", "Params": "字段列表，每项含 Type(double/string)、ValueItem(变量名)、Value(固定值)"},
    "constraints": "Type 仅\"double\"/\"string\"；ValueItem 非空时优先于 Value",
    "commonMistakes": "ValueItem 优先于 Value；数值类型 Value 不能解析会报错"
  },
  "删除结构体数据项": {
    "purpose": "删除结构体中指定数据项",
    "keyFields": {"TargetStructIndex": "结构体索引", "TargetItemIndex": "数据项索引(有特殊含义)"},
    "constraints": "TargetItemIndex>=255 删末尾项；<=-1 删首项；其他值删指定位置",
    "commonMistakes": "TargetItemIndex 有特殊语义：255 和 -1 不是普通索引"
  },
  "查找结构体数据项": {
    "purpose": "按关键字查找数据项并返回结果",
    "keyFields": {"TargetStructIndex": "结构体索引", "Type": "查找类型：名称等于key/字符串等于key/数值等于key", "key": "查找关键字", "save": "结果保存变量名"},
    "constraints": "Type=数值等于key 时 key 必须能 double.Parse",
    "commonMistakes": "Type 与 key 内容类型要匹配；save 是变量名不是索引"
  },
  "获取结构体数量": {
    "purpose": "获取结构体总数和指定结构体的项数",
    "keyFields": {"TargetStructIndex": "目标结构体索引", "StructCount": "结构体总数保存变量", "ItemCount": "项数保存变量"},
    "constraints": "StructCount 和 ItemCount 都必填",
    "commonMistakes": "两个保存变量都要填"
  },
  "网口通讯操作": {
    "purpose": "启动或断开 TCP 连接",
    "keyFields": {"Count": "子项数量", "Params": "操作列表，每项含 Name(TCP对象名) 和 Ops(启动/断开)"},
    "constraints": "Name 必须是已配置的 TCP 对象；Ops 仅\"启动\"/\"断开\"",
    "commonMistakes": "Ops 不能填其他值；Name 不存在会报\"TCP配置不存在\""
  },
  "等待网口连接": {
    "purpose": "等待 TCP 连接建立成功",
    "keyFields": {"Count": "子项数量", "Params": "等待列表，每项含 Name 和 TimeOut(超时ms)"},
    "constraints": "Name 必须已配置；TimeOut 必须>0",
    "commonMistakes": "TimeOut 是每项内的字段；超时未连接报\"等待TCP连接超时\""
  },
  "发送TCP通讯消息": {
    "purpose": "发送 TCP 消息",
    "keyFields": {"ID": "TCP对象标识", "Msg": "发送内容来源变量名", "isConVert": "true 按16进制发送", "TimeOut": "超时(ms，默认3000)"},
    "constraints": "ID 必填；TimeOut 必须>0；Msg 是变量名(运行时取值)",
    "commonMistakes": "Msg 是变量名不是直接文本"
  },
  "接收TCP通讯消息": {
    "purpose": "接收 TCP 消息并写入变量",
    "keyFields": {"ID": "TCP对象标识", "MsgSaveValue": "接收结果保存变量名", "isConVert": "true 按16进制解析", "TImeOut": "超时(ms，默认3000，注意大小写拼写)"},
    "constraints": "必须已连接；TImeOut 必须>0",
    "commonMistakes": "字段名是 TImeOut(首字母大写 I)不是 Timeout"
  },
  "串口通讯操作": {
    "purpose": "启动或断开串口连接",
    "keyFields": {"Count": "子项数量", "Params": "操作列表，每项含 Name(串口对象名) 和 Ops(启动/断开)"},
    "constraints": "Name 必须是已配置的串口对象；Ops 仅\"启动\"/\"断开\"",
    "commonMistakes": "Ops 不能填其他值"
  },
  "等待串口连接": {
    "purpose": "等待串口连接建立成功",
    "keyFields": {"Count": "子项数量", "Params": "等待列表，每项含 Name 和 TimeOut(超时ms)"},
    "constraints": "Name 必须已配置；TimeOut 必须>0",
    "commonMistakes": "TimeOut<=0 报\"超时配置无效\""
  },
  "发送串口通讯消息": {
    "purpose": "发送串口消息",
    "keyFields": {"ID": "串口对象标识", "Msg": "发送内容来源变量名", "isConVert": "true 按16进制发送", "TimeOut": "超时(ms，默认3000)"},
    "constraints": "ID 必填；TimeOut 必须>0；Msg 是变量名",
    "commonMistakes": "Msg 是变量名不是直接文本"
  },
  "接收串口通讯消息": {
    "purpose": "接收串口消息并写入变量",
    "keyFields": {"ID": "串口对象标识", "MsgSaveValue": "接收结果保存变量名", "isConVert": "true 按16进制解析", "TImeOut": "超时(ms，默认3000，注意拼写)"},
    "constraints": "必须已打开；TImeOut 必须>0",
    "commonMistakes": "字段名是 TImeOut(首字母大写 I)不是 Timeout"
  },
  "发送与接收": {
    "purpose": "一次性完成发送+接收(TCP 或串口)",
    "keyFields": {"CommType": "通讯类型：TCP/串口", "ID": "通讯对象标识", "SendMsg": "发送内容来源变量名", "SendConvert": "true 按16进制发送", "ReceiveSaveValue": "接收结果保存变量名(可空仅发送)", "ReceiveConvert": "true 按16进制解析", "TimeOut": "超时(ms，默认3000)"},
    "constraints": "CommType 仅\"TCP\"/\"串口\"；TimeOut 必须>0；ReceiveSaveValue 可空",
    "commonMistakes": "CommType 决定 ID 可选范围"
  },
  "PLC读写": {
    "purpose": "读写 PLC 数据",
    "keyFields": {"PlcName": "PLC设备名", "DataType": "数据类型：String/Boolean/Byte/UShort/Short/UInt/Int/Float/Double", "DataOps": "读写方向：读PLC/写PLC/读写", "PlcAddress": "PLC首地址", "WriteConst": "写入常量(写时优先)", "ValueName": "本地变量名(读写均用)", "Quantity": "数据数量(>0)"},
    "constraints": "Quantity 必须>0；写PLC时 WriteConst 非空优先，否则读 ValueName；读PLC时 ValueName 必填；\"读写\"模式先写后读",
    "commonMistakes": "写PLC时 WriteConst 和 ValueName 同时填会优先用 WriteConst"
  },
  "创建料盘": {
    "purpose": "根据四角参考点生成料盘网格点阵",
    "keyFields": {"StationName": "工站名", "TrayId": "料盘ID(>=0)", "RowCount/ColCount": "行/列数(>0)", "PX1": "左上参考点", "PX2": "右上参考点", "PY1": "左下参考点", "PY2": "右下参考点"},
    "constraints": "RowCount/ColCount 必须>0；四个角点必须是工站已有点位且6轴数据完整",
    "commonMistakes": "四个角点必须都已存在于工站；点位轴数量不是6会报\"参考点轴数量异常\""
  },
  "走料盘点": {
    "purpose": "移动到料盘指定位置",
    "keyFields": {"StationName": "工站名", "TrayId": "料盘号(固定)", "TrayIdValueIndex/TrayIdValueName": "料盘号变量(与 TrayId 二选一)", "TrayPos": "料盘位置(固定，>0)", "TrayPosValueIndex/TrayPosValueName": "料盘位置变量(与 TrayPos 二选一)", "isUnWait": "true 不等待运动完成"},
    "constraints": "TrayId 和 TrayId 变量二选一(变量非空时 TrayId 必须=0)；TrayPos 同理；TrayId>=0，TrayPos>0",
    "commonMistakes": "固定值非0时不能再填变量字段"
  },
  "回原": {
    "purpose": "工站回零",
    "keyFields": {"StationName": "工站名", "StationIndex": "工站索引(!=-1 时优先，默认-1)", "StationHomeType": "回原模式：所有轴同步回/轴按优先顺序回", "isUnWait": "true 不等待完成"},
    "constraints": "StationIndex!=-1 时按索引取工站；回零前轴必须已到位",
    "commonMistakes": "StationHomeType 仅两个取值；轴未到位报\"轴未到位，禁止回零\""
  },
  "工站走点": {
    "purpose": "移动到工站预设点位",
    "keyFields": {"StationName": "工站名", "PosName": "点位名", "PosIndex": "点位索引(!=-1 时优先)", "isUnWait": "true 不等待", "isCheckInPos": "true 校验到位精度(0.01)", "timeOut/timeOutV": "超时(默认120000)及变量", "IsDisableAxis": "无禁用/有禁用", "Axis1-6": "禁用标志(true=禁用该轴)", "ChangeVel": "不改变/改变速度", "Vel/VelV": "速度(0-100)", "Acc/AccV": "加速度(0-100)", "Dec/DecV": "减速度(0-100)"},
    "constraints": "速度/加减速度范围 0-100(百分比)；Axis=true 的轴被跳过；timeOut<=0 读 timeOutV",
    "commonMistakes": "Axis=true 是\"禁用\"不是\"启用\"；速度值是百分比不是实际速度"
  },
  "点位修改": {
    "purpose": "修改工站点位坐标(覆盖或叠加)",
    "keyFields": {"StationName": "工站名", "RefPosName": "参考点：自定义坐标/当前位置/具体点位名", "TargetPosName": "待修改的目标点位", "ModifyType": "修改方式：叠加/替换", "CustomX/Y/Z/U/V/W": "自定义坐标"},
    "constraints": "RefPosName=自定义坐标 时用 Custom* 字段；ModifyType=替换 覆盖，=叠加 累加",
    "commonMistakes": "替换是覆盖目标点坐标；叠加是在目标点基础上加参考点值"
  },
  "获取工站位置": {
    "purpose": "获取当前位置或点位坐标，保存到点位或变量",
    "keyFields": {"StationName": "工站名", "SourceType": "获取方式：当前位置/指定点位", "SourcePosName": "指定点位名", "SaveType": "保存方式：保存到点位/保存到变量", "TargetPosName": "保存目标点位", "OutputXIndex/XName/Y.../W...": "各轴保存变量"},
    "constraints": "SourceType=指定点位 时 SourcePosName 必填；SaveType=保存到变量 时至少配置一个轴 Output 字段",
    "commonMistakes": "保存到变量时所有轴 Output 字段都未配置会报\"保存变量未配置\""
  },
  "偏移量": {
    "purpose": "按相对距离移动(各轴独立设距离)",
    "keyFields": {"StationName": "工站名", "isUnWait": "true 不等待", "isCheckInPos": "true 校验到位", "timeOut/timeOutV": "超时(默认120000)", "Axis1-6": "各轴相对距离(=0 时读 AxisV 变量)", "Axis1V-6V": "各轴距离变量名", "ChangeVel": "不改变/改变速度", "Vel/VelV": "速度(0-100)", "Acc/AccV": "加速度(0-100)", "Dec/DecV": "减速度(0-100)"},
    "constraints": "每轴 Axis 值=0 时读对应 AxisV 变量；速度/加减速度范围 0-100",
    "commonMistakes": "Axis=0 且 AxisV 未配会报错；距离是相对量不是绝对坐标"
  },
  "设置速度": {
    "purpose": "修改工站或单轴的运行速度参数",
    "keyFields": {"StationName": "工站名", "StationIndex": "工站索引(默认-1)", "SetAxisObj": "设置对象：工站 或 具体轴名", "Vel/VelV": "速度(0-100)", "Acc/AccV": "加速度(0-100)", "Dec/DecV": "减速度(0-100)"},
    "constraints": "SetAxisObj=工站 时修改全部轴；Vel=0 时读 VelV；速度范围 0-100",
    "commonMistakes": "速度值是百分比(0-100)不是实际速度"
  },
  "停止运动": {
    "purpose": "停止工站运动(整站或按轴)",
    "keyFields": {"StationName": "工站名", "isAllStop": "true 整站停止", "Axis1-6": "轴停止标志(isAllStop=false 时，true=停止该轴)"},
    "constraints": "isAllStop=true 时停止所有轴；=false 时按 Axis=true 停止对应轴",
    "commonMistakes": "Axis1-6 仅在 isAllStop=false 时生效"
  },
  "等待运动": {
    "purpose": "等待工站运动停止(到位或回零完成)",
    "keyFields": {"StationName": "工站名", "StationIndex": "工站索引(默认-1)", "isWaitHome": "true 等待回零完成，false 等待到位", "timeOut/timeOutV": "超时(默认120000)及变量"},
    "constraints": "isWaitHome=true 时检查 HomeStatus；=false 时检查 GetInPos；timeOut<=0 读 timeOutV",
    "commonMistakes": "timeOut 默认 120000；等待回零和等待到位是两种不同判定逻辑"
  }
}
""";

        [McpServerTool, Description(
            "读取当前可引用对象目录，例如变量、IO、工站、通讯、PLC、报警编号等。修改引用字段前应先读取。")]
        public static async Task<string> GetReferenceCatalog(
            [Description("可选流程索引。某些目录可能按流程裁剪。")] int? procIndex = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetReferenceCatalog),
                args: new { procIndex },
                action: client => client.GetReferenceCatalogAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "列出本地可用的中间意图 JSON 模板。模型在生成写入意图前应先读取模板，而不是依赖提示词中的长示例。")]
        public static async Task<string> ListIntentTemplates(
            [Description("可选 Patch 动作名过滤，例如 update_operation_fields。")] string? patchAction = null)
        {
            return await ExecuteAsync(
                toolName: nameof(ListIntentTemplates),
                args: new { patchAction },
                action: client => client.ListIntentTemplatesAsync(patchAction)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "读取单个中间意图 JSON 模板。可按 templateId 精确读取，也可按 patchAction 获取对应模板。")]
        public static async Task<string> GetIntentTemplate(
            [Description("模板 ID，例如 update_operation_field。")] string? templateId = null,
            [Description("Patch 动作名，例如 update_operation_fields。")] string? patchAction = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetIntentTemplate),
                args: new { templateId, patchAction },
                action: client => client.GetIntentTemplateAsync(templateId, patchAction)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "把中间意图 JSON 转换成标准 patchJson。适合先用模板约束输出，再由本地统一组装 Patch。")]
        public static async Task<string> BuildPatchFromIntent(
            [Description("中间意图 JSON。必须符合 get_intent_template 返回的 intentShape。")] string intentJson)
        {
            return await ExecuteAsync(
                toolName: nameof(BuildPatchFromIntent),
                args: new { intentJson },
                action: client => client.BuildPatchFromIntentAsync(intentJson)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "使用中间意图 JSON 直接预演，不需要模型自行组装 patchJson。内部会先把意图转换成标准 Patch，再执行 preview。")]
        public static async Task<string> PreviewIntent(
            [Description("中间意图 JSON。必须符合 get_intent_template 返回的 intentShape。")] string intentJson)
        {
            return await ExecuteAsync(
                toolName: nameof(PreviewIntent),
                args: new { intentJson },
                action: client => client.PreviewIntentAsync(intentJson)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "使用中间意图 JSON 直接提交。必须携带 Automation 前台已确认的 previewId，且意图内容必须与预演时完全一致。")]
        public static async Task<string> ApplyIntent(
            [Description("中间意图 JSON。必须与预演通过的意图保持一致。")] string intentJson,
            [Description("Automation 前台确认预演后返回或提示的 previewId。")] string previewId)
        {
            return await ExecuteAsync(
                toolName: nameof(ApplyIntent),
                args: new { intentJson, previewId },
                action: client => client.ApplyIntentAsync(intentJson, previewId)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "预演结构化 Patch，不会落盘。patchJson 必须是完整 JSON 对象，至少包含 procIndex、baseProcId、actions。提交前必须先调用。")]
        public static async Task<string> PreviewPatch(
            [Description("结构化 Patch JSON。示例：{\"procIndex\":0,\"baseProcId\":\"guid\",\"actions\":[...]}")] string patchJson)
        {
            return await ExecuteAsync(
                toolName: nameof(PreviewPatch),
                args: new { patchJson },
                action: client => client.PreviewPatchAsync(patchJson)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "应用结构化 Patch，触发保存与发布。必须携带 Automation 前台已确认的 previewId，且 patchJson 必须与预演时完全一致。")]
        public static async Task<string> ApplyPatch(
            [Description("结构化 Patch JSON。必须与预演通过的 patch 保持一致。")] string patchJson,
            [Description("Automation 前台确认预演后返回或提示的 previewId。")] string previewId)
        {
            return await ExecuteAsync(
                toolName: nameof(ApplyPatch),
                args: new { patchJson, previewId },
                action: client => client.ApplyPatchAsync(patchJson, previewId)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "读取 Automation 当前运行快照，包括流程状态、当前位置、报警状态、选中项和安全锁定状态。")]
        public static async Task<string> GetRuntimeSnapshot(
            [Description("可选流程索引。从 0 开始；为空时返回全部流程快照。")] int? procIndex = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetRuntimeSnapshot),
                args: new { procIndex },
                action: client => client.GetRuntimeSnapshotAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "读取 Automation 运行信息页最近日志。用于排查报警、Bridge/MCP 调用失败和流程运行异常。")]
        public static async Task<string> GetInfoLogTail(
            [Description("返回条数，范围 1..200。")] int maxCount = 50)
        {
            return await ExecuteAsync(
                toolName: nameof(GetInfoLogTail),
                args: new { maxCount },
                action: client => client.GetInfoLogTailAsync(maxCount)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "诊断指定流程的结构与运行风险，包括禁用、空步骤/指令、未知指令类型、跳转错误、报警和断点。")]
        public static async Task<string> DiagnoseProc(
            [Description("流程索引，从 0 开始。")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(DiagnoseProc),
                args: new { procIndex },
                action: client => client.DiagnoseProcAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "创建新流程。预演后需等待 Automation 前台确认 previewId，再调用 apply 创建。流程名不能与现有流程重复。")]
        public static async Task<string> CreateProc(
            [Description("流程名称。")] string name,
            [Description("是否自启动。默认 false。")] bool? autoStart = null,
            [Description("是否禁用。默认 false。")] bool? disable = null)
        {
            var payload = new { name, autoStart, disable };
            string payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
            return await ExecuteAsync(
                toolName: nameof(CreateProc),
                args: payload,
                action: client => client.ManageProcPreviewAsync("create_proc", payloadJson)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "创建流程的提交。必须携带 Automation 前台已确认的 previewId。")]
        public static async Task<string> ApplyCreateProc(
            [Description("流程名称。")] string name,
            [Description("是否自启动。")] bool? autoStart = null,
            [Description("是否禁用。")] bool? disable = null,
            [Description("Automation 前台确认的 previewId。")] string previewId = "")
        {
            var payload = new { name, autoStart, disable };
            string payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
            return await ExecuteAsync(
                toolName: nameof(ApplyCreateProc),
                args: new { name, autoStart, disable, previewId },
                action: client => client.ManageProcApplyAsync("create_proc", payloadJson, previewId)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "批量删除流程。预演后需等待 Automation 前台确认 previewId，再调用 apply 删除。正在运行的流程无法删除。")]
        public static async Task<string> DeleteProcs(
            [Description("要删除的流程索引数组，例如 [0, 2, 3]。")] int[] procIndexes)
        {
            var payload = new { procIndexes };
            string payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
            return await ExecuteAsync(
                toolName: nameof(DeleteProcs),
                args: payload,
                action: client => client.ManageProcPreviewAsync("delete_procs", payloadJson)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "批量删除流程的提交。必须携带 Automation 前台已确认的 previewId。")]
        public static async Task<string> ApplyDeleteProcs(
            [Description("要删除的流程索引数组。")] int[] procIndexes,
            [Description("Automation 前台确认的 previewId。")] string previewId)
        {
            var payload = new { procIndexes };
            string payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
            return await ExecuteAsync(
                toolName: nameof(ApplyDeleteProcs),
                args: new { procIndexes, previewId },
                action: client => client.ManageProcApplyAsync("delete_procs", payloadJson, previewId)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "重排流程位置。预演后需等待 Automation 前台确认 previewId，再调用 apply 执行。")]
        public static async Task<string> ReorderProc(
            [Description("要移动的流程索引。")] int procIndex,
            [Description("目标位置索引。")] int targetIndex)
        {
            var payload = new { procIndex, targetIndex };
            string payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
            return await ExecuteAsync(
                toolName: nameof(ReorderProc),
                args: payload,
                action: client => client.ManageProcPreviewAsync("reorder_proc", payloadJson)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "重排流程位置的提交。必须携带 Automation 前台已确认的 previewId。")]
        public static async Task<string> ApplyReorderProc(
            [Description("要移动的流程索引。")] int procIndex,
            [Description("目标位置索引。")] int targetIndex,
            [Description("Automation 前台确认的 previewId。")] string previewId)
        {
            var payload = new { procIndex, targetIndex };
            string payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
            return await ExecuteAsync(
                toolName: nameof(ApplyReorderProc),
                args: new { procIndex, targetIndex, previewId },
                action: client => client.ManageProcApplyAsync("reorder_proc", payloadJson, previewId)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "复制现有流程为新流程。预演后需等待 Automation 前台确认 previewId，再调用 apply 执行。")]
        public static async Task<string> CopyProc(
            [Description("源流程索引。")] int procIndex,
            [Description("新流程名称，为空时自动追加 _副本。")] string? newName = null)
        {
            var payload = new { procIndex, newName };
            string payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
            return await ExecuteAsync(
                toolName: nameof(CopyProc),
                args: payload,
                action: client => client.ManageProcPreviewAsync("copy_proc", payloadJson)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "复制流程的提交。必须携带 Automation 前台已确认的 previewId。")]
        public static async Task<string> ApplyCopyProc(
            [Description("源流程索引。")] int procIndex,
            [Description("新流程名称。")] string? newName = null,
            [Description("Automation 前台确认的 previewId。")] string previewId = "")
        {
            var payload = new { procIndex, newName };
            string payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
            return await ExecuteAsync(
                toolName: nameof(ApplyCopyProc),
                args: new { procIndex, newName, previewId },
                action: client => client.ManageProcApplyAsync("copy_proc", payloadJson, previewId)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "控制流程运行：启动(start)、停止(stop)、暂停(pause)、恢复(resume)。不需要预演确认。")]
        public static async Task<string> ControlProc(
            [Description("流程索引，从 0 开始。")] int procIndex,
            [Description("控制动作：start、stop、pause、resume。")] string action)
        {
            return await ExecuteAsync(
                toolName: nameof(ControlProc),
                args: new { procIndex, action },
                action: client => client.ControlProcAsync(procIndex, action)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "返回 Automation MCP 的调用约束与 Patch 示例，用于提示 LLM 先读后改、先预演后提交。")]
        public static string GetPatchContract()
        {
            const string contract = """
{
  "domainConcepts": {
    "程序": "指整个 Automation 应用，包含所有流程、变量表、IO配置、通讯配置等。不是单个流程。AI 无法直接修改程序本身，只能通过修改流程、控制流程运行来操作。",
    "流程(Proc)": "Automation 中独立的执行单元，每个流程有自己的名称、自启动/禁用属性。一个程序可包含多个流程。流程由步骤组成。procIndex 是流程在列表中的位置索引（从0开始），procId 是流程的唯一 Guid 标识。",
    "步骤(Step)": "流程内的逻辑分组，包含名称和禁用属性。一个流程可有多个步骤。步骤由指令组成。stepId 是步骤的唯一 Guid 标识。步骤用于组织相关指令、标记流程执行阶段。",
    "指令(Operation)": "最小执行单元，具体的自动化动作（如 IO检测、等待、GOTO跳转、赋值等）。每个指令有 operaType（类型）和对应的字段参数。opId 是指令的唯一标识。新增指令前必须用 list_operation_types 查可用类型，用 get_operation_schema 查字段定义。",
    "层次关系": "程序 > 流程(Proc) > 步骤(Step) > 指令(Operation)",
    "运行时状态": "流程运行状态：Stopped(停止)、Paused(暂停)、Running(运行)、Alarming(报警)。只有 Stopped 状态的流程才能修改结构。"
  },
  "workflow": [
    "1. list_procs 或 get_proc_overview 定位目标流程",
    "2. get_proc_detail 读取完整结构",
    "3. get_operation_guide 读取指令调用说明（用途/字段/约束/常见误用），get_operation_schema / get_reference_catalog 获取字段约束与候选值",
    "4. list_intent_templates / get_intent_template 读取中间意图模板",
    "5. preview_intent 预演，或 build_patch_from_intent 后再 preview_patch",
    "6. Automation 前台确认 previewId 后，apply_intent 或 apply_patch 携带同一个 previewId 提交",
    "流程级管理：create_proc/delete_procs/reorder_proc/copy_proc 预演后等待确认 previewId，再调用 apply_* 提交",
    "流程运行控制：control_proc 直接执行 start/stop/pause/resume，不需要预演确认"
  ],
  "preferredWritePath": [
    "优先使用中间意图：get_intent_template -> preview_intent -> 等待 Automation 确认 previewId -> apply_intent",
    "仅在已经有标准 patchJson 时再直接调用 preview_patch -> 等待 Automation 确认 previewId -> apply_patch",
    "流程结构操作：create_proc/delete_procs/reorder_proc/copy_proc -> 等待 Automation 确认 previewId -> apply_create_proc/apply_delete_procs/apply_reorder_proc/apply_copy_proc"
  ],
  "supportedActions": [
    "update_proc_head_fields",
    "update_step_fields",
    "update_operation_fields",
    "append_step",
    "insert_step",
    "delete_step",
    "move_step",
    "append_operation",
    "insert_operation",
    "delete_operation",
    "move_operation"
  ],
  "procManagementActions": [
    "create_proc",
    "delete_procs",
    "reorder_proc",
    "copy_proc"
  ],
  "controlActions": [
    "start",
    "stop",
    "pause",
    "resume"
  ],
  "patchShape": {
    "procIndex": 0,
    "baseProcId": "guid",
    "actions": [
      {
        "type": "move_operation",
        "stepId": "guid",
        "opId": "guid",
        "targetStepId": "guid",
        "targetIndex": 2,
        "expectedOperaType": "IO检测"
      },
      {
        "type": "insert_operation",
        "stepId": "guid",
        "insertIndex": 3,
        "operaType": "IO检测",
        "fieldValues": {
          "timeOutC_TimeOut": 5000
        }
      }
    ]
  },
  "rules": [
    "优先使用中间意图工具，减少模型直接拼装 patchJson 的自由度",
    "不要直接改原始流程 JSON 文件",
    "不要假设流程名、步骤名、指令名唯一",
    "字段名必须使用 get_proc_detail.fields 或 get_operation_schema.fields.key 返回的精确键名",
    "不要在未读取 schema 的情况下猜字段名或枚举值",
    "actions[] 中的动作键必须使用 type，禁止使用旧字段 action",
    "update_*_fields 必须使用 fieldChanges，insert/append_operation 需要初始字段时必须使用 fieldValues，禁止使用旧字段 fields",
    "apply_patch 必须复用原始 patch，不能把 preview_patch 返回结果里的 changes 直接当作 actions 提交",
    "preview_intent/preview_patch 会返回 previewId 和 patchHash，提交必须携带 Automation 前台已确认的 previewId",
    "未预演、预演未确认、Patch 与预演不一致、流程版本变化都会导致 apply 失败",
    "delete/move/insert 会触发 Automation Bridge 自动重写同流程内的跳转地址",
    "move_step/move_operation 的 targetIndex 表示移除源项后的最终索引",
    "apply_patch 前必须先调用 preview_patch 并等待 Automation 前台确认",
    "流程结构操作（create_proc/delete_procs/reorder_proc/copy_proc）需先预演再 apply，apply 携带已确认的 previewId",
    "delete_procs/reorder_proc 操作正在运行的流程会失败，需先 control_proc stop 停止",
    "control_proc 不需要预演确认，直接发送 start/stop/pause/resume 命令",
    "reorder_proc 的 targetIndex 是移动后的最终索引，必须在当前流程数量范围内"
  ]
}
""";
            ToolCallLogger.Log(nameof(GetPatchContract), new { }, contract);
            return contract;
        }

        private static async Task<string> ExecuteAsync(string toolName, object args, Func<AutomationBridgeClient, Task<string>> action)
        {
            try
            {
                string result = await action(AutomationMcpRuntime.GetBridgeClient()).ConfigureAwait(false);
                ToolCallLogger.Log(toolName, args, result);
                return result;
            }
            catch (Exception ex)
            {
                string error = $$"""
{"ok":false,"type":"mcp.error","message":"{{toolName}} 调用异常","details":"{{Escape(ex.Message)}}"}
""";
                ToolCallLogger.Log(toolName, args, string.Empty, ex.Message);
                return error;
            }
        }

        private static string Escape(string text)
        {
            return (text ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
        }
    }
}
