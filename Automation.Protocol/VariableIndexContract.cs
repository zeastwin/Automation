namespace Automation.Protocol
{
    /// <summary>
    /// 变量固定槽位分区契约，供平台 Store、Bridge 与 MCP Schema 共享。
    /// </summary>
    public static class VariableIndexContract
    {
        public const int NormalValueCapacity = 1000;
        public const int SystemValueCapacity = 200;
        public const int SystemValueStartIndex = NormalValueCapacity;
        public const int ValueCapacity = NormalValueCapacity + SystemValueCapacity;
        public const int MaximumNormalValueIndex = NormalValueCapacity - 1;
        public const int MaximumValueIndex = ValueCapacity - 1;

        public const string NormalValueIndexRange = "[0,1000)";
        public const string SystemValueIndexRange = "[1000,1200)";
        public const string ValueIndexRange = "[0,1200)";

        public static bool IsValidIndex(int index)
        {
            return index >= 0 && index < ValueCapacity;
        }

        public static bool IsSystemIndex(int index)
        {
            return index >= SystemValueStartIndex && index < ValueCapacity;
        }
    }
}
