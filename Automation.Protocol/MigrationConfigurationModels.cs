using System.Collections.Generic;
using System.ComponentModel;

namespace Automation.Protocol
{
    public sealed class MotionIoMigrationDefinition
    {
        [Description("完整控制卡列表；列表顺序就是cardIndex。")]
        public List<ControlCardMigrationDefinition> ControlCards { get; set; } = new List<ControlCardMigrationDefinition>();
        [Description("完整IO映射；每项通过cardIndex关联控制卡。每张卡的输入/输出项数必须与控制卡声明一致。")]
        public List<IoMigrationDefinition> IoMappings { get; set; } = new List<IoMigrationDefinition>();
    }

    public sealed class ControlCardMigrationDefinition
    {
        [Description("平台注册的控制卡类型名称。")]
        public string CardType { get; set; } = string.Empty;
        [Description("该卡通用输入点总数，必须与ioMappings中的通用输入数量一致。")]
        public int InputCount { get; set; }
        [Description("该卡通用输出点总数，必须与ioMappings中的通用输出数量一致。")]
        public int OutputCount { get; set; }
        [Description("完整轴列表；轴号按列表顺序从0生成。")]
        public List<AxisMigrationDefinition> Axes { get; set; } = new List<AxisMigrationDefinition>();
    }

    public sealed class AxisMigrationDefinition
    {
        [Description("轴名称；同一控制卡内唯一且非空。")]
        public string Name { get; set; } = string.Empty;
        [Description("单位毫米脉冲，必须大于0。")]
        public int PulseToMm { get; set; }
        public string HomeDirection { get; set; } = string.Empty;
        public string HomeSpeed { get; set; } = string.Empty;
        public int SpeedInfo { get; set; }
        [Description("最大速度，必须大于0。")]
        public int MaxSpeed { get; set; }
        [Description("加速度时间，必须大于0。")]
        public double AccelerationTime { get; set; }
        [Description("减速度时间，必须大于0。")]
        public double DecelerationTime { get; set; }
    }

    public sealed class IoMigrationDefinition
    {
        [Description("IO名称；未命名的保留点可为空，非空名称在全部控制卡中必须唯一。")]
        public string Name { get; set; } = string.Empty;
        [Description("控制卡列表的0基索引。")]
        public int CardIndex { get; set; }
        public int Module { get; set; }
        public string IoIndex { get; set; } = string.Empty;
        [Description("严格枚举：通用输入 或 通用输出。")]
        public string IoType { get; set; } = string.Empty;
        public string UsedType { get; set; } = "通用";
        public string EffectLevel { get; set; } = "正常";
        public string Note { get; set; } = string.Empty;
        public bool IsRemark { get; set; }
    }

    public sealed class IoDebugMigrationDefinition
    {
        [Description("按名称选择需要在IO调试页显示的通用输入；名称必须已存在。")]
        public List<string> InputNames { get; set; } = new List<string>();
        [Description("按名称选择需要在IO调试页显示的通用输出；名称必须已存在。")]
        public List<string> OutputNames { get; set; } = new List<string>();
        public List<IoDebugConnectionMigrationDefinition> Group1 { get; set; } = new List<IoDebugConnectionMigrationDefinition>();
        public List<IoDebugConnectionMigrationDefinition> Group2 { get; set; } = new List<IoDebugConnectionMigrationDefinition>();
        public List<IoDebugConnectionMigrationDefinition> Group3 { get; set; } = new List<IoDebugConnectionMigrationDefinition>();
    }

    public sealed class IoDebugConnectionMigrationDefinition
    {
        public string Output1 { get; set; } = string.Empty;
        public string Output2 { get; set; } = string.Empty;
        public string Input1 { get; set; } = string.Empty;
        public string Input2 { get; set; } = string.Empty;
    }

    public sealed class PlcMigrationDefinition
    {
        [Description("PLC设备完整目标列表。")]
        public List<PlcDeviceMigrationDefinition> Devices { get; set; } = new List<PlcDeviceMigrationDefinition>();
    }

