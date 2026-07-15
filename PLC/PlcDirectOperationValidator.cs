using System;

namespace Automation
{
    /// <summary>
    /// PLC直接读写指令与运行时共用的地址契约，避免预演校验和实际执行采用不同规则。
    /// </summary>
    internal static class PlcDirectOperationValidator
    {
        public static bool TryValidateAddress(
            PlcArea area, int startAddress, PlcDataType dataType,
            int elementCount, int stringByteLength, bool isWrite, out string error)
        {
            error = null;
            if (!Enum.IsDefined(typeof(PlcArea), area) || !Enum.IsDefined(typeof(PlcDataType), dataType))
            {
                error = "地址区或数据类型无效。";
                return false;
            }
            if (startAddress < 0 || startAddress > 65535)
            {
                error = "起始地址超出0..65535。";
                return false;
            }
            if (elementCount < 1 || elementCount > 1000)
            {
                error = "元素数量必须为1..1000。";
                return false;
            }
            if ((area == PlcArea.Coil || area == PlcArea.DiscreteInput)
                != (dataType == PlcDataType.Boolean))
            {
                error = "Boolean只允许线圈地址区，寄存器类型只允许寄存器地址区。";
                return false;
            }
            if (isWrite && (area == PlcArea.DiscreteInput || area == PlcArea.InputRegister))
            {
                error = "只读地址区禁止写入。";
                return false;
            }
            if (dataType == PlcDataType.String)
            {
                if (elementCount != 1 || stringByteLength < 1 || stringByteLength > 2000)
                {
                    error = "String必须为单元素且字符串字节数为1..2000。";
                    return false;
                }
            }
            else if (stringByteLength != 0)
            {
                error = "非String类型的字符串字节数必须为0。";
                return false;
            }

            var request = new PlcMapConfig
            {
                Area = area,
                StartAddress = startAddress,
                DataType = dataType,
                ElementCount = elementCount,
                StringByteLength = stringByteLength
            };
            if ((long)startAddress + PlcConfigStore.GetAddressSpan(request) - 1 > 65535)
            {
                error = "PLC访问范围超过65535。";
                return false;
            }
            return true;
        }
    }
}
