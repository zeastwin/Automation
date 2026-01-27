using System;

namespace Automation
{
    public partial class ProcessEngine
    {
        public bool RunPlcReadWrite(ProcHandle evt, PlcReadWrite plcReadWrite)
        {
            if (plcReadWrite == null)
            {
                MarkAlarm(evt, "PLC读写参数为空");
                return false;
            }
            if (Context?.PlcStore == null)
            {
                MarkAlarm(evt, "PLC通讯未初始化");
                return false;
            }
            if (string.IsNullOrWhiteSpace(plcReadWrite.PlcName))
            {
                MarkAlarm(evt, "PLC名称为空");
                return false;
            }
            if (string.IsNullOrWhiteSpace(plcReadWrite.DataType))
            {
                MarkAlarm(evt, "PLC数据类型为空");
                return false;
            }
            if (string.IsNullOrWhiteSpace(plcReadWrite.DataOps))
            {
                MarkAlarm(evt, "PLC读写类型为空");
                return false;
            }
            if (string.IsNullOrWhiteSpace(plcReadWrite.PlcAddress))
            {
                MarkAlarm(evt, "PLC地址为空");
                return false;
            }
            if (plcReadWrite.Quantity <= 0)
            {
                MarkAlarm(evt, "PLC数据数量无效");
                return false;
            }
            bool doRead;
            bool doWrite;
            if (!TryParsePlcOps(plcReadWrite.DataOps, out doRead, out doWrite, out string opError))
            {
                MarkAlarm(evt, opError);
                return false;
            }

            ValueConfigStore valueStore = Context.ValueStore;
            if (doWrite)
            {
                object writeValue;
                if (!string.IsNullOrWhiteSpace(plcReadWrite.WriteConst))
                {
                    writeValue = plcReadWrite.WriteConst;
                }
                else
                {
                    if (valueStore == null)
                    {
                        MarkAlarm(evt, "变量库未初始化");
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(plcReadWrite.ValueName))
                    {
                        MarkAlarm(evt, "变量名称为空");
                        return false;
                    }
                    if (!valueStore.TryGetValueByName(plcReadWrite.ValueName, out DicValue valueItem))
                    {
                        MarkAlarm(evt, $"变量不存在:{plcReadWrite.ValueName}");
                        return false;
                    }
                    writeValue = valueItem.Value;
                }

                if (!Context.PlcStore.TryWriteValue(plcReadWrite.PlcName, plcReadWrite.DataType, plcReadWrite.PlcAddress, plcReadWrite.Quantity, writeValue, out string writeError))
                {
                    MarkAlarm(evt, $"PLC写入失败:{writeError}");
                    return false;
                }
            }

            if (doRead)
            {
                if (valueStore == null)
                {
                    MarkAlarm(evt, "变量库未初始化");
                    return false;
                }
                if (string.IsNullOrWhiteSpace(plcReadWrite.ValueName))
                {
                    MarkAlarm(evt, "变量名称为空");
                    return false;
                }
                if (!valueStore.TryGetValueByName(plcReadWrite.ValueName, out _))
                {
                    MarkAlarm(evt, $"变量不存在:{plcReadWrite.ValueName}");
                    return false;
                }

                if (!Context.PlcStore.TryReadValue(plcReadWrite.PlcName, plcReadWrite.DataType, plcReadWrite.PlcAddress, plcReadWrite.Quantity, out object readValue, out string readError))
                {
                    MarkAlarm(evt, $"PLC读取失败:{readError}");
                    return false;
                }
                if (!valueStore.setValueByName(plcReadWrite.ValueName, readValue, "PLC读写"))
                {
                    MarkAlarm(evt, $"变量写入失败:{plcReadWrite.ValueName}");
                    return false;
                }
            }

            return true;
        }

        private static bool TryParsePlcOps(string text, out bool doRead, out bool doWrite, out string error)
        {
            doRead = false;
            doWrite = false;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "PLC读写类型为空";
                return false;
            }
            switch (text.Trim())
            {
                case "读PLC":
                    doRead = true;
                    return true;
                case "写PLC":
                    doWrite = true;
                    return true;
                case "读写":
                    doRead = true;
                    doWrite = true;
                    return true;
                default:
                    error = $"PLC读写类型无效:{text}";
                    return false;
            }
        }
    }
}
