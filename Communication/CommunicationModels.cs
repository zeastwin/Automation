namespace Automation
{
    public class SocketInfo
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public int Port { get; set; }
        public string Address { get; set; }
        public bool isServering { get; set; }
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
    }
}
