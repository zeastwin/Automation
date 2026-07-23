namespace Automation
// 模块：通讯运行时。
// 职责范围：管理 TCP/串口通道、原始数据接收、事务等待和通讯配置模型。

{
    public class SocketInfo
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public string Type { get; set; } = "Client";
        public string LocalAddress { get; set; } = "0.0.0.0";
        public int LocalPort { get; set; }
        public string RemoteAddress { get; set; } = "127.0.0.1";
        public int RemotePort { get; set; } = 5000;
        public bool AutoReconnect { get; set; } = true;
        public string EncodingName { get; set; } = "UTF-8";
        public int ConnectTimeoutMs { get; set; } = 5000;
    }

    public class SerialPortInfo
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public string Port { get; set; }
        public string BitRate { get; set; }
        public string CheckBit { get; set; }
        public string DataBit { get; set; }
        public string StopBit { get; set; }
        public string EncodingName { get; set; } = "UTF-8";
    }
}
