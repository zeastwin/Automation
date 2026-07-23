// 模块：共享模型 / 指令。
// 职责范围：定义流程指令、参数特性和当前编辑器元数据；由 Engine、Bridge 与 Editor 共同使用。

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static Automation.OperationTypePartial;
using Automation;

namespace Automation
{
    
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class MarkedGotoAttribute : Attribute
    {
        public string Label { get; }

        public MarkedGotoAttribute(string label)
        {
            Label = label;
        }
    }

    //顺序： 1 地址 2 二级地址 3 名称 4 变量
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class ItemTypeAttribute : Attribute
    {
        public string Label { get; }

        public ItemTypeAttribute(string label)
        {
            Label = label;
        }
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class InlineListAttribute : Attribute
    {
        public InlineListAttribute(string displayName, string category)
        {
            DisplayName = displayName;
            Category = category;
        }

        public string DisplayName { get; }
        public string Category { get; }
        public int MinItems { get; set; }
        public int MaxItems { get; set; } = int.MaxValue;
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class NumericRangeAttribute : Attribute
    {
        public NumericRangeAttribute(double minimum)
            : this(minimum, double.PositiveInfinity)
        {
        }

        public NumericRangeAttribute(double minimum, double maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
        }

        public double Minimum { get; }
        public double Maximum { get; }

        public bool Contains(object value)
        {
            if (value == null)
            {
                return true;
            }
            double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return !double.IsNaN(number) && !double.IsInfinity(number)
                && number >= Minimum && number <= Maximum;
        }

        public string Describe()
        {
            return double.IsPositiveInfinity(Maximum)
                ? $"不小于 {Minimum.ToString(CultureInfo.InvariantCulture)}"
                : $"{Minimum.ToString(CultureInfo.InvariantCulture)}..{Maximum.ToString(CultureInfo.InvariantCulture)}";
        }
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class InlineGroupAttribute : Attribute
    {
        public InlineGroupAttribute(string displayName, string category)
        {
            DisplayName = displayName;
            Category = category;
        }

        public string DisplayName { get; }
        public string Category { get; }
    }

    public interface IPropertyVisibilityProvider
    {
        bool IsPropertyVisible(string propertyName, bool defaultVisibility);
    }


    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class TimeoutSetting
    {
        public const int DefaultTimeoutMs = 3000;

        [DisplayName("超时"), Category("参数"), Description("超时时间（ms）；超过该时长仍未满足条件会触发报警。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TimeoutMs { get; set; } = DefaultTimeoutMs;
        [DisplayName("超时变量"), Category("参数"), Description("超时时间变量名；当固定超时小于等于0时从该变量读取。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string TimeoutVariableName { get; set; }

        public override string ToString()
        {
            return "";
        }
    }

    public delegate void EventRefreshInspector();
    [Serializable]
    public class OperationType : ICloneable
    {
        // 使用隐藏前缀保持分类排序，检查器显示时仍规范化为“常规”。
        public const string GeneralCategory = "\uFEFF常规";

        public OperationType() 
        {
            RefreshInspector += RefleshPropertyAlarm;
        }
        [Browsable(false)]
        [JsonIgnore]
        public EventRefreshInspector RefreshInspector;

        [NonSerialized]
        private object runtimeBinding;

        [Browsable(false)]
        [JsonIgnore]
        internal object RuntimeBinding
        {
            get => runtimeBinding;
            set => runtimeBinding = value;
        }

        [Browsable(false)]
        [JsonIgnore]
        protected PlatformRuntime EditorRuntime => EditorServiceRegistry.GetRuntime(this);

        [Browsable(false)]
        [JsonIgnore]
        protected bool IsOperationEditorActive => EditorRuntime?.Editor.ModifyKind == ModifyKind.Operation
            || EditorRuntime?.Editor.IsAddingOperations == true;

        [Browsable(false)]
        [JsonIgnore]
        protected List<DataStation> EditorStations => EditorRuntime?.Stores.Stations.Items
            ?? new List<DataStation>();

        [Browsable(false)]
        public Guid Id { get; set; }

        [Browsable(false)]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string AiKey { get; set; }

        [Browsable(true)]
        [DisplayName("编号"), Category(GeneralCategory), Description("步骤内指令顺序号，用于定位与跳转校验，通常由系统维护。"), ReadOnly(true)]
        public int Num { get; set; }
        [Browsable(true)]
        [DisplayName("名称"), Category("A.指令与报警"), Description("指令显示名称，用于编辑识别与日志定位，不改变执行类型。"), ReadOnly(false)]
        public virtual string Name { get; set; }

        [Browsable(true)]
        [DisplayName("操作类型"), Category(GeneralCategory), Description("当前指令类型标识（只读），决定引擎执行分支。"), ReadOnly(true)]
        public string OperaType { get; set; }

        private string alarmType = "报警停止";
        [DisplayName("报警类型"), Category("A.指令与报警"), Description("异常时的处理策略（停止/忽略/自动/弹框），直接影响故障处置流程。"), ReadOnly(false), TypeConverter(typeof(AlarmItem))]
        [Browsable(true)]
        public string AlarmType
        {
            get => alarmType;
            set
            {
                if (alarmType != value)
                {
                    alarmType = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshPropertyAlarm();
                    }
                }
            }
        }

        public void RefleshPropertyAlarm()
        {
            if (this.alarmType == "报警停止")
            {
                SetPropertyAttribute(this, "AlarmInfoId", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto1", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto2", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto3", typeof(BrowsableAttribute), "browsable", false);
            }
            else if (this.alarmType == "报警忽略")
            {
                SetPropertyAttribute(this, "AlarmInfoId", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto1", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto2", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto3", typeof(BrowsableAttribute), "browsable", false);
            }
            else if (this.alarmType == "自动处理")
            {
                SetPropertyAttribute(this, "AlarmInfoId", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto1", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto2", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto3", typeof(BrowsableAttribute), "browsable", false);
            }
            else if (this.alarmType == "弹框确定")
            {
                SetPropertyAttribute(this, "AlarmInfoId", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto1", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto2", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto3", typeof(BrowsableAttribute), "browsable", false);
            }
            else if (this.alarmType == "弹框确定与否")
            {
                SetPropertyAttribute(this, "AlarmInfoId", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto1", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto2", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto3", typeof(BrowsableAttribute), "browsable", false);
            }
            else if (this.alarmType == "弹框确定与否与取消")
            {
                SetPropertyAttribute(this, "AlarmInfoId", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto1", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto2", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto3", typeof(BrowsableAttribute), "browsable", true);
            }
        }
        [DisplayName("报警信息ID"), Category(GeneralCategory), Description("弹框报警时使用的报警信息编号；仅在弹框类报警策略下生效。"), ReadOnly(false), TypeConverter(typeof(AlarmInfoItem))]
        [Browsable(false)]
        public string AlarmInfoId { get; set; }
        [Browsable(false)]
        [DisplayName("确定跳转"), Category(GeneralCategory), Description("在弹框或自动处理选择“确定”后跳转到的目标位置。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string Goto1 { get; set; }
        [Browsable(false)]
        [DisplayName("否跳转"), Category(GeneralCategory), Description("在弹框选择“否”后跳转到的目标位置。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string Goto2 { get; set; }
        [Browsable(false)]
        [DisplayName("取消跳转"), Category(GeneralCategory), Description("在弹框选择“取消”后跳转到的目标位置。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string Goto3 { get; set; }
        [Browsable(true)]
        [DisplayName("备注"), Category(GeneralCategory), Description("编辑备注说明，仅用于维护与排查，不参与运行计算。"), ReadOnly(false)]
        public string Note { get; set; }
        [Browsable(true)]
        [DisplayName("断点"), Category(GeneralCategory), Description("启用后流程运行到该指令会进入断点状态，便于调试。"), ReadOnly(false)]
        public bool IsBreakpoint { get; set; }

        [Browsable(true)]
        [DisplayName("禁用"), Category(GeneralCategory), Description("启用后该指令会被跳过执行，用于临时屏蔽逻辑。"), ReadOnly(false)]
        public bool Disable { get; set; }

        public object Clone()
        {
            return ObjectGraphCloner.Clone(this);
        }

        protected static double ValidatePercentage(double value, string propertyName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 100)
            {
                throw new ArgumentOutOfRangeException(
                    propertyName,
                    value,
                    "百分比必须在0到100之间。");
            }
            return value;
        }

        public void SetPropertyAttribute(object obj, string propertyName, Type attrType, string attrField, object value)
        {
            if (obj == null || string.IsNullOrEmpty(propertyName) || attrType == null)
            {
                return;
            }

            PropertyDescriptorCollection props = TypeDescriptor.GetProperties(obj);
            PropertyDescriptor prop = props[propertyName];
            if (prop == null)
            {
                return;
            }
            Attribute attr = prop.Attributes[attrType];
            if (attr == null)
            {
                return;
            }
            FieldInfo field = attrType.GetField(attrField, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                return;
            }
            field.SetValue(attr, value);
        }

        //更改某个参数的显示名称
        public void SetDisplayName(Type propertyType, string PropertyName, string disPlayName)
        {
            object obj = propertyType.IsInstanceOfType(this) ? this : null;
            if (obj == null)
            {
                return;
            }
            PropertyDescriptor descriptor = TypeDescriptor.GetProperties(obj)[PropertyName];
            if (descriptor == null)
            {
                return;
            }
            DisplayNameAttribute attribute = descriptor.Attributes[typeof(DisplayNameAttribute)] as DisplayNameAttribute;
            if (attribute == null)
            {
                return;
            }
            FieldInfo field = attribute.GetType().GetField("_displayName", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(attribute, disPlayName);
        }

    }

    [Serializable]
    public abstract class CommunicationOperationType : OperationType
    {
        [DisplayName("重试次数"), Category("失败重试"), Description("通信失败后的额外重试次数；0表示只执行一次，并跳过接收结果判定。"), ReadOnly(false), RefreshProperties(RefreshProperties.All)]
        [NumericRange(0, 10)]
        public int RetryCount { get; set; }

        [DisplayName("重试间隔(ms)"), Category("失败重试"), Description("每次失败后到下一次通信之间的固定等待时间，不使用退避。"), ReadOnly(false)]
        [NumericRange(0, 60000)]
        public int RetryIntervalMs { get; set; }
    }

    [Serializable]
    public abstract class ResponseCommunicationOperationType : CommunicationOperationType, IPropertyVisibilityProvider
    {
        protected ResponseCommunicationOperationType()
        {
            ParamListConverter<CommunicationResponseCondition>.Name = "结果判定";
            ResponseConditions = new CustomList<CommunicationResponseCondition>();
        }

        [DisplayName("结果判定"), Category("接收结果判定"), Description("仅重试次数大于0时执行；支持变量值、文本和JSON字段判定，失败后按固定间隔重试通信。"), ReadOnly(false)]
        [InlineList("结果判定", "接收结果判定", MaxItems = 20)]
        [TypeConverter(typeof(ParamListConverter<CommunicationResponseCondition>))]
        public CustomList<CommunicationResponseCondition> ResponseConditions { get; set; }

        public virtual bool IsPropertyVisible(string propertyName, bool defaultVisibility)
        {
            return propertyName != nameof(ResponseConditions) || RetryCount > 0;
        }

        public virtual bool ShouldSerializeResponseConditions() => RetryCount > 0;

        internal virtual bool ShouldEvaluateResponseConditions => true;
    }

    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public sealed class CommunicationResponseCondition : IPropertyVisibilityProvider
    {
        private string judgeMode = "非空";

        [DisplayName("来源变量"), Category("判定"), Description("接收结果或PLC读取结果所在变量。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string SourceVariableName { get; set; }

        [DisplayName("JSON字段路径"), Category("判定"), Description("可选，按点分隔的精确字段路径，例如 data.code；为空时判断整个变量值。"), ReadOnly(false)]
        public string JsonFieldPath { get; set; }

        [DisplayName("判断模式"), Category("判定"), Description("支持非空、字段存在、文本相等/包含和数值区间判断。"), ReadOnly(false), RefreshProperties(RefreshProperties.All), TypeConverter(typeof(CommunicationResponseJudgeModeItem))]
        public string JudgeMode
        {
            get => judgeMode;
            set => judgeMode = value;
        }

        [DisplayName("上限"), Category("判定"), Description("数值区间判断的上限。"), ReadOnly(false)]
        public double Up { get; set; }

        [DisplayName("下限"), Category("判定"), Description("数值边界或区间下限。"), ReadOnly(false)]
        public double Down { get; set; }

        [DisplayName("期望文本"), Category("判定"), Description("文本相等或包含模式下的期望内容。"), ReadOnly(false)]
        public string ExpectedText { get; set; }

        [DisplayName("包含边界"), Category("判定"), Description("数值判断时是否包含等号。"), ReadOnly(false)]
        public bool IncludeBoundary { get; set; }

        [DisplayName("运算符"), Category("判定"), Description("与上一条条件使用“且”或“或”组合；第一条忽略此字段。"), ReadOnly(false), TypeConverter(typeof(Operator))]
        public string Operator { get; set; } = "且";

        public bool IsPropertyVisible(string propertyName, bool defaultVisibility)
        {
            bool numeric = judgeMode == "值在区间左" || judgeMode == "值在区间右"
                || judgeMode == "值在区间内";
            bool text = judgeMode == "等于特征字符" || judgeMode == "包含特征字符";
            switch (propertyName)
            {
                case nameof(Up):
                    return judgeMode == "值在区间内";
                case nameof(Down):
                case nameof(IncludeBoundary):
                    return numeric;
                case nameof(ExpectedText):
                    return text;
                default:
                    return defaultVisibility;
            }
        }

        public override string ToString() => string.Empty;
    }
    [Serializable]
    public class CallCustomFunc : OperationType
    {
        public CallCustomFunc()
        {
            OperaType = "自定义方法";
        }
        [DisplayName("函数名称"), Category("参数"), Description("通过平台公开接口注册的自定义函数精确名称。"), ReadOnly(false), TypeConverter(typeof(funcNameItem))]
        public string FunctionName { get; set; }

    }
    [Serializable]
    public class IoOperate : OperationType
    {
        public IoOperate()
        {
            OperaType = "IO操作";
            ParamListConverter<IoOutParam>.Name = "IO";
            IoParams = new CustomList<IoOutParam>
            {
                new IoOutParam()
            };
        }
        [DisplayName("IO设置"), Category("参数"), Description("同一卡输出项配置入口；全部输出通过一次端口写入同步切换。"), ReadOnly(false)]
        [InlineList("IO", "参数")]
        [TypeConverter(typeof(ParamListConverter<IoOutParam>))]

        public CustomList<IoOutParam> IoParams { get; set; }
    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class IoOutParam
    {

        [DisplayName("名称"), Category("IO参数"), Description("输出IO点名称；运行时向该输出点写入目标状态。"), ReadOnly(false), TypeConverter(typeof(IoOutItem))]
        public string IoName { get; set; }
        [DisplayName("写入值"), Category("IO参数"), Description("IO输出目标状态（开/关）。"), ReadOnly(false)]
        public bool TargetState { get; set; }
        public override string ToString()
        {
            return "";
        }

    }
    [Serializable]
    public class IoCheck : OperationType
    {
        public IoCheck()
        {
            OperaType = "IO检测";
            ParamListConverter<IoCheckParam>.Name = "IO";
            IoParams = new CustomList<IoCheckParam>
            {
                new IoCheckParam()
            };
        }
        [DisplayName("超时设置"), Category("参数"), Description("超时参数组，用于配置固定超时或变量超时来源。"), ReadOnly(true)]
        [InlineGroup("超时设置", "参数")]
        public TimeoutSetting Timeout { get; set; } = new TimeoutSetting();
        [DisplayName("IO设置"), Category("参数"), Description("IO检测项配置入口；每一项定义一个输入点及期望状态。"), ReadOnly(false)]
        [InlineList("IO", "参数")]
        [TypeConverter(typeof(ParamListConverter<IoCheckParam>))]

        public CustomList<IoCheckParam> IoParams { get; set; }

    }

    [Serializable]
    public class IoGroup : OperationType
    {
        public IoGroup()
        {
            OperaType = "IO组";
            ParamListConverter<IoOutParam>.Name = "IO";
            ParamListConverter<IoCheckParam>.Name = "IO";
            OutIoParams = new CustomList<IoOutParam>
            {
                new IoOutParam()
            };
            CheckIoParams = new CustomList<IoCheckParam>
            {
                new IoCheckParam()
            };
        }

        [DisplayName("超时设置"), Category("参数"), Description("超时参数组，用于配置固定超时或变量超时来源。"), ReadOnly(true)]
        [InlineGroup("超时设置", "参数")]
        public TimeoutSetting Timeout { get; set; } = new TimeoutSetting();

        [DisplayName("输出设置"), Category("参数"), Description("IO组同一卡输出配置；全部输出通过一次端口写入同步切换。"), ReadOnly(false)]
        [InlineList("输出IO", "参数")]
        [TypeConverter(typeof(ParamListConverter<IoOutParam>))]
        public CustomList<IoOutParam> OutIoParams { get; set; }

        [DisplayName("检测设置"), Category("参数"), Description("IO组检测子项配置入口；用于定义检测点与通过条件。"), ReadOnly(false)]
        [InlineList("检测IO", "参数")]
        [TypeConverter(typeof(ParamListConverter<IoCheckParam>))]
        public CustomList<IoCheckParam> CheckIoParams { get; set; }
    }

    [Serializable]
    public class IoLogicGoto : OperationType
    {
        public IoLogicGoto()
        {
            OperaType = "IO逻辑跳转";
            ParamListConverter<IoLogicGotoParam>.Name = "IO";
            IoParams = new CustomList<IoLogicGotoParam>
            {
                new IoLogicGotoParam()
            };
        }

        [DisplayName("失败延时(ms)"), Category("B判断参数"), Description("判断失败后的重试延时（ms），用于控制循环检测节奏。"), ReadOnly(false)]
        [NumericRange(0)]
        public int InvalidDelayMs { get; set; }

        [DisplayName("IO设置"), Category("B判断参数"), Description("IO判断项配置入口。"), ReadOnly(false)]
        [InlineList("IO", "B判断参数")]
        [TypeConverter(typeof(ParamListConverter<IoLogicGotoParam>))]
        public CustomList<IoLogicGotoParam> IoParams { get; set; }

        [DisplayName("true跳转"), Category("A跳转位置"), Description("逻辑判断为真时跳转到的目标位置。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string TrueGoto { get; set; }

        [DisplayName("false跳转"), Category("A跳转位置"), Description("逻辑判断为假时跳转到的目标位置。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string FalseGoto { get; set; }
    }

    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class IoLogicGotoParam
    {
        [DisplayName("名称"), Category("IO参数"), Description("输入IO点名称；运行时读取该输入点的逻辑状态。"), ReadOnly(false), TypeConverter(typeof(IoInItem))]
        public string IoName { get; set; }

        [DisplayName("目标"), Category("IO参数"), Description("逻辑判断目标值。"), ReadOnly(false)]
        public bool Target { get; set; }

        [DisplayName("逻辑"), Category("IO参数"), Description("比较逻辑（等于/不等于等），用于IO条件判断。"), ReadOnly(false), TypeConverter(typeof(LogicOperator))]
        public string Logic { get; set; } = "与";

        public override string ToString()
        {
            return "";
        }
    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class IoCheckParam
    {
        [DisplayName("名称"), Category("IO参数"), Description("输入IO点名称；运行时读取该输入点的逻辑状态。"), ReadOnly(false), TypeConverter(typeof(IoInItem))]
        public string IoName { get; set; }
        [DisplayName("检测状态"), Category("IO参数"), Description("期望输入状态；达到该状态判定检测通过。"), ReadOnly(false)]
        public bool ExpectedState { get; set; }
        public override string ToString()
        {
            return "";
        }

    }
    [Serializable]
    public class ProcOps : OperationType
    {
        public ProcOps()
        {
            OperaType = "流程操作";
            ParamListConverter<ProcParam>.Name = "流程";
            Params = new CustomList<ProcParam>
            {
                new ProcParam { DelayAfterMs = -1 }
            };
        }
        [DisplayName("设置"), Category("参数"), Description("子项配置入口；每个子项对应一组独立参数。"), ReadOnly(false)]
        [InlineList("流程", "参数")]
        [TypeConverter(typeof(ParamListConverter<ProcParam>))]

        public CustomList<ProcParam> Params { get; set; }

    }
    [Serializable]
    public class CycleTimeProbe : OperationType
    {
        public CycleTimeProbe()
        {
            OperaType = "CT探针";
        }

        [DisplayName("任务标识"), Category("计时"), Description("同一流程运行内一组探针共享的稳定标识。"), ReadOnly(false)]
        public string TaskKey { get; set; }

        [DisplayName("分段名称"), Category("计时"), Description("当前探针代表的业务阶段名称。"), ReadOnly(false)]
        public string SegmentName { get; set; }

        [DisplayName("计时起点"), Category("计时"), Description("为 true 时重置本任务并开始一个新周期。"), ReadOnly(false)]
        public bool StartNewCycle { get; set; }

        [DisplayName("分段耗时变量"), Category("结果"), Description("可选 double 变量，写入距上一个探针的毫秒数。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string SegmentMillisecondsVariableName { get; set; }

        [DisplayName("累计耗时变量"), Category("结果"), Description("可选 double 变量，写入距计时起点的累计毫秒数。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string CycleMillisecondsVariableName { get; set; }
    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class ProcParam
    {
        [DisplayName("流程名称"), Category("参数"), Description("目标流程名称；运行时按名称定位对应流程。"), ReadOnly(false), TypeConverter(typeof(ProcItem))]
        public string ProcName { get; set; }
        [DisplayName("流程变量"), Category("参数"), Description("流程名来源变量；用于运行时动态选择目标流程。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ProcValue { get; set; }

        [DisplayName("操作类型"), Category("参数"), Description("运行表示启动目标流程；停止表示停止目标流程。"), ReadOnly(false), TypeConverter(typeof(ProcItemOps))]
        public string TargetState { get; set; }

        [DisplayName("操作后延时"), Category("参数"), Description("当前操作完成后的附加延时（ms），用于等待状态稳定。"), ReadOnly(false)]
        public int DelayAfterMs { get; set; }
        [DisplayName("操作后延时变量"), Category("参数"), Description("附加延时变量名；固定延时小于等于0时从变量读取。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string DelayAfterVariableName { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class WaitProc : OperationType, IPropertyVisibilityProvider
    {
        public const string WaitReadyMode = "等待就绪";
        public const string StateJumpMode = "状态跳转";
        public const string GetStateMode = "获取状态";

        private string workMode = WaitReadyMode;

        public WaitProc()
        {
            OperaType = "等待流程状态";
            ParamListConverter<WaitProcParam>.Name = "流程";
            Params = new CustomList<WaitProcParam>
            {
                new WaitProcParam()
            };
        }

        [DisplayName("工作模式"), Category("参数"), Description("等待就绪：等待目标进入运行或自然完成；状态跳转：按目标当前状态进入对应地址；获取状态：把完整状态写入变量。"), ReadOnly(false), RefreshProperties(RefreshProperties.All), TypeConverter(typeof(WaitProcWorkModeItem))]
        public string WorkMode
        {
            get => workMode;
            set => workMode = value;
        }

        [DisplayName("操作后延时"), Category("参数"), Description("当前操作完成后的附加延时（ms），用于等待状态稳定。"), ReadOnly(false)]
        public int DelayAfterMs { get; set; }
        [DisplayName("操作后延时变量"), Category("参数"), Description("附加延时变量名；固定延时小于等于0时从变量读取。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string DelayAfterVariableName { get; set; }

        [DisplayName("超时设置"), Category("等待就绪"), Description("等待模式的超时参数组；超过边界仍未满足条件时进入报警策略。"), ReadOnly(false)]
        [InlineGroup("超时设置", "等待就绪")]
        public TimeoutSetting Timeout { get; set; } = new TimeoutSetting();
        [DisplayName("设置"), Category("参数"), Description("子项配置入口；每个子项对应一组独立参数。"), ReadOnly(false)]
        [InlineList("流程", "参数", MinItems = 1)]
        [TypeConverter(typeof(ParamListConverter<WaitProcParam>))]

        public CustomList<WaitProcParam> Params { get; set; }

        [DisplayName("目标流程"), Category("目标流程"), Description("状态跳转或获取状态模式使用的目标流程名称。"), ReadOnly(false), TypeConverter(typeof(ProcItem))]
        public string TargetProcName { get; set; }

        [DisplayName("目标流程变量"), Category("目标流程"), Description("状态跳转或获取状态模式使用的流程名来源变量；目标流程名称优先。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string TargetProcValue { get; set; }

        [DisplayName("就绪跳转"), Category("状态跳转"), Description("目标流程处于 Ready（自然完成）时跳转到的当前流程地址。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string ReadyGoto { get; set; }

        [DisplayName("异常跳转"), Category("状态跳转"), Description("目标流程处于 Alarming 或 Stopped（停止请求或异常结束）时跳转到的当前流程地址。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string AbnormalGoto { get; set; }

        [DisplayName("运行中跳转"), Category("状态跳转"), Description("目标流程处于运行、暂停、单步或过渡状态时跳转到的当前流程地址。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string RunningGoto { get; set; }

        [DisplayName("状态变量"), Category("获取状态"), Description("目标变量；double 写入状态码（Stopped=0、Paused=1、SingleStep=2、Running=3、Alarming=4、Pausing=5、Stopping=6、Ready=7），string 写入对应枚举名称。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string StateVariableName { get; set; }

        public bool IsPropertyVisible(string propertyName, bool defaultVisibility)
        {
            switch (propertyName)
            {
                case nameof(DelayAfterMs):
                case nameof(DelayAfterVariableName):
                case nameof(Timeout):
                case nameof(Params):
                    return string.Equals(workMode, WaitReadyMode, StringComparison.Ordinal);
                case nameof(TargetProcName):
                case nameof(TargetProcValue):
                    return string.Equals(workMode, StateJumpMode, StringComparison.Ordinal)
                        || string.Equals(workMode, GetStateMode, StringComparison.Ordinal);
                case nameof(ReadyGoto):
                case nameof(AbnormalGoto):
                case nameof(RunningGoto):
                    return string.Equals(workMode, StateJumpMode, StringComparison.Ordinal);
                case nameof(StateVariableName):
                    return string.Equals(workMode, GetStateMode, StringComparison.Ordinal);
                default:
                    return defaultVisibility;
            }
        }

        public bool ShouldSerializeDelayAfterMs() => workMode == WaitReadyMode;
        public bool ShouldSerializeDelayAfterVariableName() => workMode == WaitReadyMode;
        public bool ShouldSerializeTimeout() => workMode == WaitReadyMode;
        public bool ShouldSerializeParams() => workMode == WaitReadyMode;
        public bool ShouldSerializeTargetProcName() => workMode != WaitReadyMode;
        public bool ShouldSerializeTargetProcValue() => workMode != WaitReadyMode;
        public bool ShouldSerializeReadyGoto() => workMode == StateJumpMode;
        public bool ShouldSerializeAbnormalGoto() => workMode == StateJumpMode;
        public bool ShouldSerializeRunningGoto() => workMode == StateJumpMode;
        public bool ShouldSerializeStateVariableName() => workMode == GetStateMode;
    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class WaitProcParam
    {
        [DisplayName("流程名称"), Category("参数"), Description("目标流程名称；运行时按名称定位对应流程。"), ReadOnly(false), TypeConverter(typeof(ProcItem))]
        public string ProcName { get; set; }
        [DisplayName("流程变量"), Category("参数"), Description("流程名来源变量；用于运行时动态选择目标流程。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ProcValue { get; set; }

        [DisplayName("等待状态"), Category("参数"), Description("只允许运行或就绪；就绪仅表示目标流程自然完成。"), ReadOnly(false), TypeConverter(typeof(ProcWaitStateItem))]
        public string TargetState { get; set; } = "就绪";

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class Goto : OperationType
    {
        public Goto()
        {
            OperaType = "跳转";
            ParamListConverter<GotoParam>.Name = "跳转";
            Params = new CustomList<GotoParam>();
        }

        [DisplayName("变量索引"), Category("参数"), Description("变量索引地址；运行时按索引读取或写入数据。"), ReadOnly(false)]
        public string ValueIndex { get; set; }

        [DisplayName("变量索引二级"), Category("参数"), Description("二级变量索引；用于嵌套对象或二维结构的第二层定位。"), ReadOnly(false)]
        public string ValueIndex2Index { get; set; }

        [DisplayName("变量名称"), Category("参数"), Description("变量名称；运行时按名称读取或写入数据。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueName { get; set; }

        [DisplayName("变量名称二级"), Category("参数"), Description("二级变量名称；用于嵌套对象或二维结构的第二层定位。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueName2Index { get; set; }

        [DisplayName("默认跳转"), Category("参数"), Description("未命中任何条件时使用的默认跳转位置。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string DefaultGoto { get; set; }


        [DisplayName("设置"), Category("参数"), Description("子项配置入口；每个子项对应一组独立参数。"), ReadOnly(false)]
        [InlineList("跳转", "参数")]
        [TypeConverter(typeof(ParamListConverter<GotoParam>))]

        public CustomList<GotoParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class GotoParam
    {
        [DisplayName("匹配值"), Category("参数"), Description("用于条件匹配的固定值。"), ReadOnly(false)]
        public string MatchValue { get; set; }

        [DisplayName("匹配值索引"), Category("参数"), Description("匹配值索引地址；用于按索引读取匹配目标。"), ReadOnly(false)]
        public string MatchValueIndex { get; set; }

        [DisplayName("匹配值变量"), Category("参数"), Description("匹配值变量名；运行时读取匹配目标。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string MatchValueV { get; set; }

        [DisplayName("跳转位置"), Category("参数"), Description("当前匹配项对应的跳转目标位置。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string Goto { get; set; }

        public override string ToString()
        {
            return "";
        }
    }

    [Serializable]
    public class ParamGoto : OperationType
    {
        public ParamGoto()
        {
            OperaType = "逻辑判断";
            ParamListConverter<ParamGotoParam>.Name = "条件";
            Params = new CustomList<ParamGotoParam>
            {
                new ParamGotoParam()
            };
        }
        [DisplayName("成功跳转"), Category("参数"), Description("条件成立时跳转到的目标位置。必须填写三段式数字地址 procIndex-stepIndex-opIndex；界面显示的步骤名或指令名不是可写值。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string TrueGoto { get; set; }

        [DisplayName("失败跳转"), Category("参数"), Description("条件不成立时跳转到的目标位置。必须填写三段式数字地址 procIndex-stepIndex-opIndex；若需继续下一条指令，也要填写下一条指令的地址。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string FalseGoto { get; set; }

        [DisplayName("失败延时(ms)"), Category("参数"), Description("条件不满足时的重试间隔（ms），用于降低轮询压力。"), ReadOnly(false)]
        [NumericRange(0)]
        public int InvalidDelayMs { get; set; }

        [DisplayName("设置"), Category("参数"), Description("子项配置入口；每个子项对应一组独立参数。"), ReadOnly(false)]
        [InlineList("条件", "参数")]
        [TypeConverter(typeof(ParamListConverter<ParamGotoParam>))]

        public CustomList<ParamGotoParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class ParamGotoParam : IPropertyVisibilityProvider
    {
        private string judgeMode = "值在区间内";

        [DisplayName("变量索引"), Category("参数"), Description("变量索引地址；运行时按索引读取或写入数据。"), ReadOnly(false)]
        public string ValueIndex { get; set; }

        [DisplayName("变量索引二级"), Category("参数"), Description("二级变量索引；用于嵌套对象或二维结构的第二层定位。"), ReadOnly(false)]
        public string ValueIndex2Index { get; set; }

        [DisplayName("变量名称"), Category("参数"), Description("变量名称；运行时按名称读取或写入数据。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueName { get; set; }

        [DisplayName("变量名称二级"), Category("参数"), Description("二级变量名称；用于嵌套对象或二维结构的第二层定位。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueName2Index { get; set; }

        [DisplayName("判断模式"), Category("参数"), Description("值在区间左：IncludeBoundary=true 时变量值<=Down，否则<Down；值在区间右：IncludeBoundary=true 时变量值>=Down，否则>Down；值在区间内：IncludeBoundary=true 时 Down<=变量值<=Up，否则 Down<变量值<Up；等于特征字符：变量文本等于 ExpectedText。"), ReadOnly(false), RefreshProperties(RefreshProperties.All), TypeConverter(typeof(JudgeMode))]
        public string JudgeMode
        {
            get => judgeMode;
            set => judgeMode = value;
        }

        [DisplayName("上限"), Category("参数"), Description("区间判断上限值。"), ReadOnly(false)]
        public double Up { get; set; }

        [DisplayName("下限"), Category("参数"), Description("区间判断下限值。"), ReadOnly(false)]
        public double Down { get; set; }

        [DisplayName("特征字符"), Category("参数"), Description("字符匹配模式下的目标特征串。"), ReadOnly(false)]
        public string ExpectedText { get; set; }

        [DisplayName("带等号"), Category("参数"), Description("区间判断时是否包含边界值。"), ReadOnly(false)]
        public bool IncludeBoundary { get; set; }

        [DisplayName("运算符"), Category("参数"), Description("比较运算符（=、!=、>、<等）。"), ReadOnly(false), TypeConverter(typeof(Operator))]
        public string Operator { get; set; } = "且";

        public bool IsPropertyVisible(string propertyName, bool defaultVisibility)
        {
            bool textMode = string.Equals(judgeMode, "等于特征字符", StringComparison.Ordinal);
            switch (propertyName)
            {
                case nameof(Up):
                    return string.Equals(judgeMode, "值在区间内", StringComparison.Ordinal);
                case nameof(Down):
                case nameof(IncludeBoundary):
                    return !textMode;
                case nameof(ExpectedText):
                    return textMode;
                default:
                    return defaultVisibility;
            }
        }

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class Delay : OperationType
    {
        public Delay()
        {
            OperaType = "延时";
        }
        [DisplayName("延时时间(ms)"), Category("参数"), Description("固定延时时长（ms）。"), ReadOnly(false)]
        [NumericRange(0)]
        public int? DelayMs { get; set; }

        [DisplayName("延时时间变量"), Category("参数"), Description("延时时间变量名；用于运行时动态延时。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string DelayVariableName { get; set; }

    }
    [Serializable]
    public class EndProcess : OperationType
    {
        public EndProcess()
        {
            OperaType = "流程结束";
            Name = "结束流程";
        }
    }
    [Serializable]
    public class PopupDialog : OperationType
    {
        public PopupDialog()
        {
            OperaType = "弹框";
            popupType = "弹是";
            infoType = "自定义提示信息";
            alarmLightEnable = "禁用";
            buzzerTimeType = "自定义时间";
            Btn1Text = "是";
            Btn2Text = "否";
            Btn3Text = "取消";
            RefreshInspector += RefleshPropertyPopup;
            RefleshPropertyPopup();
        }

        private string popupType;
        [DisplayName("弹框类型"), Category("弹框相关设置"), Description("弹框样式类型（提示/警告等），影响交互按钮与显示形式。"), ReadOnly(false), TypeConverter(typeof(PopupTypeItem))]
        [Browsable(true)]
        public string PopupType
        {
            get => popupType;
            set
            {
                if (popupType != value)
                {
                    popupType = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshPropertyPopup();
                    }
                }
            }
        }

        [Browsable(true)]
        [DisplayName("按钮1文本"), Category("弹框相关设置"), Description("按钮1显示文本，对应“确定”分支语义。"), ReadOnly(false)]
        public string Btn1Text { get; set; }

        [Browsable(false)]
        [DisplayName("按钮2文本"), Category("弹框相关设置"), Description("按钮2显示文本，对应“否”分支语义。"), ReadOnly(false)]
        public string Btn2Text { get; set; }

        [Browsable(false)]
        [DisplayName("按钮3文本"), Category("弹框相关设置"), Description("按钮3显示文本，对应“取消”分支语义。"), ReadOnly(false)]
        public string Btn3Text { get; set; }

        [Browsable(true)]
        [DisplayName("确定跳转"), Category("弹框跳转设置"), Description("用户点击“确定”后的跳转目标位置。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string PopupGoto1 { get; set; }

        [Browsable(false)]
        [DisplayName("否跳转"), Category("弹框跳转设置"), Description("用户点击“否”后的跳转目标位置。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string PopupGoto2 { get; set; }

        [Browsable(false)]
        [DisplayName("取消跳转"), Category("弹框跳转设置"), Description("用户点击“取消”后的跳转目标位置。"), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string PopupGoto3 { get; set; }

        private string infoType;
        [DisplayName("提示信息类型"), Category("弹框信息设置"), Description("提示信息来源类型（固定文本/变量/报警信息）。"), ReadOnly(false), TypeConverter(typeof(PopupInfoTypeItem))]
        [Browsable(true)]
        public string InfoType
        {
            get => infoType;
            set
            {
                if (infoType != value)
                {
                    infoType = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshPropertyPopup();
                    }
                }
            }
        }

        [Browsable(true)]
        [DisplayName("弹框提示信息"), Category("弹框信息设置"), Description("固定提示文本内容。"), ReadOnly(false)]
        public string PopupMessage { get; set; }

        [Browsable(false)]
        [DisplayName("提示变量"), Category("弹框信息设置"), Description("提示内容变量名；运行时读取变量作为弹框文本。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string PopupMessageValue { get; set; }

        [Browsable(false)]
        [DisplayName("报警信息ID"), Category("弹框信息设置"), Description("报警信息表编号；按编号加载弹框提示内容。"), ReadOnly(false), TypeConverter(typeof(AlarmInfoItem))]
        public string PopupAlarmInfoId { get; set; }

        [Browsable(true)]
        [DisplayName("延时后关闭"), Category("弹框操作"), Description("启用后弹框会在指定时间到达后自动关闭。"), ReadOnly(false)]
        public bool DelayClose
        {
            get => delayClose;
            set
            {
                if (delayClose != value)
                {
                    delayClose = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshPropertyPopup();
                    }
                }
            }
        }

        [Browsable(false)]
        [DisplayName("延时关闭时间(ms)"), Category("弹框操作"), Description("弹框自动关闭等待时间（ms）。"), ReadOnly(false)]
        [NumericRange(0)]
        public int DelayCloseTimeMs { get; set; }

        [DisplayName("保存到报警文件"), Category("弹框操作"), Description("启用后将本次弹框信息写入报警记录文件。"), ReadOnly(false)]
        public bool SaveToAlarmFile { get; set; }

        private string alarmLightEnable;
        [DisplayName("启动报警灯"), Category("启动报警灯"), Description("是否联动蜂鸣器与三色灯输出。"), ReadOnly(false), TypeConverter(typeof(EnableItem))]
        [Browsable(true)]
        public string AlarmLightEnable
        {
            get => alarmLightEnable;
            set
            {
                if (alarmLightEnable != value)
                {
                    alarmLightEnable = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshPropertyPopup();
                    }
                }
            }
        }

        [Browsable(false)]
        [DisplayName("蜂鸣器IO"), Category("启动报警灯"), Description("蜂鸣器输出IO点位。"), ReadOnly(false), TypeConverter(typeof(IoOutItem))]
        public string BuzzerIo { get; set; }

        [Browsable(false)]
        [DisplayName("红灯IO"), Category("启动报警灯"), Description("红灯输出IO点位。"), ReadOnly(false), TypeConverter(typeof(IoOutItem))]
        public string RedLightIo { get; set; }

        [Browsable(false)]
        [DisplayName("黄灯IO"), Category("启动报警灯"), Description("黄灯输出IO点位。"), ReadOnly(false), TypeConverter(typeof(IoOutItem))]
        public string YellowLightIo { get; set; }

        [Browsable(false)]
        [DisplayName("绿灯IO"), Category("启动报警灯"), Description("绿灯输出IO点位。"), ReadOnly(false), TypeConverter(typeof(IoOutItem))]
        public string GreenLightIo { get; set; }

        private string buzzerTimeType;
        [DisplayName("蜂鸣时间类型"), Category("蜂鸣时间设置"), Description("蜂鸣策略类型（固定时长/持续等）。"), ReadOnly(false), TypeConverter(typeof(BuzzerTimeTypeItem))]
        [Browsable(true)]
        public string BuzzerTimeType
        {
            get => buzzerTimeType;
            set
            {
                if (buzzerTimeType != value)
                {
                    buzzerTimeType = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshPropertyPopup();
                    }
                }
            }
        }

        [Browsable(false)]
        [DisplayName("蜂鸣时间(ms)"), Category("蜂鸣时间设置"), Description("蜂鸣持续时间（ms）。"), ReadOnly(false)]
        [NumericRange(0)]
        public int BuzzerTimeMs { get; set; } = 1000;

        private bool delayClose;

        private void RefleshPropertyPopup()
        {
            bool isTwoButton = popupType == "弹是与否" || popupType == "弹是与否与取消";
            bool isThreeButton = popupType == "弹是与否与取消";
            bool useAlarmInfo = infoType == "报警信息库";
            bool useValueInfo = infoType == "变量类型";
            bool useCustomInfo = infoType == "自定义提示信息";

            SetPropertyAttribute(this, "PopupGoto2", typeof(BrowsableAttribute), "browsable", isTwoButton);
            SetPropertyAttribute(this, "PopupGoto3", typeof(BrowsableAttribute), "browsable", isThreeButton);

            SetPropertyAttribute(this, "PopupAlarmInfoId", typeof(BrowsableAttribute), "browsable", useAlarmInfo);
            SetPropertyAttribute(this, "PopupMessage", typeof(BrowsableAttribute), "browsable", useCustomInfo);
            SetPropertyAttribute(this, "PopupMessageValue", typeof(BrowsableAttribute), "browsable", useValueInfo);

            bool showBtnText = !useAlarmInfo;
            SetPropertyAttribute(this, "Btn1Text", typeof(BrowsableAttribute), "browsable", showBtnText);
            SetPropertyAttribute(this, "Btn2Text", typeof(BrowsableAttribute), "browsable", showBtnText && isTwoButton);
            SetPropertyAttribute(this, "Btn3Text", typeof(BrowsableAttribute), "browsable", showBtnText && isThreeButton);

            SetPropertyAttribute(this, "DelayCloseTimeMs", typeof(BrowsableAttribute), "browsable", delayClose);

            bool lightEnabled = alarmLightEnable == "启用";
            SetPropertyAttribute(this, "BuzzerIo", typeof(BrowsableAttribute), "browsable", lightEnabled);
            SetPropertyAttribute(this, "RedLightIo", typeof(BrowsableAttribute), "browsable", lightEnabled);
            SetPropertyAttribute(this, "YellowLightIo", typeof(BrowsableAttribute), "browsable", lightEnabled);
            SetPropertyAttribute(this, "GreenLightIo", typeof(BrowsableAttribute), "browsable", lightEnabled);

            bool showBuzzerTime = lightEnabled && buzzerTimeType == "自定义时间";
            SetPropertyAttribute(this, "BuzzerTimeMs", typeof(BrowsableAttribute), "browsable", showBuzzerTime);

        }
    }
    [Serializable]
    public class GetValue : OperationType
    {
        public GetValue()
        {
            OperaType = "获取变量";
            ParamListConverter<GetValueParam>.Name = "变量";
            Params = new CustomList<GetValueParam>
            {
                new GetValueParam()
            };
        }
        [DisplayName("设置"), Category("参数"), Description("子项配置入口；每个子项对应一组独立参数。"), ReadOnly(false)]
        [InlineList("变量", "参数")]
        [TypeConverter(typeof(ParamListConverter<GetValueParam>))]

        public CustomList<GetValueParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class GetValueParam
    {
        [DisplayName("源变量索引"), Category("源变量"), Description("源变量索引地址；用于读取输入内容。"), ReadOnly(false)]
        public string ValueSourceIndex { get; set; }

        [DisplayName("源变量索引二级"), Category("源变量"), Description("源变量二级索引地址；用于嵌套取值。"), ReadOnly(false)]
        public string ValueSourceIndex2Index { get; set; }

        [DisplayName("源变量名称"), Category("源变量"), Description("源变量名称；用于读取输入内容。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueSourceName { get; set; }

        [DisplayName("源变量名称二级"), Category("源变量"), Description("源变量二级名称；用于嵌套取值。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueSourceName2Index { get; set; }


        [DisplayName("存储变量索引"), Category("存储变量"), Description("存储变量索引地址；用于写入输出内容。"), ReadOnly(false)]
        public string ValueSaveIndex { get; set; }

        [DisplayName("存储变量索引二级"), Category("存储变量"), Description("存储变量二级索引地址；用于嵌套写入。"), ReadOnly(false)]
        public string ValueSaveIndex2Index { get; set; }

        [DisplayName("存储变量名称"), Category("存储变量"), Description("存储变量名称；用于写入输出内容。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueSaveName { get; set; }

        [DisplayName("存储变量名称二级"), Category("存储变量"), Description("存储变量二级名称；用于嵌套写入。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueSaveName2Index { get; set; }

        public override string ToString()
        {
            return "";
        }
    }

    [Serializable]
    public class ModifyValue : OperationType
    {
        public ModifyValue()
        {
            OperaType = "修改变量";
            ModifyType = "替换";
        }

        [DisplayName("修改模式"), Category("参数"), Description("值修改模式（赋值/增减/拼接等）；决定源值与修改值的计算方式。"), ReadOnly(false), TypeConverter(typeof(ModifyType))]

        public string ModifyType { get; set; }

        [DisplayName("原变量取反"), Category("参数"), Description("启用后先对原变量值取反，再参与后续修改计算。"), ReadOnly(false)]

        public bool NegateSource { get; set; }

        [DisplayName("修改值取反"), Category("参数"), Description("启用后先对修改值取反，再与原变量进行运算。"), ReadOnly(false)]
        public bool NegateOperand { get; set; }


        [DisplayName("源变量索引"), Category("A源变量"), Description("源变量索引地址；用于读取待处理值。"), ReadOnly(false)]
        public string ValueSourceIndex { get; set; }

        [DisplayName("源变量索引二级"), Category("A源变量"), Description("源变量二级索引地址；用于嵌套数据取值。"), ReadOnly(false)]
        public string ValueSourceIndex2Index { get; set; }

        [DisplayName("源变量名称"), Category("A源变量"), Description("源变量名称；用于读取待处理值。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueSourceName { get; set; }

        [DisplayName("源变量名称二级"), Category("A源变量"), Description("源变量二级名称；用于嵌套数据取值。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueSourceName2Index { get; set; }




        [DisplayName("修改值"), Category("B修改参数"), Description("参与运算的修改值；与“修改模式”共同决定结果。"), ReadOnly(false)]
        public string ChangeValue { get; set; }

        [DisplayName("修改变量索引"), Category("B修改参数"), Description("变量索引地址；运行时按索引读取或写入数据。"), ReadOnly(false)]
        public string ChangeValueIndex { get; set; }

        [DisplayName("修改变量索引二级"), Category("B修改参数"), Description("二级变量索引；用于嵌套对象或二维结构的第二层定位。"), ReadOnly(false)]
        public string ChangeValueIndex2Index { get; set; }

        [DisplayName("修改变量名称"), Category("B修改参数"), Description("变量名称；运行时按名称读取或写入数据。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ChangeValueName { get; set; }

        [DisplayName("修改变量名称二级"), Category("B修改参数"), Description("二级变量名称；用于嵌套对象或二维结构的第二层定位。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ChangeValueName2Index { get; set; }



        [DisplayName("结果变量索引"), Category("C保存参数"), Description("结果变量索引地址；用于保存计算结果。"), ReadOnly(false)]
        public string OutputValueIndex { get; set; }

        [DisplayName("结果变量索引二级"), Category("C保存参数"), Description("结果变量二级索引地址；用于嵌套保存。"), ReadOnly(false)]
        public string OutputValueIndex2Index { get; set; }

        [DisplayName("结果变量名称"), Category("C保存参数"), Description("结果变量名称；用于保存计算结果。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string OutputValueName { get; set; }

        [DisplayName("结果变量名称二级"), Category("C保存参数"), Description("结果变量二级名称；用于嵌套保存。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string OutputValueName2Index { get; set; }
    }

    [Serializable]
    public class StringFormat : OperationType
    {
        public StringFormat()
        {
            OperaType = "数据拼接";
            ParamListConverter<StringFormatParam>.Name = "变量";
            Params = new CustomList<StringFormatParam>
            {
                new StringFormatParam()
            };
        }
        [DisplayName("拼接格式"), Category("参数"), Description("字符串拼接格式模板，按占位顺序组合输入值。"), ReadOnly(false)]
        public string Format { get; set; }

        [DisplayName("存储变量索引"), Category("参数"), Description("存储变量索引地址；用于保存计算结果。"), ReadOnly(false)]
        public string OutputValueIndex { get; set; }
        [DisplayName("存储变量"), Category("参数"), Description("存储变量名称；用于保存计算结果。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string OutputValueName { get; set; }
        [DisplayName("设置"), Category("参数"), Description("子项配置入口；每个子项对应一组独立参数。"), ReadOnly(false)]
        [InlineList("变量", "参数")]
        [TypeConverter(typeof(ParamListConverter<StringFormatParam>))]

        public CustomList<StringFormatParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class StringFormatParam
    {
        [DisplayName("源变量索引"), Category("参数"), Description("源变量索引地址；用于读取输入值。"), ReadOnly(false)]
        public string ValueSourceIndex { get; set; }
        [DisplayName("源变量"), Category("参数"), Description("源变量名称；用于读取输入值。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueSourceName { get; set; }
        public override string ToString()
        {
            return "";
        }
    }

    [Serializable]
    public class Split : OperationType
    {
        public Split()
        {
            OperaType = "字符串分割";
        }

        [DisplayName("分割符"), Category("参数"), Description("字符串分割分隔符。"), ReadOnly(false)]

        public char SplitMark { get; set; }

        [DisplayName("分割起始索引"), Category("参数"), Description("分割结果起始下标（从0开始）。"), ReadOnly(false)]

        [NumericRange(0)]
        public int StartIndex { get; set; }

        [DisplayName("分割数量"), Category("参数"), Description("从起始下标开始提取的分段数量；null 表示使用分割结果总数。"), ReadOnly(false)]
        [NumericRange(0)]
        public int? Count { get; set; }
        [DisplayName("源变量索引"), Category("参数"), Description("源变量索引地址；用于读取输入值。"), ReadOnly(false)]
        public string SourceValueIndex { get; set; }
        [DisplayName("源变量"), Category("参数"), Description("源变量名称；用于读取输入值。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string SourceValue { get; set; }
        [DisplayName("结果变量索引"), Category("参数"), Description("结果变量索引地址；用于写入输出值。"), ReadOnly(false)]
        public string OutputIndex { get; set; }
        [DisplayName("结果变量"), Category("参数"), Description("结果变量名称；用于写入输出值。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string Output { get; set; }
    }

    [Serializable]
    public class Replace : OperationType
    {
        public Replace()
        {
            OperaType = "字符串替换";
            RefreshInspector += RefleshProperty;
        }
        [DisplayName("被替换字符串"), Category("A被替换字符串"), Description("待替换目标文本。"), ReadOnly(false)]
        [Browsable(true)]
        public string ReplaceStr { get; set; }
        [DisplayName("被替换字符串索引"), Category("A被替换字符串"), Description("待替换文本索引地址；用于按索引读取目标文本。"), ReadOnly(false)]
        [Browsable(true)]
        public string ReplaceStrIndex { get; set; }
        [DisplayName("被替换字符串变量"), Category("A被替换字符串"), Description("待替换文本变量名；运行时读取目标文本。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string ReplaceStrV { get; set; }

        [DisplayName("新字符串"), Category("B新字符串"), Description("替换后写入的新文本。"), ReadOnly(false)]
        [Browsable(true)]
        public string NewStr { get; set; }

        [DisplayName("新字符串索引"), Category("B新字符串"), Description("新文本索引地址；用于按索引读取替换文本。"), ReadOnly(false)]
        [Browsable(true)]
        public string NewStrIndex { get; set; }

        [DisplayName("新字符串变量"), Category("B新字符串"), Description("新文本变量名；运行时读取替换文本。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string NewStrV { get; set; }

        [DisplayName("替换起始索引"), Category("参数"), Description("替换起始位置（从0开始）。"), ReadOnly(false)]
        [Browsable(true)]
        [NumericRange(0)]
        public int StartIndex { get; set; }

        [DisplayName("替换数量"), Category("参数"), Description("连续替换的字符数量。"), ReadOnly(false)]
        [Browsable(true)]
        [NumericRange(0)]
        public int? Count { get; set; }

        private string replaceType = "替换指定字符";
        [DisplayName("替换类型"), Category("参数"), Description("替换模式（按内容/按位置等）。"), ReadOnly(false), TypeConverter(typeof(ReplaceType))]
        [Browsable(true)]
        public string ReplaceType
        {
            get => replaceType;
            set
            {
                if (replaceType != value)
                {
                    replaceType = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshProperty();
                    }
                }
            }
        }

        [DisplayName("源变量索引"), Category("参数"), Description("源变量索引地址；用于读取输入值。"), ReadOnly(false)]
        [Browsable(true)]
        public string SourceValueIndex { get; set; }
        [DisplayName("源变量"), Category("参数"), Description("源变量名称；用于读取输入值。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string SourceValue { get; set; }

        [DisplayName("结果变量索引"), Category("参数"), Description("结果变量索引地址；用于写入输出值。"), ReadOnly(false)]
        [Browsable(true)]
        public string OutputIndex { get; set; }
        [DisplayName("结果变量"), Category("参数"), Description("结果变量名称；用于写入输出值。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string Output { get; set; }

        public void RefleshProperty()
        {
            if (this.replaceType == "替换指定字符")
            {
                SetPropertyAttribute(this, "StartIndex", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Count", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "ReplaceStr", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "ReplaceStrIndex", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "ReplaceStrV", typeof(BrowsableAttribute), "browsable", true);
            }
            else
            {
                SetPropertyAttribute(this, "StartIndex", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Count", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "ReplaceStr", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "ReplaceStrIndex", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "ReplaceStrV", typeof(BrowsableAttribute), "browsable", false);
            }
        }
    }
    [Serializable]
    public class SetDataStructItem : OperationType
    {
        public SetDataStructItem()
        {
            OperaType = "设置结构体数据项";
            ParamListConverter<SetDataStructItemParam>.Name = "数据";
            Params = new CustomList<SetDataStructItemParam>
            {
                new SetDataStructItemParam()
            };
        }
        [DisplayName("按名称寻址"), Category("参数"), Description("启用后严格使用结构体名称、数据项名称和字段名称；关闭时继续使用原索引。"), ReadOnly(false)]
        public bool UseNameAddressing { get; set; }

        [DisplayName("结构体名称"), Category("参数"), Description("按名称寻址时使用的结构体精确名称。"), ReadOnly(false), TypeConverter(typeof(DataStItem))]
        public string StructName { get; set; }

        [DisplayName("数据项名称"), Category("参数"), Description("按名称寻址时使用的数据项精确名称。"), ReadOnly(false)]
        public string ItemName { get; set; }

        [DisplayName("结构体索引"), Category("参数"), Description("目标结构体索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int StructIndex { get; set; }

        [DisplayName("数据项索引"), Category("参数"), Description("目标数据项索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int ItemIndex { get; set; }

        [DisplayName("设置"), Category("参数"), Description("子项配置入口；每个子项对应一组独立参数。"), ReadOnly(false)]
        [InlineList("数据", "参数")]
        [TypeConverter(typeof(ParamListConverter<SetDataStructItemParam>))]

        public CustomList<SetDataStructItemParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class SetDataStructItemParam
    {
        [DisplayName("值索引"), Category("参数"), Description("值来源索引地址。"), ReadOnly(false)]
        [NumericRange(0)]
        public int FieldIndex { get; set; }

        [DisplayName("字段名称"), Category("参数"), Description("按名称寻址时使用的字段精确名称。"), ReadOnly(false)]
        public string FieldName { get; set; }

        [DisplayName("值"), Category("参数"), Description("固定值内容。"), ReadOnly(false)]
        public string Value { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class GetDataStructItem : OperationType
    {
        public GetDataStructItem()
        {
            OperaType = "获取结构体数据项";
            ParamListConverter<GetDataStructItemParam>.Name = "数据";
            Params = new CustomList<GetDataStructItemParam>
            {
                new GetDataStructItemParam()
            };
        }
        [DisplayName("按名称寻址"), Category("参数"), Description("启用后严格使用结构体名称、数据项名称和字段名称；关闭时继续使用原索引。"), ReadOnly(false)]
        public bool UseNameAddressing { get; set; }

        [DisplayName("结构体名称"), Category("参数"), Description("按名称寻址时使用的结构体精确名称。"), ReadOnly(false), TypeConverter(typeof(DataStItem))]
        public string StructName { get; set; }

        [DisplayName("数据项名称"), Category("参数"), Description("按名称寻址时使用的数据项精确名称。"), ReadOnly(false)]
        public string ItemName { get; set; }

        [DisplayName("是否获取所有项"), Category("参数"), Description("启用后按数量连续读取多个数据项。"), ReadOnly(false)]
        public bool IsAllItem { get; set; }

        [DisplayName("首个结果变量"), Category("参数"), Description("批量读取时结果保存的首个变量，后续字段写入连续变量。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string FirstResultVariableName { get; set; }

        [DisplayName("结构体索引"), Category("参数"), Description("目标结构体索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int StructIndex { get; set; }

        [DisplayName("数据项索引"), Category("参数"), Description("目标数据项索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int ItemIndex { get; set; }

        [DisplayName("设置"), Category("参数"), Description("子项配置入口；每个子项对应一组独立参数。"), ReadOnly(false)]
        [InlineList("数据", "参数")]
        [TypeConverter(typeof(ParamListConverter<GetDataStructItemParam>))]

        public CustomList<GetDataStructItemParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class GetDataStructItemParam
    {

        [DisplayName("数据项索引"), Category("参数"), Description("目标数据项索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int FieldIndex { get; set; }

        [DisplayName("字段名称"), Category("参数"), Description("按名称寻址时使用的字段精确名称。"), ReadOnly(false)]
        public string FieldName { get; set; }

        [DisplayName("结果变量索引"), Category("参数"), Description("当前结构体字段直接写入的目标变量索引。"), ReadOnly(false)]
        public string OutputValueIndex { get; set; }

        [DisplayName("结果变量名称"), Category("参数"), Description("当前结构体字段直接写入的目标变量名称。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string OutputValueName { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class CopyDataStructItem : OperationType
    {
        public CopyDataStructItem()
        {
            OperaType = "复制结构体数据项";
            ParamListConverter<CopyDataStructItemParam>.Name = "数据";
            Params = new CustomList<CopyDataStructItemParam>
            {
                new CopyDataStructItemParam()
            };
        }
        [DisplayName("源按名称寻址"), Category("参数"), Description("启用后源严格使用结构体名称、数据项名称和字段名称；关闭时继续使用源索引。"), ReadOnly(false)]
        public bool UseSourceNameAddressing { get; set; }

        [DisplayName("源结构体名称"), Category("参数"), Description("源按名称寻址时使用的结构体精确名称。"), ReadOnly(false), TypeConverter(typeof(DataStItem))]
        public string SourceStructName { get; set; }

        [DisplayName("源数据项名称"), Category("参数"), Description("源按名称寻址时使用的数据项精确名称。"), ReadOnly(false)]
        public string SourceItemName { get; set; }

        [DisplayName("目标按名称寻址"), Category("参数"), Description("启用后目标严格使用结构体名称、数据项名称和字段名称；关闭时继续使用目标索引。"), ReadOnly(false)]
        public bool UseTargetNameAddressing { get; set; }

        [DisplayName("目标结构体名称"), Category("参数"), Description("目标按名称寻址时使用的结构体精确名称。"), ReadOnly(false), TypeConverter(typeof(DataStItem))]
        public string TargetStructName { get; set; }

        [DisplayName("目标数据项名称"), Category("参数"), Description("目标按名称寻址时使用的数据项精确名称。"), ReadOnly(false)]
        public string TargetItemName { get; set; }

        [DisplayName("是否复制所有项"), Category("参数"), Description("启用后从起始项开始按数量连续复制。"), ReadOnly(false)]
        public bool IsAllValue { get; set; }

        [DisplayName("源结构体索引"), Category("参数"), Description("源结构体索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int SourceStructIndex { get; set; }

        [DisplayName("源数据项索引"), Category("参数"), Description("源数据项索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int SourceItemIndex { get; set; }

        [DisplayName("目标结构体索引"), Category("参数"), Description("目标结构体索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TargetStructIndex { get; set; }

        [DisplayName("目标数据项索引"), Category("参数"), Description("目标数据项索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TargetItemIndex { get; set; }

        [DisplayName("设置"), Category("参数"), Description("子项配置入口；每个子项对应一组独立参数。"), ReadOnly(false)]
        [InlineList("数据", "参数")]
        [TypeConverter(typeof(ParamListConverter<CopyDataStructItemParam>))]

        public CustomList<CopyDataStructItemParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class CopyDataStructItemParam
    {

        [DisplayName("源数据项索引"), Category("参数"), Description("源数据项索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int SourceFieldIndex { get; set; }

        [DisplayName("源字段名称"), Category("参数"), Description("源按名称寻址时使用的字段精确名称。"), ReadOnly(false)]
        public string SourceFieldName { get; set; }

        [DisplayName("目标数据项索引"), Category("参数"), Description("目标数据项索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TargetFieldIndex { get; set; }

        [DisplayName("目标字段名称"), Category("参数"), Description("目标按名称寻址时使用的字段精确名称。"), ReadOnly(false)]
        public string TargetFieldName { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class InsertDataStructItem : OperationType
    {
        public InsertDataStructItem()
        {
            OperaType = "插入结构体数据项";
            ParamListConverter<InsertDataStructItemParam>.Name = "数据";
            Params = new CustomList<InsertDataStructItemParam>
            {
                new InsertDataStructItemParam()
            };
        }

        [DisplayName("结构体按名称寻址"), Category("参数"), Description("启用后严格使用目标结构体名称；关闭时继续使用目标结构体索引。插入位置始终使用数据项索引。"), ReadOnly(false)]
        public bool UseStructNameAddressing { get; set; }

        [DisplayName("目标结构体名称"), Category("参数"), Description("按名称寻址时使用的目标结构体精确名称。"), ReadOnly(false), TypeConverter(typeof(DataStItem))]
        public string TargetStructName { get; set; }

        [DisplayName("数据项名称"), Category("参数"), Description("插入后新数据项的名称。"), ReadOnly(false)]
        public string ItemName { get; set; }

        [DisplayName("目标结构体索引"), Category("参数"), Description("目标结构体索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TargetStructIndex { get; set; }

        [DisplayName("目标数据项索引"), Category("参数"), Description("目标数据项索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TargetItemIndex { get; set; }

        [DisplayName("设置"), Category("参数"), Description("子项配置入口；每个子项对应一组独立参数。"), ReadOnly(false)]
        [InlineList("数据", "参数")]
        [TypeConverter(typeof(ParamListConverter<InsertDataStructItemParam>))]

        public CustomList<InsertDataStructItemParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class InsertDataStructItemParam
    {
        [DisplayName("数据类型"), Category("参数"), Description("数据项类型；决定变量取值与写入方式。"), ReadOnly(false), TypeConverter(typeof(SturctItemType))]
        public string Type { get; set; }

        [DisplayName("数据变量"), Category("参数"), Description("数据来源变量名。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueVariableName { get; set; }

        [DisplayName("数据值"), Category("参数"), Description("固定数据值。"), ReadOnly(false)]
        public string Value { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class DelDataStructItem : OperationType
    {
        public DelDataStructItem()
        {
            OperaType = "删除结构体数据项";
        }

        [DisplayName("按名称寻址"), Category("参数"), Description("启用后严格使用目标结构体名称和数据项名称；关闭时继续使用原索引。"), ReadOnly(false)]
        public bool UseNameAddressing { get; set; }

        [DisplayName("目标结构体名称"), Category("参数"), Description("按名称寻址时使用的目标结构体精确名称。"), ReadOnly(false), TypeConverter(typeof(DataStItem))]
        public string TargetStructName { get; set; }

        [DisplayName("目标数据项名称"), Category("参数"), Description("按名称寻址时使用的目标数据项精确名称。"), ReadOnly(false)]
        public string TargetItemName { get; set; }

        [DisplayName("目标结构体索引"), Category("参数"), Description("目标结构体索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TargetStructIndex { get; set; }

        [DisplayName("目标数据项索引"), Category("参数"), Description("目标数据项索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TargetItemIndex { get; set; }
    }

    [Serializable]
    public class FindDataStructItem : OperationType
    {
        public FindDataStructItem()
        {
            OperaType = "查找结构体数据项";
        }

        [DisplayName("结构体按名称寻址"), Category("参数"), Description("启用后严格使用目标结构体名称；关闭时继续使用目标结构体索引。"), ReadOnly(false)]
        public bool UseStructNameAddressing { get; set; }

        [DisplayName("目标结构体名称"), Category("参数"), Description("按名称寻址时使用的目标结构体精确名称。"), ReadOnly(false), TypeConverter(typeof(DataStItem))]
        public string TargetStructName { get; set; }

        [DisplayName("目标结构体索引"), Category("参数"), Description("目标结构体索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TargetStructIndex { get; set; }

        //[DisplayName("目标结构项索引"), Category("参数"), Description(""), ReadOnly(false)]
        //public int TargetItemIndex { get; set; }

        [DisplayName("查找类型"), Category("参数"), Description("查找匹配模式（按值/按条件）。"), ReadOnly(false), TypeConverter(typeof(FindType))]
        public string Type { get; set; }

        [DisplayName("目标关键字"), Category("参数"), Description("查找关键字内容。"), ReadOnly(false)]
        public string Key { get; set; }

        [DisplayName("结果保存地址"), Category("参数"), Description("查找结果保存变量。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ResultVariableName { get; set; }

    }

    [Serializable]
    public class GetDataStructCount : OperationType
    {
        public GetDataStructCount()
        {
            OperaType = "获取结构体数量";
        }

        [DisplayName("结构体按名称寻址"), Category("参数"), Description("启用后严格使用目标结构体名称；关闭时继续使用目标结构体索引。"), ReadOnly(false)]
        public bool UseStructNameAddressing { get; set; }

        [DisplayName("目标结构体名称"), Category("参数"), Description("按名称寻址时使用的目标结构体精确名称。"), ReadOnly(false), TypeConverter(typeof(DataStItem))]
        public string TargetStructName { get; set; }

        [DisplayName("目标结构体索引"), Category("参数"), Description("目标结构体索引。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TargetStructIndex { get; set; }


        [DisplayName("结构体数量保存地址"), Category("参数"), Description("结构体数量结果保存变量。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string StructCountVariableName { get; set; }

        [DisplayName("结构项数量保存地址"), Category("参数"), Description("结构项数量结果保存变量。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ItemCountVariableName { get; set; }

    }

    [Serializable]
    public class TcpOps : OperationType
    {
        public TcpOps()
        {
            OperaType = "网口通讯操作";
            ParamListConverter<TcpOpsParam>.Name = "网口";
            Params = new CustomList<TcpOpsParam>
            {
                new TcpOpsParam()
            };
        }

        [DisplayName("设置"), Category("参数"), Description("子项配置入口；每个子项对应一组独立参数。"), ReadOnly(false)]
        [InlineList("网口", "参数")]
        [TypeConverter(typeof(ParamListConverter<TcpOpsParam>))]

        public CustomList<TcpOpsParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class TcpOpsParam
    {
        [DisplayName("对象名称"), Category("参数"), Description("TCP逻辑通道精确名称；Server通道可按远端条件绑定独立会话。"), ReadOnly(false), TypeConverter(typeof(TcpItem))]

        public string Name { get; set; }

        [DisplayName("操作"), Category("参数"), Description("启动只建立通道生命周期：Server进入监听，Client进入连接或自动重连；断开会停止通道。"), ReadOnly(false), TypeConverter(typeof(CommunicationOps))]

        public string Ops { get; set; }
        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class WaitTcp : OperationType
    {
        public WaitTcp()
        {
            OperaType = "等待网口连接";
            ParamListConverter<WaitTcpParam>.Name = "网口";
            Params = new CustomList<WaitTcpParam>
            {
                new WaitTcpParam()
            };
        }

        [DisplayName("设置"), Category("参数"), Description("子项配置入口；每个子项对应一组独立参数。"), ReadOnly(false)]
        [InlineList("网口", "参数")]
        [TypeConverter(typeof(ParamListConverter<WaitTcpParam>))]

        public CustomList<WaitTcpParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class WaitTcpParam
    {
        public WaitTcpParam()
        {
            TimeoutMs = TimeoutSetting.DefaultTimeoutMs;
        }

        [DisplayName("对象名称"), Category("参数"), Description("TCP逻辑通道精确名称；等待真实活动连接，监听中、连接中和重连中均不算成功。"), ReadOnly(false), TypeConverter(typeof(TcpItem))]

        public string Name { get; set; }

        [DisplayName("超时"), Category("参数"), Description("超时时间（ms）；超过该时长仍未满足条件会触发报警。"), ReadOnly(false)]

        [NumericRange(0)]
        public int TimeoutMs { get; set; }
        public override string ToString()
        {
            return "";
        }
    }

    [Serializable]
    public class SendTcpMsg : CommunicationOperationType
    {
        public SendTcpMsg()
        {
            OperaType = "发送TCP通讯消息";
            TimeoutMs = 3000;
        }

        [DisplayName("通讯对象"), Category("参数"), Description("TCP逻辑通道精确名称；Server具体远端通道定向发送，通配通道可能向多个会话广播。"), ReadOnly(false), TypeConverter(typeof(TcpItem))]
        public string ConnectionName { get; set; }

        [DisplayName("发送信息"), Category("参数"), Description("发送内容来源变量；运行时读取变量值并下发。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string Msg { get; set; }

        [DisplayName("是否16进制发送"), Category("参数"), Description("启用后按16进制格式发送数据；关闭则按文本发送。"), ReadOnly(false)]
        public bool UseHexEncoding { get; set; }

        [DisplayName("超时"), Category("参数"), Description("超时时间（ms）；超过该时长仍未满足条件会触发报警。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TimeoutMs { get; set; }

    }


    [Serializable]
    public class ReceiveTcpMsg : ResponseCommunicationOperationType
    {
        public ReceiveTcpMsg()
        {
            OperaType = "接收TCP通讯消息";
            TimeoutMs = TimeoutSetting.DefaultTimeoutMs;
        }

        [DisplayName("通讯对象"), Category("参数"), Description("TCP逻辑通道精确名称；Server只接收被路由到该通道的会话消息。"), ReadOnly(false), TypeConverter(typeof(TcpItem))]
        public string ConnectionName { get; set; }

        [DisplayName("接收信息"), Category("参数"), Description("接收结果保存变量；收到数据后写入该变量。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string MsgSaveValue { get; set; }

        [DisplayName("是否16进制接收"), Category("参数"), Description("启用后按16进制解析接收数据；关闭则按文本解析。"), ReadOnly(false)]
        public bool UseHexEncoding { get; set; }

        [DisplayName("超时时间"), Category("参数"), Description("超时时间（ms）；用于发送/接收等待上限控制。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TimeoutMs { get; set; }


    }

    [Serializable]
    public class SerialPortOps : OperationType
    {
        public SerialPortOps()
        {
            OperaType = "串口通讯操作";
            ParamListConverter<SerialPortOpsParam>.Name = "串口";
            Params = new CustomList<SerialPortOpsParam>
            {
                new SerialPortOpsParam()
            };
        }

        [DisplayName("设置"), Category("参数"), Description("子项配置入口；每个子项对应一组独立参数。"), ReadOnly(false)]
        [InlineList("串口", "参数")]
        [TypeConverter(typeof(ParamListConverter<SerialPortOpsParam>))]

        public CustomList<SerialPortOpsParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class SerialPortOpsParam
    {
        [DisplayName("对象名称"), Category("参数"), Description("通讯对象名称；运行时按名称定位TCP/串口通道。"), ReadOnly(false), TypeConverter(typeof(SerialPortItem))]

        public string Name { get; set; }

        [DisplayName("操作"), Category("参数"), Description("连接操作类型（启动/断开），用于控制通道生命周期。"), ReadOnly(false), TypeConverter(typeof(CommunicationOps))]

        public string Ops { get; set; }
        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class WaitSerialPort : OperationType
    {
        public WaitSerialPort()
        {
            OperaType = "等待串口连接";
            ParamListConverter<WaitSerialPortParam>.Name = "串口";
            Params = new CustomList<WaitSerialPortParam>
            {
                new WaitSerialPortParam()
            };
        }

        [DisplayName("设置"), Category("参数"), Description("子项配置入口；每个子项对应一组独立参数。"), ReadOnly(false)]
        [InlineList("串口", "参数")]
        [TypeConverter(typeof(ParamListConverter<WaitSerialPortParam>))]

        public CustomList<WaitSerialPortParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class WaitSerialPortParam
    {
        public WaitSerialPortParam()
        {
            TimeoutMs = TimeoutSetting.DefaultTimeoutMs;
        }

        [DisplayName("对象名称"), Category("参数"), Description("通讯对象名称；运行时按名称定位TCP/串口通道。"), ReadOnly(false), TypeConverter(typeof(SerialPortItem))]

        public string Name { get; set; }

        [DisplayName("超时"), Category("参数"), Description("超时时间（ms）；超过该时长仍未满足条件会触发报警。"), ReadOnly(false)]

        [NumericRange(0)]
        public int TimeoutMs { get; set; }
        public override string ToString()
        {
            return "";
        }
    }

    [Serializable]
    public class SendSerialPortMsg : CommunicationOperationType
    {
        public SendSerialPortMsg()
        {
            OperaType = "发送串口通讯消息";
            TimeoutMs = 3000;
        }

        [DisplayName("通讯对象"), Category("参数"), Description("串口通讯对象名称；用于绑定具体发送通道。"), ReadOnly(false), TypeConverter(typeof(SerialPortItem))]
        public string ConnectionName { get; set; }

        [DisplayName("发送信息"), Category("参数"), Description("发送内容来源变量；运行时读取变量值并下发。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string Msg { get; set; }

        [DisplayName("是否16进制发送"), Category("参数"), Description("启用后按16进制格式发送数据；关闭则按文本发送。"), ReadOnly(false)]
        public bool UseHexEncoding { get; set; }

        [DisplayName("超时"), Category("参数"), Description("超时时间（ms）；超过该时长仍未满足条件会触发报警。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TimeoutMs { get; set; }

    }


    [Serializable]
    public class ReceiveSerialPortMsg : ResponseCommunicationOperationType
    {
        public ReceiveSerialPortMsg()
        {
            OperaType = "接收串口通讯消息";
            TimeoutMs = TimeoutSetting.DefaultTimeoutMs;
        }

        [DisplayName("通讯对象"), Category("参数"), Description("串口通讯对象名称；用于绑定具体接收通道。"), ReadOnly(false), TypeConverter(typeof(SerialPortItem))]
        public string ConnectionName { get; set; }

        [DisplayName("接收信息"), Category("参数"), Description("接收结果保存变量；收到数据后写入该变量。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string MsgSaveValue { get; set; }

        [DisplayName("是否16进制接收"), Category("参数"), Description("启用后按16进制解析接收数据；关闭则按文本解析。"), ReadOnly(false)]
        public bool UseHexEncoding { get; set; }

        [DisplayName("超时时间"), Category("参数"), Description("超时时间（ms）；用于发送/接收等待上限控制。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TimeoutMs { get; set; }


    }

    [Serializable]
    public class SendReceiveCommMsg : ResponseCommunicationOperationType
    {
        public SendReceiveCommMsg()
        {
            OperaType = "发送与接收";
            CommType = "TCP";
            TimeoutMs = 3000;
        }

        [DisplayName("通讯类型"), Category("参数"), Description("通讯类型（TCP/串口）选择，决定通讯对象可选范围。"), ReadOnly(false), TypeConverter(typeof(CommType))]
        public string CommType { get; set; }

        [DisplayName("通讯对象"), Category("参数"), Description("通讯对象精确名称；TCP Server请求响应要求该逻辑通道恰好一个在线会话。"), ReadOnly(false), TypeConverter(typeof(CommItem))]
        public string ConnectionName { get; set; }

        [DisplayName("发送信息"), Category("参数"), Description("发送内容来源变量；运行时读取变量值并下发。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string SendMsg { get; set; }

        [DisplayName("是否16进制发送"), Category("参数"), Description("启用后按16进制格式发送数据；关闭则按文本发送。"), ReadOnly(false)]
        public bool SendConvert { get; set; }

        [DisplayName("接收信息"), Category("参数"), Description("接收结果保存变量；收到数据后写入该变量。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ReceiveSaveValue { get; set; }

        [DisplayName("是否16进制接收"), Category("参数"), Description("启用后按16进制解析接收数据；关闭则按文本解析。"), ReadOnly(false)]
        public bool ReceiveConvert { get; set; }

        [DisplayName("超时时间"), Category("参数"), Description("超时时间（ms）；用于发送/接收等待上限控制。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TimeoutMs { get; set; }
    }

    [Serializable]
    public class PlcReadWrite : ResponseCommunicationOperationType
    {
        public const int CurrentModelVersion = 2;
        private PlcAccessAction action;
        private PlcAccessMode mode;

        public PlcReadWrite()
        {
            OperaType = "PLC读写";
            ModelVersion = CurrentModelVersion;
            action = PlcAccessAction.Read;
            mode = PlcAccessMode.Items;
            ReadItems = new CustomList<PlcReadItem> { new PlcReadItem() };
            WriteItems = new CustomList<PlcWriteItem> { new PlcWriteItem() };
            ReadBatch = new PlcReadBatch();
            WriteBatch = new PlcWriteBatch();
        }

        [Browsable(false)]
        [JsonProperty(Required = Required.Always)]
        public int ModelVersion { get; set; }

        [DisplayName("PLC设备"), Category("B.PLC参数"), Description("PLC设备名称；运行时按名称选择目标连接。"), ReadOnly(false), TypeConverter(typeof(PlcItem))]
        public string DeviceName { get; set; }

        [DisplayName("操作"), Category("B.PLC参数"), Description("选择读取或写入；界面仅显示当前操作相关的配置。"), ReadOnly(false), RefreshProperties(RefreshProperties.All), TypeConverter(typeof(PlcAccessActionItem))]
        public PlcAccessAction Action
        {
            get => action;
            set
            {
                action = value;
            }
        }

        [DisplayName("方式"), Category("C.访问方式"), Description("按项模式支持多个独立地址；连续批量模式通过一次请求访问连续地址。"), ReadOnly(false), RefreshProperties(RefreshProperties.All), TypeConverter(typeof(PlcAccessModeItem))]
        public PlcAccessMode Mode
        {
            get => mode;
            set
            {
                mode = value;
            }
        }

        [Browsable(true)]
        [DisplayName("读取项设置"), Category("D.按项读取"), Description("每项分别配置PLC地址、数据类型和保存变量。"), ReadOnly(false)]
        [InlineList("读取项", "D.按项读取", MinItems = 1, MaxItems = 100)]
        [TypeConverter(typeof(ParamListConverter<PlcReadItem>))]
        public CustomList<PlcReadItem> ReadItems { get; set; }

        [Browsable(false)]
        [DisplayName("连续读取"), Category("D.连续批量读取"), Description("一次读取同一区域内的连续地址，并写入连续变量。"), ReadOnly(false)]
        [InlineGroup("连续读取", "D.连续批量读取")]
        [TypeConverter(typeof(SerializableExpandableObjectConverter))]
        public PlcReadBatch ReadBatch { get; set; }

        [Browsable(false)]
        [DisplayName("写入项设置"), Category("D.按项写入"), Description("每项分别配置PLC地址、数据类型和写入来源。"), ReadOnly(false)]
        [InlineList("写入项", "D.按项写入", MinItems = 1, MaxItems = 100)]
        [TypeConverter(typeof(ParamListConverter<PlcWriteItem>))]
        public CustomList<PlcWriteItem> WriteItems { get; set; }

        [Browsable(false)]
        [DisplayName("连续写入"), Category("D.连续批量写入"), Description("一次写入同一区域内的连续地址。"), ReadOnly(false)]
        [InlineGroup("连续写入", "D.连续批量写入")]
        [TypeConverter(typeof(PlcWriteBatchConverter))]
        public PlcWriteBatch WriteBatch { get; set; }

        public override bool IsPropertyVisible(string propertyName, bool defaultVisibility)
        {
            if (propertyName == nameof(ResponseConditions))
            {
                return RetryCount > 0 && action == PlcAccessAction.Read;
            }
            bool read = action == PlcAccessAction.Read;
            bool items = mode == PlcAccessMode.Items;
            switch (propertyName)
            {
                case nameof(ReadItems):
                    return read && items;
                case nameof(ReadBatch):
                    return read && !items;
                case nameof(WriteItems):
                    return !read && items;
                case nameof(WriteBatch):
                    return !read && !items;
                default:
                    return defaultVisibility;
            }
        }

        public override bool ShouldSerializeResponseConditions()
        {
            return base.ShouldSerializeResponseConditions() && action == PlcAccessAction.Read;
        }

        internal override bool ShouldEvaluateResponseConditions => action == PlcAccessAction.Read;

        public bool ShouldSerializeReadItems() => action == PlcAccessAction.Read && mode == PlcAccessMode.Items;
        public bool ShouldSerializeReadBatch() => action == PlcAccessAction.Read && mode == PlcAccessMode.ContinuousBatch;
        public bool ShouldSerializeWriteItems() => action == PlcAccessAction.Write && mode == PlcAccessMode.Items;
        public bool ShouldSerializeWriteBatch() => action == PlcAccessAction.Write && mode == PlcAccessMode.ContinuousBatch;
    }

    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class PlcReadItem
    {
        [DisplayName("地址区"), Category("PLC地址"), Description("线圈/离散输入仅支持Boolean，寄存器区支持数值和字符串。"), ReadOnly(false)]
        [TypeConverter(typeof(PlcAreaItem))]
        public PlcArea Area { get; set; } = PlcArea.HoldingRegister;

        [DisplayName("地址"), Category("PLC地址"), Description("本项要读取的PLC地址。"), ReadOnly(false)]
        public int StartAddress { get; set; }

        [DisplayName("数据类型"), Category("PLC地址"), Description("本项PLC数据类型。"), ReadOnly(false)]
        public PlcDataType DataType { get; set; } = PlcDataType.Float;

        [DisplayName("字符串字节数"), Category("PLC地址"), Description("String填写1..2000，其他类型必须为0。"), ReadOnly(false)]
        public int StringByteLength { get; set; }

        [DisplayName("保存变量"), Category("结果"), Description("本项读取结果保存到的平台变量。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string VariableName { get; set; }

        public override string ToString()
        {
            return $"{Area}/{StartAddress} → {VariableName}";
        }
    }

    [TypeConverter(typeof(PlcWriteItemConverter))]
    [Serializable]
    public class PlcWriteItem
    {
        [DisplayName("地址区"), Category("PLC地址"), Description("写入仅支持线圈或保持寄存器。"), ReadOnly(false), TypeConverter(typeof(PlcAreaItem))]
        public PlcArea Area { get; set; } = PlcArea.HoldingRegister;

        [DisplayName("地址"), Category("PLC地址"), Description("本项要写入的PLC地址。"), ReadOnly(false)]
        public int StartAddress { get; set; }

        [DisplayName("数据类型"), Category("PLC地址"), Description("本项PLC数据类型。"), ReadOnly(false)]
        public PlcDataType DataType { get; set; } = PlcDataType.Float;

        [DisplayName("字符串字节数"), Category("PLC地址"), Description("String填写1..2000，其他类型必须为0。"), ReadOnly(false)]
        public int StringByteLength { get; set; }

        [DisplayName("数据来源"), Category("写入值"), Description("从变量取值或使用固定值。"), ReadOnly(false), TypeConverter(typeof(PlcValueSourceItem))]
        public PlcValueSource Source { get; set; }

        [DisplayName("来源变量"), Category("写入值"), Description("数据来源为变量时使用。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string VariableName { get; set; }

        [DisplayName("固定值"), Category("写入值"), Description("数据来源为固定值时使用，按数据类型严格解析。"), ReadOnly(false)]
        public string ConstantValue { get; set; }

        public override string ToString()
        {
            return $"{Area}/{StartAddress} ← {(Source == PlcValueSource.Variable ? VariableName : ConstantValue)}";
        }
    }

    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class PlcReadBatch
    {
        [DisplayName("地址区"), Category("连续读取"), ReadOnly(false), TypeConverter(typeof(PlcAreaItem))]
        public PlcArea Area { get; set; } = PlcArea.HoldingRegister;
        [DisplayName("起始地址"), Category("连续读取"), ReadOnly(false)]
        public int StartAddress { get; set; }
        [DisplayName("数据类型"), Category("连续读取"), ReadOnly(false)]
        public PlcDataType DataType { get; set; } = PlcDataType.Float;
        [DisplayName("元素数量"), Category("连续读取"), Description("连续元素数量，不是字节数。"), ReadOnly(false)]
        public int ElementCount { get; set; } = 1;
        [DisplayName("字符串字节数"), Category("连续读取"), ReadOnly(false)]
        public int StringByteLength { get; set; }
        [DisplayName("首保存变量"), Category("连续读取"), Description("后续元素按变量索引连续展开。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string FirstVariableName { get; set; }
        public override string ToString() => string.Empty;
    }

    [TypeConverter(typeof(PlcWriteBatchConverter))]
    [Serializable]
    public class PlcWriteBatch
    {
        [DisplayName("地址区"), Category("连续写入"), ReadOnly(false), TypeConverter(typeof(PlcAreaItem))]
        public PlcArea Area { get; set; } = PlcArea.HoldingRegister;
        [DisplayName("起始地址"), Category("连续写入"), ReadOnly(false)]
        public int StartAddress { get; set; }
        [DisplayName("数据类型"), Category("连续写入"), ReadOnly(false)]
        public PlcDataType DataType { get; set; } = PlcDataType.Float;
        [DisplayName("元素数量"), Category("连续写入"), Description("连续元素数量，不是字节数。"), ReadOnly(false)]
        public int ElementCount { get; set; } = 1;
        [DisplayName("字符串字节数"), Category("连续写入"), ReadOnly(false)]
        public int StringByteLength { get; set; }
        [DisplayName("数据来源"), Category("连续写入"), Description("连续变量，或将一个固定值明确重复写入全部元素。"), ReadOnly(false), TypeConverter(typeof(PlcValueSourceItem))]
        public PlcValueSource Source { get; set; }
        [DisplayName("首来源变量"), Category("连续写入"), Description("后续元素按变量索引连续展开。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string FirstVariableName { get; set; }
        [DisplayName("重复固定值"), Category("连续写入"), Description("固定值来源时，将该值写入全部连续元素。"), ReadOnly(false)]
        public string ConstantValue { get; set; }
        public override string ToString() => string.Empty;
    }

    [Serializable]
    public class PlcMappingControl : OperationType
    {
        public PlcMappingControl()
        {
            OperaType = "PLC映射控制";
            Action = PlcMappingAction.Start;
        }

        [DisplayName("PLC设备"), Category("参数"), Description("按设备独立控制映射。"), ReadOnly(false), TypeConverter(typeof(PlcItem))]
        public string DeviceName { get; set; }

        [DisplayName("控制动作"), Category("参数"), Description("重新初始化、启动映射或停止映射。"), ReadOnly(false), TypeConverter(typeof(PlcMappingActionItem))]
        public PlcMappingAction Action { get; set; }
    }

    [Serializable]
    public class CreateTray : OperationType
    {
        public CreateTray()
        {
            OperaType = "创建料盘";
        }

        [DisplayName("工站名称"), Category("料盘参数设置"), Description("目标工站名称；用于定位料盘所属工站。"), ReadOnly(false), TypeConverter(typeof(StationtItem))]
        public string StationName { get; set; }

        [DisplayName("料盘ID"), Category("料盘参数设置"), Description("料盘模板标识ID，用于区分不同料盘模型。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TrayId { get; set; }

        [DisplayName("行数"), Category("料盘参数设置"), Description("料盘行数；与列数共同确定点位总数。"), ReadOnly(false)]
        [NumericRange(0)]
        public int RowCount { get; set; }

        [DisplayName("列数"), Category("料盘参数设置"), Description("料盘列数；与行数共同确定点位总数。"), ReadOnly(false)]
        [NumericRange(0)]
        public int ColCount { get; set; }

        [DisplayName("左上"), Category("料盘格点设置"), Description("料盘左上角参考点。"), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string PX1 { get; set; }

        [DisplayName("右上"), Category("料盘格点设置"), Description("料盘右上角参考点。"), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string PX2 { get; set; }

        [DisplayName("左下"), Category("料盘格点设置"), Description("料盘左下角参考点。"), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string PY1 { get; set; }

        [DisplayName("右下"), Category("料盘格点设置"), Description("料盘右下角参考点。"), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string PY2 { get; set; }
    }

    [Serializable]
    public class TrayRunPos : OperationType
    {
        public TrayRunPos()
        {
            OperaType = "走料盘点";
        }

        [DisplayName("工站名称"), Category("B.目标工站"), Description("目标工站名称；用于定位料盘所属工站。"), ReadOnly(false), TypeConverter(typeof(StationtItem))]
        public string StationName { get; set; }

        [DisplayName("固定料盘号"), Category("C.料盘号-固定值"), Description("直接填写目标料盘号；如使用变量读取料盘号，此处必须保持 0。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TrayId { get; set; }

        [DisplayName("料盘号变量索引"), Category("D.料盘号-变量读取"), Description("按变量索引读取目标料盘号；与固定料盘号二选一。"), ReadOnly(false)]
        public string TrayIdValueIndex { get; set; }

        [DisplayName("料盘号变量索引二级"), Category("D.料盘号-变量读取"), Description("先按变量索引读取另一个变量索引，再读取该变量中的目标料盘号；与固定料盘号二选一。"), ReadOnly(false)]
        public string TrayIdValueIndex2Index { get; set; }

        [DisplayName("料盘号变量名称"), Category("D.料盘号-变量读取"), Description("按变量名称读取目标料盘号；与固定料盘号二选一。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string TrayIdValueName { get; set; }

        [DisplayName("料盘号变量名称二级"), Category("D.料盘号-变量读取"), Description("先按变量名称读取另一个变量索引，再读取该变量中的目标料盘号；与固定料盘号二选一。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string TrayIdValueName2Index { get; set; }

        [DisplayName("固定料盘位置"), Category("E.料盘位置-固定值"), Description("直接填写目标料盘位置，必须大于 0；如使用变量读取料盘位置，此处必须保持 0。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TrayPos { get; set; }

        [DisplayName("料盘位置变量索引"), Category("F.料盘位置-变量读取"), Description("按变量索引读取目标料盘位置；与固定料盘位置二选一。"), ReadOnly(false)]
        public string TrayPosValueIndex { get; set; }

        [DisplayName("料盘位置变量索引二级"), Category("F.料盘位置-变量读取"), Description("先按变量索引读取另一个变量索引，再读取该变量中的目标料盘位置；与固定料盘位置二选一。"), ReadOnly(false)]
        public string TrayPosValueIndex2Index { get; set; }

        [DisplayName("料盘位置变量名称"), Category("F.料盘位置-变量读取"), Description("按变量名称读取目标料盘位置；与固定料盘位置二选一。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string TrayPosValueName { get; set; }

        [DisplayName("料盘位置变量名称二级"), Category("F.料盘位置-变量读取"), Description("先按变量名称读取另一个变量索引，再读取该变量中的目标料盘位置；与固定料盘位置二选一。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string TrayPosValueName2Index { get; set; }

        [DisplayName("不等待完成"), Category("G.执行选项"), Description("启用后下发动作后不等待运动完成即继续执行。"), ReadOnly(false)]
        public bool ContinueWithoutWaiting { get; set; }
    }

    [Serializable]
    public class HomeRun : OperationType
    {
        public HomeRun()
        {
            OperaType = "回原";
        }

        [DisplayName("工站名称"), Category("参数"), Description("目标工站名称；运行时按名称定位工站对象。"), ReadOnly(false), TypeConverter(typeof(StationtItem))]

        public string StationName { get; set; }
        [DisplayName("工站索引"), Category("参数"), Description("工站索引号；用于按索引定位工站（与工站名称二选一或互补）。"), ReadOnly(false)]

        public int StationIndex { get; set; } = -1;

        [DisplayName("回原模式"), Category("参数"), Description("回原执行模式，决定回零路径与策略。"), ReadOnly(false), TypeConverter(typeof(StationHomeType))]
        public string StationHomeType { get; set; }

        [DisplayName("不等待"), Category("参数"), Description("启用后动作下发后不等待完成，立即继续后续指令。"), ReadOnly(false)]
        public bool ContinueWithoutWaiting { get; set; }

    }

    [Serializable]
    public class StationRunPos : OperationType
    {
        public StationRunPos()
        {
            OperaType = "工站走点";
            RefreshInspector += RefleshPropertyName;
            RefreshInspector += RefleshPropertyVel;
            TimeoutMs = 120000;
        }
        [DisplayName("工站名称"), Category("参数"), Description("目标工站名称；运行时按名称定位工站对象。"), ReadOnly(false), TypeConverter(typeof(StationtItem))]

        public string StationName { get; set; }
        //[DisplayName("工站索引"), Category("参数"), Description(""), ReadOnly(false)]

        //public int StationIndex { get; set; } = -1;

        [DisplayName("点位名称"), Category("参数"), Description("目标点位名称；用于定位工站预设点。"), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string PosName { get; set; }
        [DisplayName("点位索引"), Category("参数"), Description("目标点位索引；用于按索引定位点位。"), ReadOnly(false)]

        public int PosIndex { get; set; } = -1;

        [DisplayName("不等待"), Category("参数"), Description("启用后动作下发后不等待完成，立即继续后续指令。"), ReadOnly(false)]
        public bool ContinueWithoutWaiting { get; set; }

        [DisplayName("检测到位"), Category("参数"), Description("启用后将等待到位信号确认后再继续。"), ReadOnly(false)]
        public bool CheckInPosition { get; set; }

        [DisplayName("超时时间(ms)"), Category("参数"), Description("等待超时时间（ms）；超时后按报警策略处理。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TimeoutMs { get; set; }

        [DisplayName("超时时间(ms)变量"), Category("参数"), Description("等待超时时间变量名；固定超时无效时从变量读取。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string TimeoutVariableName { get; set; }

        private string isDisableAxis = "无禁用";
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false), TypeConverter(typeof(AxisDisableParam))]
        public string IsDisableAxis
        {
            get => isDisableAxis;
            set
            {
                if (isDisableAxis != value)
                {
                    isDisableAxis = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshPropertyName();
                    }
                }
            }
        }

        public List<bool> GetAllValues()
        {
            List<bool> allValues = new List<bool>
        {
            Axis1,
            Axis2,
            Axis3,
            Axis4,
            Axis5,
            Axis6
            };
            return allValues;
        }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public bool Axis1 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public bool Axis2 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public bool Axis3 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public bool Axis4 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public bool Axis5 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public bool Axis6 { get; set; }

        private string changeVel = "不改变";
        [DisplayName("速度设置"), Category("速度设置"), Description("速度参数组入口；统一配置速度/加速度/减速度。"), ReadOnly(false), TypeConverter(typeof(AxisVelParam))]
        public string ChangeVel
        {
            get => changeVel;
            set
            {
                if (changeVel != value)
                {
                    changeVel = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshPropertyVel();
                    }
                }
            }
        }
        private double vel = 0; 
        [Browsable(false)]
        [DisplayName("生产速度能力(%)"), Category("速度设置"), Description("自动生产运动速度百分比，必须在1到100之间。"), ReadOnly(false)]
        [NumericRange(0, 100)]
        public double Vel
        {
            get { return vel; }
            set { vel = ValidatePercentage(value, nameof(Vel)); }
        }
        [DisplayName("速度变量"), Category("速度设置"), Description("速度变量名；用于运行时动态设定速度。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string VelV { get; set; }

        private double acc = 0;
        [Browsable(false)]
        [DisplayName("生产加速能力(%)"), Category("速度设置"), Description("自动生产加速能力百分比；数值越小，加速时间越长。"), ReadOnly(false)]
        [NumericRange(0, 100)]
        public double Acc
        {
            get { return acc; }
            set { acc = ValidatePercentage(value, nameof(Acc)); }
        }
        [DisplayName("加速度变量"), Category("速度设置"), Description("加速度变量名；用于运行时动态设定加速度。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string AccV { get; set; }

        private double dec = 0;
        [Browsable(false)]
        [DisplayName("生产减速能力(%)"), Category("速度设置"), Description("自动生产减速能力百分比；数值越小，减速时间越长。"), ReadOnly(false)]
        [NumericRange(0, 100)]
        public double Dec
        {
            get { return dec; }
            set { dec = ValidatePercentage(value, nameof(Dec)); }
        }
        [DisplayName("减速度变量"), Category("速度设置"), Description("减速度变量名；用于运行时动态设定减速度。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string DecV { get; set; }

        public void RefleshPropertyName()
        {
            string[] AxisName = { "Axis1", "Axis2", "Axis3", "Axis4", "Axis5", "Axis6" };
            if (this.isDisableAxis == "无禁用")
            {
                for (int i = 0; i < 6; i++)
                {
                    SetPropertyAttribute(this, AxisName[i], typeof(BrowsableAttribute), "browsable", false);
                }
                return;
            }

            if (EditorRuntime == null)
            {
                for (int i = 0; i < 6; i++)
                {
                    SetPropertyAttribute(this, AxisName[i], typeof(BrowsableAttribute), "browsable", false);
                }
                return;
            }

            var station = EditorStations.FirstOrDefault(sc => sc.Name == StationName);
            if (station == null)
            {
                for (int i = 0; i < 6; i++)
                {
                    SetPropertyAttribute(this, AxisName[i], typeof(BrowsableAttribute), "browsable", false);
                }
                return;
            }

            int stationIndex = EditorStations.IndexOf(station);
            if (stationIndex != -1)
            {
                for (int j = 0; j < EditorStations[stationIndex].dataAxis.axisConfigs.Count; j++)
                {
                    ushort index = (ushort)j;
                    if (EditorStations[stationIndex].dataAxis.axisConfigs[j].AxisName != "-1")
                    {
                        SetPropertyAttribute(this, AxisName[j], typeof(BrowsableAttribute), "browsable", true);
                        SetDisplayName(typeof(StationRunPos), AxisName[j], EditorStations[stationIndex].dataAxis.axisConfigs[j].AxisName);
                    }
                    else
                    {
                        SetPropertyAttribute(this, AxisName[j], typeof(BrowsableAttribute), "browsable", false);
                    }
                }
            }
        }

        public void RefleshPropertyVel()
        {
            if (this.changeVel == "不改变")
            {
               SetPropertyAttribute(this, "Vel", typeof(BrowsableAttribute), "browsable", false);
               SetPropertyAttribute(this, "Dec", typeof(BrowsableAttribute), "browsable", false);
               SetPropertyAttribute(this, "Acc", typeof(BrowsableAttribute), "browsable", false);

                SetPropertyAttribute(this, "VelV", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "DecV", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "AccV", typeof(BrowsableAttribute), "browsable", false);
            }
            else
            {

                SetPropertyAttribute(this, "Vel", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Dec", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Acc", typeof(BrowsableAttribute), "browsable", true);

                SetPropertyAttribute(this, "VelV", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "DecV", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "AccV", typeof(BrowsableAttribute), "browsable", true);
            }
          
        }
    }
    [Serializable]
    public class ModifyStationPos : OperationType
    {
        public ModifyStationPos()
        {
            OperaType = "点位修改";
            ModifyType = "叠加";
            RefreshInspector += RefleshRefPosMode;
        }

        [DisplayName("工站名称"), Category("A修改参数"), Description("目标工站名称；用于定位要修改的工站。"), ReadOnly(false), TypeConverter(typeof(StationtItem))]
        public string StationName { get; set; }

        private string refPosName;
        [DisplayName("参考点"), Category("A修改参数"), Description("修改基准点位；偏移或覆盖将以此点为参照。"), ReadOnly(false), TypeConverter(typeof(StationPosWithSpecial))]
        public string RefPosName
        {
            get => refPosName;
            set
            {
                if (refPosName != value)
                {
                    refPosName = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshRefPosMode();
                    }
                }
            }
        }

        [DisplayName("目标点"), Category("A修改参数"), Description("待修改的目标点位。"), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string TargetPosName { get; set; }

        [DisplayName("修改方式"), Category("B修改参数"), Description("坐标修改方式（覆盖/增量）。"), ReadOnly(false), TypeConverter(typeof(PointModifyType))]
        public string ModifyType { get; set; }

        [Browsable(false)]
        [DisplayName("X"), Category("B修改参数"), Description("X轴修改值。"), ReadOnly(false)]
        public double CustomX { get; set; }
        [Browsable(false)]
        [DisplayName("Y"), Category("B修改参数"), Description("Y轴修改值。"), ReadOnly(false)]
        public double CustomY { get; set; }
        [Browsable(false)]
        [DisplayName("Z"), Category("B修改参数"), Description("Z轴修改值。"), ReadOnly(false)]
        public double CustomZ { get; set; }
        [Browsable(false)]
        [DisplayName("U"), Category("B修改参数"), Description("U轴修改值。"), ReadOnly(false)]
        public double CustomU { get; set; }
        [Browsable(false)]
        [DisplayName("V"), Category("B修改参数"), Description("V轴修改值。"), ReadOnly(false)]
        public double CustomV { get; set; }
        [Browsable(false)]
        [DisplayName("W"), Category("B修改参数"), Description("W轴修改值。"), ReadOnly(false)]
        public double CustomW { get; set; }

        public void RefleshRefPosMode()
        {
            bool useCustom = refPosName == "自定义坐标";
            SetPropertyAttribute(this, "CustomX", typeof(BrowsableAttribute), "browsable", useCustom);
            SetPropertyAttribute(this, "CustomY", typeof(BrowsableAttribute), "browsable", useCustom);
            SetPropertyAttribute(this, "CustomZ", typeof(BrowsableAttribute), "browsable", useCustom);
            SetPropertyAttribute(this, "CustomU", typeof(BrowsableAttribute), "browsable", useCustom);
            SetPropertyAttribute(this, "CustomV", typeof(BrowsableAttribute), "browsable", useCustom);
            SetPropertyAttribute(this, "CustomW", typeof(BrowsableAttribute), "browsable", useCustom);
        }
    }

    [Serializable]
    public class GetStationPos : OperationType
    {
        public GetStationPos()
        {
            OperaType = "获取工站位置";
            SourceType = "当前位置";
            SaveType = "保存到点位";
            RefreshInspector += RefleshProperty;
            RefleshProperty();
        }

        [DisplayName("工站名称"), Category("A获取参数"), Description("目标工站名称；用于定位位置来源工站。"), ReadOnly(false), TypeConverter(typeof(StationtItem))]
        public string StationName { get; set; }

        private string sourceType;
        [DisplayName("获取方式"), Category("A获取参数"), Description("位置获取方式（当前位置或指定点位）。"), ReadOnly(false), TypeConverter(typeof(StationPosSourceType))]
        public string SourceType
        {
            get => sourceType;
            set
            {
                if (sourceType != value)
                {
                    sourceType = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshProperty();
                    }
                }
            }
        }

        [DisplayName("指定点位"), Category("A获取参数"), Description("当获取方式为指定点位时，从该点位读取坐标。"), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string SourcePosName { get; set; }

        private string saveType;
        [DisplayName("保存方式"), Category("B保存参数"), Description("位置结果保存方式（写点位/写变量）。"), ReadOnly(false), TypeConverter(typeof(StationPosSaveType))]
        public string SaveType
        {
            get => saveType;
            set
            {
                if (saveType != value)
                {
                    saveType = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshProperty();
                    }
                }
            }
        }

        [DisplayName("保存到点位"), Category("B保存参数"), Description("将获取结果写回的目标点位。"), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string TargetPosName { get; set; }

        [DisplayName("X变量索引"), Category("C保存到变量"), Description("X坐标保存变量索引。"), ReadOnly(false)]
        [Browsable(false)]
        public string OutputXIndex { get; set; }
        [DisplayName("X变量索引二级"), Category("C保存到变量"), Description("X坐标保存二级索引。"), ReadOnly(false)]
        [Browsable(false)]
        public string OutputXIndex2Index { get; set; }
        [DisplayName("X变量名称"), Category("C保存到变量"), Description("X坐标保存变量名称。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputXName { get; set; }
        [DisplayName("X变量名称二级"), Category("C保存到变量"), Description("X坐标保存二级变量名称。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputXName2Index { get; set; }

        [DisplayName("Y变量索引"), Category("C保存到变量"), Description("Y坐标保存变量索引。"), ReadOnly(false)]
        [Browsable(false)]
        public string OutputYIndex { get; set; }
        [DisplayName("Y变量索引二级"), Category("C保存到变量"), Description("Y坐标保存二级索引。"), ReadOnly(false)]
        [Browsable(false)]
        public string OutputYIndex2Index { get; set; }
        [DisplayName("Y变量名称"), Category("C保存到变量"), Description("Y坐标保存变量名称。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputYName { get; set; }
        [DisplayName("Y变量名称二级"), Category("C保存到变量"), Description("Y坐标保存二级变量名称。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputYName2Index { get; set; }

        [DisplayName("Z变量索引"), Category("C保存到变量"), Description("Z坐标保存变量索引。"), ReadOnly(false)]
        [Browsable(false)]
        public string OutputZIndex { get; set; }
        [DisplayName("Z变量索引二级"), Category("C保存到变量"), Description("Z坐标保存二级索引。"), ReadOnly(false)]
        [Browsable(false)]
        public string OutputZIndex2Index { get; set; }
        [DisplayName("Z变量名称"), Category("C保存到变量"), Description("Z坐标保存变量名称。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputZName { get; set; }
        [DisplayName("Z变量名称二级"), Category("C保存到变量"), Description("Z坐标保存二级变量名称。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputZName2Index { get; set; }

        [DisplayName("U变量索引"), Category("C保存到变量"), Description("U坐标保存变量索引。"), ReadOnly(false)]
        [Browsable(false)]
        public string OutputUIndex { get; set; }
        [DisplayName("U变量索引二级"), Category("C保存到变量"), Description("U坐标保存二级索引。"), ReadOnly(false)]
        [Browsable(false)]
        public string OutputUIndex2Index { get; set; }
        [DisplayName("U变量名称"), Category("C保存到变量"), Description("U坐标保存变量名称。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputUName { get; set; }
        [DisplayName("U变量名称二级"), Category("C保存到变量"), Description("U坐标保存二级变量名称。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputUName2Index { get; set; }

        [DisplayName("V变量索引"), Category("C保存到变量"), Description("V坐标保存变量索引。"), ReadOnly(false)]
        [Browsable(false)]
        public string OutputVIndex { get; set; }
        [DisplayName("V变量索引二级"), Category("C保存到变量"), Description("V坐标保存二级索引。"), ReadOnly(false)]
        [Browsable(false)]
        public string OutputVIndex2Index { get; set; }
        [DisplayName("V变量名称"), Category("C保存到变量"), Description("V坐标保存变量名称。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputVName { get; set; }
        [DisplayName("V变量名称二级"), Category("C保存到变量"), Description("V坐标保存二级变量名称。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputVName2Index { get; set; }

        [DisplayName("W变量索引"), Category("C保存到变量"), Description("W坐标保存变量索引。"), ReadOnly(false)]
        [Browsable(false)]
        public string OutputWIndex { get; set; }
        [DisplayName("W变量索引二级"), Category("C保存到变量"), Description("W坐标保存二级索引。"), ReadOnly(false)]
        [Browsable(false)]
        public string OutputWIndex2Index { get; set; }
        [DisplayName("W变量名称"), Category("C保存到变量"), Description("W坐标保存变量名称。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputWName { get; set; }
        [DisplayName("W变量名称二级"), Category("C保存到变量"), Description("W坐标保存二级变量名称。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputWName2Index { get; set; }

        public void RefleshProperty()
        {
            bool useSourcePos = sourceType == "指定点位";
            SetPropertyAttribute(this, "SourcePosName", typeof(BrowsableAttribute), "browsable", useSourcePos);

            bool saveToPoint = saveType == "保存到点位";
            SetPropertyAttribute(this, "TargetPosName", typeof(BrowsableAttribute), "browsable", saveToPoint);

            bool saveToValue = saveType == "保存到变量";
            SetPropertyAttribute(this, "OutputXIndex", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputXIndex2Index", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputXName", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputXName2Index", typeof(BrowsableAttribute), "browsable", saveToValue);

            SetPropertyAttribute(this, "OutputYIndex", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputYIndex2Index", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputYName", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputYName2Index", typeof(BrowsableAttribute), "browsable", saveToValue);

            SetPropertyAttribute(this, "OutputZIndex", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputZIndex2Index", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputZName", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputZName2Index", typeof(BrowsableAttribute), "browsable", saveToValue);

            SetPropertyAttribute(this, "OutputUIndex", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputUIndex2Index", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputUName", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputUName2Index", typeof(BrowsableAttribute), "browsable", saveToValue);

            SetPropertyAttribute(this, "OutputVIndex", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputVIndex2Index", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputVName", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputVName2Index", typeof(BrowsableAttribute), "browsable", saveToValue);

            SetPropertyAttribute(this, "OutputWIndex", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputWIndex2Index", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputWName", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputWName2Index", typeof(BrowsableAttribute), "browsable", saveToValue);
        }
    }

    [Serializable]
    public class StationRunRel : OperationType
    {
        public StationRunRel()
        {
            OperaType = "偏移量";
            RefreshInspector += RefleshPropertyName;
            RefreshInspector += RefleshPropertyVel;
            TimeoutMs = 120000;
        }
        private string stationName;
        [DisplayName("工站名称"), Category("参数"), Description("目标工站名称；运行时按名称定位工站对象。"), ReadOnly(false), TypeConverter(typeof(StationtItem))]

        public string StationName
        {
            get => stationName;
            set
            {
                if (stationName != value)
                {
                    stationName = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshPropertyName();
                    }
                }
            }
        }
        //[DisplayName("工站索引"), Category("参数"), Description(""), ReadOnly(false)]

        //public int StationIndex { get; set; } = -1;
        //[DisplayName("点位索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        //public string StationHomeType { get; set; }

        [DisplayName("不等待"), Category("参数"), Description("启用后动作下发后不等待完成，立即继续后续指令。"), ReadOnly(false)]
        public bool ContinueWithoutWaiting { get; set; }

        [DisplayName("检测到位"), Category("参数"), Description("启用后将等待到位信号确认后再继续。"), ReadOnly(false)]
        public bool CheckInPosition { get; set; }

        [DisplayName("超时时间(ms)"), Category("参数"), Description("等待超时时间（ms）；超时后按报警策略处理。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TimeoutMs { get; set; }

        [DisplayName("超时时间(ms)变量"), Category("参数"), Description("等待超时时间变量名；固定超时无效时从变量读取。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string TimeoutVariableName { get; set; }

        public List<double> GetAllValues()
        {
            List<double> allValues = new List<double>
        {
            Axis1,
            Axis2,
            Axis3,
            Axis4,
            Axis5,
            Axis6
            };
            return allValues;
        }
        public List<string> GetAllValuesV()
        {
            List<string> allValues = new List<string>
        {
            Axis1V,
            Axis2V,
            Axis3V,
            Axis4V,
            Axis5V,
            Axis6V
            };
            return allValues;
        }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public double Axis1 { get; set; }
        [DisplayName("轴设置变量"), Category("轴设置"), Description("轴设置变量名；用于动态控制该轴参与状态。"), ReadOnly(false), TypeConverter(typeof(ValueItem)), Browsable(false)]
        public string Axis1V { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public double Axis2 { get; set; }
        [DisplayName("轴设置变量"), Category("轴设置"), Description("轴设置变量名；用于动态控制该轴参与状态。"), ReadOnly(false), TypeConverter(typeof(ValueItem)), Browsable(false)]
        public string Axis2V { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public double Axis3 { get; set; }
        [DisplayName("轴设置变量"), Category("轴设置"), Description("轴设置变量名；用于动态控制该轴参与状态。"), ReadOnly(false), TypeConverter(typeof(ValueItem)), Browsable(false)]
        public string Axis3V { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public double Axis4 { get; set; }
        [DisplayName("轴设置变量"), Category("轴设置"), Description("轴设置变量名；用于动态控制该轴参与状态。"), ReadOnly(false), TypeConverter(typeof(ValueItem)), Browsable(false)]
        public string Axis4V { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public double Axis5 { get; set; }
        [DisplayName("轴设置变量"), Category("轴设置"), Description("轴设置变量名；用于动态控制该轴参与状态。"), ReadOnly(false), TypeConverter(typeof(ValueItem)), Browsable(false)]
        public string Axis5V { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public double Axis6 { get; set; }
        [DisplayName("轴设置变量"), Category("轴设置"), Description("轴设置变量名；用于动态控制该轴参与状态。"), ReadOnly(false), TypeConverter(typeof(ValueItem)), Browsable(false)]
        public string Axis6V { get; set; }

        private string changeVel = "不改变";
        [DisplayName("速度设置"), Category("速度设置"), Description("速度参数组入口；统一配置速度/加速度/减速度。"), ReadOnly(false), TypeConverter(typeof(AxisVelParam))]
        public string ChangeVel
        {
            get => changeVel;
            set
            {
                if (changeVel != value)
                {
                    changeVel = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshPropertyVel();
                    }
                }
            }
        }
        private double vel = 0;
        [Browsable(false)]
        [DisplayName("生产速度能力(%)"), Category("速度设置"), Description("自动生产运动速度百分比，必须在1到100之间。"), ReadOnly(false)]
        [NumericRange(0, 100)]
        public double Vel
        {
            get { return vel; }
            set { vel = ValidatePercentage(value, nameof(Vel)); }
        }
        [DisplayName("速度变量"), Category("速度设置"), Description("速度变量名；用于运行时动态设定速度。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string VelV { get; set; }
        private double acc = 0;
        [Browsable(false)]
        [DisplayName("生产加速能力(%)"), Category("速度设置"), Description("自动生产加速能力百分比；数值越小，加速时间越长。"), ReadOnly(false)]
        [NumericRange(0, 100)]
        public double Acc
        {
            get { return acc; }
            set { acc = ValidatePercentage(value, nameof(Acc)); }
        }
        [DisplayName("加速度变量"), Category("速度设置"), Description("加速度变量名；用于运行时动态设定加速度。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string AccV { get; set; }
        private double dec = 0;
        [Browsable(false)]
        [DisplayName("生产减速能力(%)"), Category("速度设置"), Description("自动生产减速能力百分比；数值越小，减速时间越长。"), ReadOnly(false)]
        [NumericRange(0, 100)]
        public double Dec
        {
            get { return dec; }
            set { dec = ValidatePercentage(value, nameof(Dec)); }
        }
        [DisplayName("减速度变量"), Category("速度设置"), Description("减速度变量名；用于运行时动态设定减速度。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string DecV { get; set; }
        public void RefleshPropertyVel()
        {
            if (this.changeVel == "不改变")
            {
                SetPropertyAttribute(this, "Vel", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Dec", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Acc", typeof(BrowsableAttribute), "browsable", false);

                SetPropertyAttribute(this, "VelV", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "DecV", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "AccV", typeof(BrowsableAttribute), "browsable", false);
            }
            else
            {

                SetPropertyAttribute(this, "Vel", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Dec", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Acc", typeof(BrowsableAttribute), "browsable", true);

                SetPropertyAttribute(this, "VelV", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "DecV", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "AccV", typeof(BrowsableAttribute), "browsable", true);
            }

        }
        public void RefleshPropertyName()
        {
            string[] AxisName = { "Axis1", "Axis2", "Axis3", "Axis4", "Axis5", "Axis6" };

            if (EditorRuntime == null)
            {
                for (int i = 0; i < 6; i++)
                {
                    SetPropertyAttribute(this, AxisName[i], typeof(BrowsableAttribute), "browsable", false);
                    SetPropertyAttribute(this, AxisName[i] + "V", typeof(BrowsableAttribute), "browsable", false);
                }
                return;
            }

            var station = EditorStations.FirstOrDefault(sc => sc.Name == StationName);

            if (station == null)
            {
                for (int i = 0; i < 6; i++)
                {
                    SetPropertyAttribute(this, AxisName[i], typeof(BrowsableAttribute), "browsable", false);
                    SetPropertyAttribute(this, AxisName[i] + "V", typeof(BrowsableAttribute), "browsable", false);
                }
                return;
            }

            int stationIndex = EditorStations.IndexOf(station);
            if (stationIndex != -1)
            {
                for (int j = 0; j < EditorStations[stationIndex].dataAxis.axisConfigs.Count; j++)
                {
                    ushort index = (ushort)j;
                    if (EditorStations[stationIndex].dataAxis.axisConfigs[j].AxisName != "-1")
                    {
                        SetPropertyAttribute(this, AxisName[j], typeof(BrowsableAttribute), "browsable", true);
                        SetPropertyAttribute(this, AxisName[j] + "V", typeof(BrowsableAttribute), "browsable", true);
                        SetDisplayName(typeof(StationRunRel), AxisName[j], EditorStations[stationIndex].dataAxis.axisConfigs[j].AxisName);
                        SetDisplayName(typeof(StationRunRel), AxisName[j] + "V", EditorStations[stationIndex].dataAxis.axisConfigs[j].AxisName + "变量");
                    }
                    else
                    {
                        SetPropertyAttribute(this, AxisName[j], typeof(BrowsableAttribute), "browsable", false);
                        SetPropertyAttribute(this, AxisName[j] + "V", typeof(BrowsableAttribute), "browsable", false);
                    }
                }
            }
        }
    }
    [Serializable]
    public class SetStationVel : OperationType
    {
        public SetStationVel()
        {
            OperaType = "设置速度";
        }
        [DisplayName("工站名称"), Category("参数"), Description("目标工站名称；运行时按名称定位工站对象。"), ReadOnly(false), TypeConverter(typeof(StationtItem))]

        public string StationName { get; set; }
        [DisplayName("工站索引"), Category("参数"), Description("工站索引号；用于按索引定位工站（与工站名称二选一或互补）。"), ReadOnly(false)]

        public int StationIndex { get; set; } = -1;

        [DisplayName("设置对象"), Category("参数"), Description("设置速度生效对象（整站/单轴）。"), ReadOnly(false), TypeConverter(typeof(StationAixsItem))]

        public string SetAxisObj { get; set; }

        private double vel = 0;
        [Browsable(true)]
        [DisplayName("生产速度能力(%)"), Category("速度设置"), Description("持续作用于目标物理轴的自动生产速度百分比，必须在1到100之间。"), ReadOnly(false)]
        [NumericRange(0, 100)]
        public double Vel
        {
            get { return vel; }
            set { vel = ValidatePercentage(value, nameof(Vel)); }
        }
        [DisplayName("速度变量"), Category("速度设置"), Description("速度变量名；用于运行时动态设定速度。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string VelV { get; set; }
        private double acc = 0;
        [Browsable(true)]
        [DisplayName("生产加速能力(%)"), Category("速度设置"), Description("持续作用于目标物理轴；数值越小，加速时间越长。"), ReadOnly(false)]
        [NumericRange(0, 100)]
        public double Acc
        {
            get { return acc; }
            set { acc = ValidatePercentage(value, nameof(Acc)); }
        }
        [DisplayName("加速度变量"), Category("速度设置"), Description("加速度变量名；用于运行时动态设定加速度。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string AccV { get; set; }
        private double dec = 0;
        [Browsable(true)]
        [DisplayName("生产减速能力(%)"), Category("速度设置"), Description("持续作用于目标物理轴；数值越小，减速时间越长。"), ReadOnly(false)]
        [NumericRange(0, 100)]
        public double Dec
        {
            get { return dec; }
            set { dec = ValidatePercentage(value, nameof(Dec)); }
        }
        [DisplayName("减速度变量"), Category("速度设置"), Description("减速度变量名；用于运行时动态设定减速度。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string DecV { get; set; }

    }
    [Serializable]
    public class StationStop : OperationType
    {
        public StationStop()
        {
            OperaType = "停止运动";
            RefreshInspector += RefleshPropertyName;
        }
        private string stationName;
        [DisplayName("工站名称"), Category("参数"), Description("目标工站名称；运行时按名称定位工站对象。"), ReadOnly(false), TypeConverter(typeof(StationtItem))]


        public string StationName
        {
            get => stationName;
            set
            {
                if (stationName != value)
                {
                    stationName = value;
                    if (IsOperationEditorActive)
                    {
                        RefleshPropertyName();
                    }
                }
            }
        }

        public List<bool> GetAllValues()
        {
            List<bool> allValues = new List<bool>
            {
            Axis1,
            Axis2,
            Axis3,
            Axis4,
            Axis5,
            Axis6
            };
            return allValues;
        }
        [DisplayName("是否整体工站停止"), Category("工站设置"), Description("启用后以整站方式停止；关闭则按轴设置逐轴停止。"), ReadOnly(false)]
        public bool StopEntireStation { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public bool Axis1 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public bool Axis2 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public bool Axis3 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public bool Axis4 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public bool Axis5 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description("轴使能/参与设置；决定该轴是否参与本次动作。"), ReadOnly(false)]
        public bool Axis6 { get; set; }

        public void RefleshPropertyName()
        {
            string[] AxisName = { "Axis1", "Axis2", "Axis3", "Axis4", "Axis5", "Axis6" };

            if (EditorRuntime == null)
            {
                for (int i = 0; i < 6; i++)
                {
                    SetPropertyAttribute(this, AxisName[i], typeof(BrowsableAttribute), "browsable", false);
                }
                return;
            }

            var station = EditorStations.FirstOrDefault(sc => sc.Name == StationName);

            if (station == null)
            {
                for (int i = 0; i < 6; i++)
                {
                    SetPropertyAttribute(this, AxisName[i], typeof(BrowsableAttribute), "browsable", false);
                }
                return;
            }

            int stationIndex = EditorStations.IndexOf(station);
            if (stationIndex != -1)
            {
                for (int j = 0; j < EditorStations[stationIndex].dataAxis.axisConfigs.Count; j++)
                {
                    ushort index = (ushort)j;
                    if (EditorStations[stationIndex].dataAxis.axisConfigs[j].AxisName != "-1")
                    {
                        SetPropertyAttribute(this, AxisName[j], typeof(BrowsableAttribute), "browsable", true);
                        SetDisplayName(typeof(StationStop), AxisName[j], EditorStations[stationIndex].dataAxis.axisConfigs[j].AxisName);
                    }
                    else
                    {
                        SetPropertyAttribute(this, AxisName[j], typeof(BrowsableAttribute), "browsable", false);
                    }
                }
            }
        }
    }
    [Serializable]
    public class WaitStationStop : OperationType
    {
        public WaitStationStop()
        {
            OperaType = "等待运动";
            TimeoutMs = 120000;
        }
        [DisplayName("工站名称"), Category("参数"), Description("目标工站名称；运行时按名称定位工站对象。"), ReadOnly(false), TypeConverter(typeof(StationtItem))]

        public string StationName { get; set; }
        [DisplayName("工站索引"), Category("参数"), Description("工站索引号；用于按索引定位工站（与工站名称二选一或互补）。"), ReadOnly(false)]

        public int StationIndex { get; set; } = -1;
        [DisplayName("是否等待回零"), Category("工站设置"), Description("启用后等待工站回零完成状态；关闭则等待普通停止状态。"), ReadOnly(false)]
        public bool WaitForHomeCompleted { get; set; }

        [DisplayName("超时时间(ms)"), Category("参数"), Description("等待超时时间（ms）；超时后按报警策略处理。"), ReadOnly(false)]
        [NumericRange(0)]
        public int TimeoutMs { get; set; }
        [DisplayName("超时时间(ms)变量"), Category("参数"), Description("等待超时时间变量名；固定超时无效时从变量读取。"), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string TimeoutVariableName { get; set; }
    }
}
