using System;

namespace Automation
{
    public static class PlcConstants
    {
        public static readonly string[] Protocols = { "S7", "ModbusTcp" };
        public static readonly string[] DataTypes = { "String", "Boolean", "Byte", "UShort", "Short", "UInt", "Int", "Float", "Double" };
        public static readonly string[] Directions = { "读PLC", "写PLC", "读写" };
        public static readonly string[] CpuTypes = { "S7200", "S7300", "S7400", "S71200", "S71500" };
        public static readonly string[] ModbusAreas = { "C", "DI", "HR", "IR" };
    }

    [Serializable]
    public class PlcDevice
    {
        public string Name { get; set; }
        public string Protocol { get; set; }
        public string CpuType { get; set; }
        public string Ip { get; set; }
        public int Port { get; set; }
        public int Rack { get; set; }
        public int Slot { get; set; }
        public int TimeoutMs { get; set; }
        public int UnitId { get; set; }

        public PlcDevice()
        {
            Protocol = "S7";
            CpuType = "S71200";
            Ip = "192.168.0.1";
            Port = 102;
            Rack = 0;
            Slot = 1;
            TimeoutMs = 3000;
            UnitId = 1;
        }
    }

    [Serializable]
    public class PlcMapItem
    {
        public string PlcName { get; set; }
        public string DataType { get; set; }
        public string Direction { get; set; }
        public string PlcAddress { get; set; }
        public string ValueName { get; set; }
        public int Quantity { get; set; }
        public string WriteConst { get; set; }

        public PlcMapItem()
        {
            DataType = "Float";
            Direction = "读PLC";
            Quantity = 1;
            WriteConst = string.Empty;
        }
    }
}