    public sealed class PlcDeviceMigrationDefinition
    {
        [Description("PLC设备唯一名称。")]
        public string Name { get; set; } = string.Empty;
        [Description("严格枚举：GenericModbusTcp 或 InovanceModbusTcp。")]
        public string Profile { get; set; } = "GenericModbusTcp";
        [Description("IPv4地址。")]
        public string IpAddress { get; set; } = string.Empty;
        [Description("TCP端口，范围1..65535。")]
        public int Port { get; set; } = 502;
        [Description("Modbus站号，范围0..255。")]
        public int UnitId { get; set; } = 1;
        [Description("连接超时，范围100..60000毫秒。")]
        public int ConnectTimeoutMs { get; set; } = 1000;
        public bool AutoConnect { get; set; } = true;
        [Description("扫描周期，范围50..60000毫秒。")]
        public int ScanIntervalMs { get; set; } = 50;
        [Description("严格枚举：ABCD、BADC、CDAB、DCBA。")]
        public string DataFormat { get; set; } = "CDAB";
        public bool IsStringReverse { get; set; }
        public bool AddressStartWithZero { get; set; } = true;
        [Description("可选的double状态变量精确名称；为空表示不绑定。")]
        public string StatusVariableName { get; set; } = string.Empty;
        [Description("该设备的完整地址映射。")]
        public List<PlcMapMigrationDefinition> Mappings { get; set; } = new List<PlcMapMigrationDefinition>();
    }

    public sealed class PlcMapMigrationDefinition
    {
        [Description("32位N格式Guid；新增时可省略，由Bridge生成。")]
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        [Description("严格枚举：Coil、DiscreteInput、HoldingRegister、InputRegister。")]
        public string Area { get; set; } = "HoldingRegister";
        [Description("起始地址，范围0..65535。")]
        public int StartAddress { get; set; }
        [Description("严格枚举：String、Boolean、Byte、UShort、Short、UInt、Int、Float、Double。")]
        public string DataType { get; set; } = "Float";
        [Description("严格枚举：ReadFromPlc、WriteToPlc、Bidirectional。")]
        public string Direction { get; set; } = "ReadFromPlc";
        [Description("严格枚举：Low、Medium、High。")]
        public string Priority { get; set; } = "High";
        [Description("元素数量，范围1..1000；必须与variableNames数量一致。")]
        public int ElementCount { get; set; } = 1;
        [Description("String类型为1..2000，其他类型必须为0。")]
        public int StringByteLength { get; set; }
        [Description("映射到的变量精确名称列表；数量必须等于elementCount。")]
        public List<string> VariableNames { get; set; } = new List<string>();
        [Description("非负有限数；只有Float和Double允许非零值。")]
        public double ChangeTolerance { get; set; }
    }

    public sealed class CommunicationMigrationDefinition
    {
        [Description("完整TCP配置列表。")]
        public List<TcpMigrationDefinition> Tcp { get; set; } = new List<TcpMigrationDefinition>();
        [Description("完整串口配置列表。")]
        public List<SerialMigrationDefinition> Serial { get; set; } = new List<SerialMigrationDefinition>();
    }

    public sealed class TcpMigrationDefinition
    {
        [Description("正整数编号；在TCP列表内唯一。")]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        [Description("严格枚举：Server 或 Client。")]
        public string Type { get; set; } = string.Empty;
        [Description("端口，范围1..65535。")]
        public int Port { get; set; }
        [Description("监听或远端IP地址。")]
        public string Address { get; set; } = string.Empty;
        [Description("严格枚举：Raw 或 Delimiter。")]
        public string FrameMode { get; set; } = "Raw";
        public string FrameDelimiter { get; set; } = "\\n";
        public string EncodingName { get; set; } = "UTF-8";
        [Description("连接超时毫秒数，必须大于0。")]
        public int ConnectTimeoutMs { get; set; } = 5000;
    }

    public sealed class SerialMigrationDefinition
    {
        [Description("正整数编号；在串口列表内唯一。")]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        [Description("串口名称，例如COM1；不同配置不得重复占用。")]
        public string Port { get; set; } = string.Empty;
        [Description("正整数波特率文本，例如115200。")]
        public string BitRate { get; set; } = string.Empty;
        [Description("System.IO.Ports.Parity枚举名称，例如None、Odd、Even。")]
        public string CheckBit { get; set; } = string.Empty;
        [Description("数据位文本，范围5..8。")]
        public string DataBit { get; set; } = string.Empty;
        [Description("System.IO.Ports.StopBits枚举名称，例如One、Two。")]
        public string StopBit { get; set; } = string.Empty;
        [Description("严格枚举：Raw 或 Delimiter。")]
        public string FrameMode { get; set; } = "Delimiter";
        public string FrameDelimiter { get; set; } = "\\n";
        public string EncodingName { get; set; } = "UTF-8";
    }

}
