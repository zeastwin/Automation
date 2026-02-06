using System;

namespace Automation
{
    public class SocketInfo
    {
        public int ID { get; set; }
        public string ChannelId { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; }
        public string Type { get; set; }
        public int Port { get; set; }
        public string Address { get; set; }
        public string FrameMode { get; set; } = "Delimiter";
        public string FrameDelimiter { get; set; } = "\\n";
        public string EncodingName { get; set; } = "UTF-8";
        public int ConnectTimeoutMs { get; set; } = 5000;
        public bool isServering { get; set; }
    }

    public class SerialPortInfo
    {
        public int ID { get; set; }
        public string ChannelId { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; }
        public string Port { get; set; }
        public string BitRate { get; set; }
        public string CheckBit { get; set; }
        public string DataBit { get; set; }
        public string StopBit { get; set; }
        public string FrameMode { get; set; } = "Delimiter";
        public string FrameDelimiter { get; set; } = "\\n";
        public string EncodingName { get; set; } = "UTF-8";
    }
}
