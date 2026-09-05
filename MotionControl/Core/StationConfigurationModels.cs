// 模块：运动控制 / 工站配置。
// 职责范围：定义轴工站与机器人工站共用的六轴配置和点位持久化模型。

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Automation.MotionControl;

namespace Automation
{
    /// <summary>
    /// 工站类型值沿用 3.0 运动模块的持久化约定。
    /// </summary>
    public enum StationType
    {
        Axis = 0,
        Epson = 1,
        Inovance = 2,
        InovanceV4 = 3
    }

    public class DataStation : ICustomTypeDescriptor
    {
        public const int PointCapacity = 400;
        public const int RobotPointCapacity = 200;

        public static int GetPointCapacity(StationType type)
        {
            return type == StationType.Axis ? PointCapacity : RobotPointCapacity;
        }

        [DisplayName("站名"), Category("A基本参数"), Description(""), ReadOnly(false)]
        public string Name { get; set; }

        [DisplayName("工站类型"), Category("A基本参数"), Description("轴、EPSON 与汇川机器人统一按六轴工站配置。"), ReadOnly(false), RefreshProperties(RefreshProperties.All)]
        public StationType Type { get; set; }

        [DisplayName("坐标系"), Category("A基本参数"), Description("协调直线运动使用的控制器坐标系编号。"), ReadOnly(false)]
        [NumericRange(0, CoordinatedLinearMoveRequest.MaximumCoordinateSystem)]
        public ushort CoordinateSystem { get; set; }

        [DisplayName("前瞻使能"), Category("D连续轨迹"), Description("沿用3.0连续插补前瞻开关。"), ReadOnly(false)]
        public bool LookAheadEnabled { get; set; }

        [DisplayName("轨迹误差"), Category("D连续轨迹"), Description("连续插补前瞻允许的路径误差。"), ReadOnly(false)]
        [NumericRange(0)]
        public double PathError { get; set; }

        [DisplayName("前瞻加速度倍数"), Category("D连续轨迹"), Description("轨迹实际加速度的前瞻放大倍数；默认沿用3.0的2000。"), ReadOnly(false)]
        [NumericRange(0.000001)]
        public double LookAheadAccelerationMultiplier { get; set; } = 2000;

        [DisplayName("插补最大速度"), Category("D连续轨迹"), Description("连续插补速度基准；默认沿用3.0的20。"), ReadOnly(false)]
        [NumericRange(0.000001)]
        public double ContinuousPathMaximumVelocity { get; set; } = 20;

        [DisplayName("插补最大加速度"), Category("D连续轨迹"), Description("连续插补加速度基准；默认沿用3.0的200。"), ReadOnly(false)]
        [NumericRange(0.000001)]
        public double ContinuousPathMaximumAcceleration { get; set; } = 200;

        [DisplayName("插补最大减速度"), Category("D连续轨迹"), Description("连续插补减速度基准；默认沿用3.0的200。"), ReadOnly(false)]
        [NumericRange(0.000001)]
        public double ContinuousPathMaximumDeceleration { get; set; } = 200;

        /// <summary>
        /// XYZUVW 六个通道的到位精度，语义沿用 3.0 StationInfo.beta。
        /// </summary>
        [Browsable(false)]
        public double[] PositionTolerances { get; set; }

        [DisplayName("机器人通讯对象"), Category("C机器人工站配置"), Description("引用当前平台通讯配置中的 TCP 对象名称。"), ReadOnly(false), TypeConverter(typeof(RobotCommunicationItem))]
        public string CommunicationName { get; set; }

        [DisplayName("从机器人加载点位"), Category("C机器人工站配置"), Description("是否优先从机器人控制器加载点位。"), DefaultValue(true), ReadOnly(false)]
        public bool PointFromRobot { get; set; }

        [DisplayName("远程模式"), Category("C机器人工站配置"), Description("是否使用机器人远程控制模式。"), DefaultValue(false), ReadOnly(false)]
        public bool RemoteMode { get; set; }

        [DisplayName("机器人远程通讯对象"), Category("C机器人工站配置"), Description("远程模式登录、复位和启动使用的独立 TCP 对象。"), ReadOnly(false), TypeConverter(typeof(RobotCommunicationItem))]
        public string RemoteCommunicationName { get; set; }

        [DisplayName("轴配置"), Category("B工站配置"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public DataAxis dataAxis { get; set; }

        [DisplayName("轴回原顺序"), Category("B工站配置"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public HomeSeq homeSeq { get; set; }

        [Browsable(false)]
        public Dictionary<string, DataPos> dicDataPos { get; set; }

        [Browsable(false)]
        public List<DataPos> ListDataPos;

        private double manualSpeedPercent = 10;

        [Browsable(false)]
        public double ManualSpeedPercent
        {
            get => manualSpeedPercent;
            set
            {
                if (value < 1 || value > 100 || double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "手动调试速度百分比必须在1到100之间。");
                }
                manualSpeedPercent = value;
            }
        }

        public DataStation(bool isnull)
        {
            Type = StationType.Axis;
            CommunicationName = string.Empty;
            RemoteCommunicationName = string.Empty;
            PointFromRobot = true;
            LookAheadAccelerationMultiplier = 2000;
            ContinuousPathMaximumVelocity = 20;
            ContinuousPathMaximumAcceleration = 200;
            ContinuousPathMaximumDeceleration = 200;
            PositionTolerances = CreateDefaultPositionTolerances();
            dataAxis = new DataAxis(Name);
            homeSeq = new HomeSeq(Name);
            dicDataPos = new Dictionary<string, DataPos>();
            ListDataPos = new List<DataPos>();

            if (!isnull)
            {
                for (int i = 0; i < PointCapacity; i++)
                {
                    DataPos dataPos = dicDataPos.Values.FirstOrDefault(item => item.Index == i);
                    ListDataPos.Add(dataPos ?? new DataPos(i));
                }
            }
        }

        internal void NormalizeConfiguration()
        {
            CommunicationName = CommunicationName ?? string.Empty;
            RemoteCommunicationName = RemoteCommunicationName ?? string.Empty;
            PositionTolerances = NormalizePositionTolerances(PositionTolerances);
            dataAxis = dataAxis ?? new DataAxis(Name);
            homeSeq = homeSeq ?? new HomeSeq(Name);
            dicDataPos = dicDataPos ?? new Dictionary<string, DataPos>();
            ListDataPos = ListDataPos ?? new List<DataPos>();
            dataAxis.NormalizeConfiguration();
            homeSeq.NormalizeConfiguration();
            if (double.IsNaN(PathError) || double.IsInfinity(PathError) || PathError < 0)
            {
                throw new InvalidOperationException($"工站{Name ?? "<未命名>"}轨迹误差必须是大于等于0的有限数。");
            }
            if (double.IsNaN(LookAheadAccelerationMultiplier)
                || double.IsInfinity(LookAheadAccelerationMultiplier)
                || LookAheadAccelerationMultiplier <= 0)
            {
                throw new InvalidOperationException($"工站{Name ?? "<未命名>"}前瞻加速度倍数必须是大于0的有限数。");
            }
            if (!IsFinitePositive(ContinuousPathMaximumVelocity)
                || !IsFinitePositive(ContinuousPathMaximumAcceleration)
                || !IsFinitePositive(ContinuousPathMaximumDeceleration))
            {
                throw new InvalidOperationException(
                    $"工站{Name ?? "<未命名>"}插补最大速度、加速度和减速度必须是大于0的有限数。");
            }
        }

        private static bool IsFinitePositive(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double[] CreateDefaultPositionTolerances()
        {
            return new[] { 0.01, 0.01, 0.01, 0.01, 0.01, 0.01 };
        }

        private static double[] NormalizePositionTolerances(double[] value)
        {
            double[] normalized = CreateDefaultPositionTolerances();
            if (value != null)
            {
                Array.Copy(value, normalized, Math.Min(value.Length, normalized.Length));
            }
            return normalized;
        }

        AttributeCollection ICustomTypeDescriptor.GetAttributes() =>
            TypeDescriptor.GetAttributes(this, true);

        string ICustomTypeDescriptor.GetClassName() =>
            TypeDescriptor.GetClassName(this, true);

        string ICustomTypeDescriptor.GetComponentName() =>
            TypeDescriptor.GetComponentName(this, true);

        TypeConverter ICustomTypeDescriptor.GetConverter() =>
            TypeDescriptor.GetConverter(this, true);

        EventDescriptor ICustomTypeDescriptor.GetDefaultEvent() =>
            TypeDescriptor.GetDefaultEvent(this, true);

        PropertyDescriptor ICustomTypeDescriptor.GetDefaultProperty() =>
            TypeDescriptor.GetDefaultProperty(this, true);

        object ICustomTypeDescriptor.GetEditor(System.Type editorBaseType) =>
            TypeDescriptor.GetEditor(this, editorBaseType, true);

        EventDescriptorCollection ICustomTypeDescriptor.GetEvents(Attribute[] attributes) =>
            TypeDescriptor.GetEvents(this, attributes, true);

        EventDescriptorCollection ICustomTypeDescriptor.GetEvents() =>
            TypeDescriptor.GetEvents(this, true);

        PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties(Attribute[] attributes) =>
            GetStationProperties(attributes);

        PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties() =>
            GetStationProperties(null);

        object ICustomTypeDescriptor.GetPropertyOwner(PropertyDescriptor property) => this;

        private PropertyDescriptorCollection GetStationProperties(Attribute[] attributes)
        {
            PropertyDescriptorCollection all = attributes == null
                ? TypeDescriptor.GetProperties(this, true)
                : TypeDescriptor.GetProperties(this, attributes, true);
            string[] hidden = Type == StationType.Axis
                ? new[] { nameof(CommunicationName), nameof(PointFromRobot), nameof(RemoteMode), nameof(RemoteCommunicationName) }
                : new[] { nameof(CoordinateSystem), nameof(dataAxis), nameof(homeSeq) };
            var visible = new List<PropertyDescriptor>();
            foreach (PropertyDescriptor property in all)
            {
                if (!hidden.Contains(property.Name, StringComparer.Ordinal))
                {
                    visible.Add(property);
                }
            }
            return new PropertyDescriptorCollection(visible.ToArray(), true);
        }
    }

    [Serializable]
    public class DataPos : ICloneable
    {
        private const double DefaultPositionLimit = 2000;

        public int Index { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// 点位坐标是否已由人工编辑、取点或运行时真实采集确认。
        /// null 表示旧版本数据；为兼容既有已用点位，旧数据按已示教处理。
        /// AI 只登记名称时写入 false，不能据此执行运动。
        /// </summary>
        [Browsable(false)]
        public bool? IsTaught { get; set; }

        /// <summary>
        /// 点位分组和启用状态沿用 3.0 NGroup 语义；禁用点位保留在配置中但不下发运动。
        /// </summary>
        [Browsable(false)]
        public string GroupName { get; set; }

        [Browsable(false)]
        public bool GroupVisible { get; set; }

        [Browsable(false)]
        public bool Enabled { get; set; }

        [Browsable(false), JsonIgnore]
        public bool IsMotionReady => !string.IsNullOrWhiteSpace(Name) && IsTaught != false;

        [Browsable(false), JsonIgnore]
        public string TeachingState => string.IsNullOrWhiteSpace(Name)
            ? "empty"
            : IsTaught == false ? "planned" : "taught";

        [Browsable(false), JsonIgnore]
        public string TeachingStateDisplay => string.IsNullOrWhiteSpace(Name)
            ? string.Empty
            : IsTaught == false ? "待示教" : "已示教";

        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double U { get; set; }
        public double V { get; set; }
        public double W { get; set; }

        /// <summary>
        /// 机器人姿态：手势、肘势、腕势、臂姿势，语义沿用 3.0 NPoint.pose。
        /// </summary>
        [Browsable(false)]
        public short[] Pose { get; set; }

        /// <summary>
        /// 点位速度参数，语义沿用 3.0 NPoint.vel。
        /// </summary>
        [Browsable(false)]
        public double[] Velocity { get; set; }

        /// <summary>
        /// XYZUVW 六维坐标上下限，每项依次为下限和上限。
        /// </summary>
        [Browsable(false)]
        public double[][] PositionLimits { get; set; }

        [Browsable(false)]
        public string Description { get; set; }

        public List<double> GetAllValues()
        {
            return new List<double>
            {
                X,
                Y,
                Z,
                U,
                V,
                W
            };
        }

        public object Clone()
        {
            return ObjectGraphCloner.Clone(this);
        }

        public DataPos(int index)
        {
            Index = index;
            Name = string.Empty;
            Description = string.Empty;
            GroupName = "通用";
            GroupVisible = true;
            Enabled = true;
            Pose = CreateDefaultPose();
            Velocity = new double[4];
            PositionLimits = CreateDefaultPositionLimits();
        }

        internal void NormalizeRobotMetadata()
        {
            Name = Name ?? string.Empty;
            Description = Description ?? string.Empty;
            GroupName = string.IsNullOrWhiteSpace(GroupName) ? "通用" : GroupName;
            Pose = NormalizeArray(Pose, CreateDefaultPose());
            Velocity = NormalizeArray(Velocity, new double[4]);
            PositionLimits = NormalizePositionLimits(PositionLimits);
        }

        private static short[] CreateDefaultPose()
        {
            return new short[] { 1, 0, 0, 0 };
        }

        private static double[][] CreateDefaultPositionLimits()
        {
            double[][] limits = new double[6][];
            for (int i = 0; i < limits.Length; i++)
            {
                limits[i] = new[] { -DefaultPositionLimit, DefaultPositionLimit };
            }
            return limits;
        }

        private static T[] NormalizeArray<T>(T[] value, T[] defaults)
        {
            if (value == null)
            {
                return defaults;
            }
            if (value.Length >= defaults.Length)
            {
                return value;
            }
            T[] normalized = defaults;
            Array.Copy(value, normalized, value.Length);
            return normalized;
        }

        private static double[][] NormalizePositionLimits(double[][] value)
        {
            double[][] normalized = CreateDefaultPositionLimits();
            if (value == null)
            {
                return normalized;
            }
            int coordinateCount = Math.Min(value.Length, normalized.Length);
            for (int i = 0; i < coordinateCount; i++)
            {
                if (value[i] == null)
                {
                    continue;
                }
                if (value[i].Length > 0)
                {
                    normalized[i][0] = value[i][0];
                }
                if (value[i].Length > 1)
                {
                    normalized[i][1] = value[i][1];
                }
            }
            return normalized;
        }
    }

    public class DataAxis
    {
        [Browsable(false)]
        public string Name { get; set; }

        public override string ToString()
        {
            return string.Empty;
        }

        [DisplayName("轴1"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisConfig axisConfig1 { get; set; }

        [DisplayName("轴2"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisConfig axisConfig2 { get; set; }

        [DisplayName("轴3"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisConfig axisConfig3 { get; set; }

        [DisplayName("轴4"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisConfig axisConfig4 { get; set; }

        [DisplayName("轴5"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisConfig axisConfig5 { get; set; }

        [DisplayName("轴6"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisConfig axisConfig6 { get; set; }

        [Browsable(false), JsonIgnore]
        public List<AxisConfig> axisConfigs = new List<AxisConfig>();

        public DataAxis(string name)
        {
            Name = name;
            axisConfig1 = new AxisConfig();
            axisConfig2 = new AxisConfig();
            axisConfig3 = new AxisConfig();
            axisConfig4 = new AxisConfig();
            axisConfig5 = new AxisConfig();
            axisConfig6 = new AxisConfig();
            NormalizeConfiguration();
        }

        internal void NormalizeConfiguration()
        {
            axisConfig1 = axisConfig1 ?? new AxisConfig();
            axisConfig2 = axisConfig2 ?? new AxisConfig();
            axisConfig3 = axisConfig3 ?? new AxisConfig();
            axisConfig4 = axisConfig4 ?? new AxisConfig();
            axisConfig5 = axisConfig5 ?? new AxisConfig();
            axisConfig6 = axisConfig6 ?? new AxisConfig();
            axisConfigs = axisConfigs ?? new List<AxisConfig>();
            axisConfigs.Clear();
            axisConfigs.AddRange(new[]
            {
                axisConfig1,
                axisConfig2,
                axisConfig3,
                axisConfig4,
                axisConfig5,
                axisConfig6
            });
        }
    }

    public class AxisConfig
    {
        public override string ToString()
        {
            return string.Empty;
        }

        private string cardNum;

        [DisplayName("卡编号"), Description(""), ReadOnly(false), TypeConverter(typeof(CardItem))]
        public string CardNum
        {
            get => cardNum;
            set => cardNum = value;
        }

        [Browsable(false)]
        public Axis axis { get; set; }

        public string axisName;

        [DisplayName("轴名称"), Description(""), ReadOnly(false), TypeConverter(typeof(AxisItem))]
        public string AxisName
        {
            get => axisName;
            set
            {
                axisName = value;
                PlatformRuntime runtime = EditorServiceRegistry.GetRuntime(this);
                if (value != "-1"
                    && runtime?.Editor.ActiveSession?.Draft is DataStation
                    && int.TryParse(CardNum, out int cardNumber)
                    && runtime.Stores.Cards.TryGetAxisByName(cardNumber, value, out Axis resolvedAxis))
                {
                    axis = resolvedAxis;
                }
            }
        }

        public AxisConfig()
        {
            AxisName = "-1";
            CardNum = "-1";
        }
    }

    public class AxisName
    {
        public override string ToString()
        {
            return string.Empty;
        }

        [DisplayName("回原轴"), Description(""), ReadOnly(false), TypeConverter(typeof(AxisItem))]
        public string Name { get; set; }

        public AxisName()
        {
            Name = "-1";
        }
    }

    public class HomeSeq
    {
        [Browsable(false)]
        public string Name { get; set; }

        [DisplayName("第1回原轴"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisName AxisName1 { get; set; }

        [DisplayName("第2回原轴"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisName AxisName2 { get; set; }

        [DisplayName("第3回原轴"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisName AxisName3 { get; set; }

        [DisplayName("第4回原轴"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisName AxisName4 { get; set; }

        [DisplayName("第5回原轴"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisName AxisName5 { get; set; }

        [DisplayName("第6回原轴"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisName AxisName6 { get; set; }

        [Browsable(false), JsonIgnore]
        public List<AxisName> axisSeq = new List<AxisName>();

        public override string ToString()
        {
            return string.Empty;
        }

        public HomeSeq(string name)
        {
            Name = name;
            AxisName1 = new AxisName();
            AxisName2 = new AxisName();
            AxisName3 = new AxisName();
            AxisName4 = new AxisName();
            AxisName5 = new AxisName();
            AxisName6 = new AxisName();
            NormalizeConfiguration();
        }

        internal void NormalizeConfiguration()
        {
            AxisName1 = AxisName1 ?? new AxisName();
            AxisName2 = AxisName2 ?? new AxisName();
            AxisName3 = AxisName3 ?? new AxisName();
            AxisName4 = AxisName4 ?? new AxisName();
            AxisName5 = AxisName5 ?? new AxisName();
            AxisName6 = AxisName6 ?? new AxisName();
            axisSeq = axisSeq ?? new List<AxisName>();
            axisSeq.Clear();
            axisSeq.AddRange(new[]
            {
                AxisName1,
                AxisName2,
                AxisName3,
                AxisName4,
                AxisName5,
                AxisName6
            });
        }
    }
}
