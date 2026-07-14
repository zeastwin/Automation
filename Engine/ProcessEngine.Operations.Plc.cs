using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Automation
{
    public partial class ProcessEngine
    {
        public bool RunPlcReadWrite(ProcHandle evt, PlcReadWrite operation)
        {
            if (operation == null) return FailPlc(evt, "PLC读写参数为空。");
            if (Context?.PlcRuntime == null) return FailPlc(evt, "PLC运行时未初始化。");
            if (!TryBuildDirectRequest(operation, out PlcMapConfig request, out string error))
                return FailPlc(evt, error);

            if (operation.Action == PlcAccessAction.Read)
            {
                if (!Context.PlcRuntime.TryRead(operation.DeviceName, request, out object[] values, out error))
                    return FailPlc(evt, $"PLC读取失败:{error}");
                if (!SetReadVariables(operation, values, out error)) return FailPlc(evt, error);
                return true;
            }

            if (!TryGetWriteValues(operation, out object[] writeValues, out error)) return FailPlc(evt, error);
            if (!Context.PlcRuntime.TryWrite(operation.DeviceName, request, writeValues, out error))
                return FailPlc(evt, $"PLC写入失败:{error}");
            return true;
        }

        public bool RunPlcMappingControl(ProcHandle evt, PlcMappingControl operation)
        {
            if (operation == null) return FailPlc(evt, "PLC映射控制参数为空。");
            if (Context?.PlcRuntime == null) return FailPlc(evt, "PLC运行时未初始化。");
            if (string.IsNullOrWhiteSpace(operation.DeviceName)) return FailPlc(evt, "PLC设备名称为空。");
            if (!Enum.IsDefined(typeof(PlcMappingAction), operation.Action)) return FailPlc(evt, "PLC映射控制动作无效。");

            bool success;
            string error;
            switch (operation.Action)
            {
                case PlcMappingAction.Reinitialize:
                    success = Context.PlcRuntime.TryReinitialize(operation.DeviceName, out error);
                    break;
                case PlcMappingAction.Start:
                    success = Context.PlcRuntime.TryStartMapping(operation.DeviceName, out error);
                    break;
                case PlcMappingAction.Stop:
                    success = Context.PlcRuntime.TryStopMapping(operation.DeviceName, out error);
                    break;
                default:
                    return FailPlc(evt, "PLC映射控制动作无效。");
            }
            return success || FailPlc(evt, $"PLC映射控制失败:{error}");
        }

        private bool TryBuildDirectRequest(PlcReadWrite operation, out PlcMapConfig request, out string error)
        {
            request = null;
            error = null;
            if (string.IsNullOrWhiteSpace(operation.DeviceName)) { error = "PLC设备名称为空。"; return false; }
            if (!Enum.IsDefined(typeof(PlcAccessAction), operation.Action)
                || !Enum.IsDefined(typeof(PlcArea), operation.Area)
                || !Enum.IsDefined(typeof(PlcDataType), operation.DataType)
                || !Enum.IsDefined(typeof(PlcWriteSource), operation.WriteSource))
            { error = "PLC读写枚举参数无效。"; return false; }
            if (operation.StartAddress < 0 || operation.StartAddress > 65535) { error = "PLC起始地址超出范围。"; return false; }
            if (operation.ElementCount < 1 || operation.ElementCount > 1000) { error = "PLC元素数量必须为1..1000。"; return false; }
            if ((operation.Area == PlcArea.Coil || operation.Area == PlcArea.DiscreteInput)
                != (operation.DataType == PlcDataType.Boolean))
            { error = "Boolean只允许线圈地址区，寄存器类型只允许寄存器地址区。"; return false; }
            if (operation.Action == PlcAccessAction.Write
                && (operation.Area == PlcArea.DiscreteInput || operation.Area == PlcArea.InputRegister))
            { error = "只读地址区禁止写入。"; return false; }
            if (operation.DataType == PlcDataType.String)
            {
                if (operation.ElementCount != 1 || operation.StringByteLength < 1 || operation.StringByteLength > 2000)
                { error = "String必须为单元素且字符串字节数为1..2000。"; return false; }
            }
            else if (operation.StringByteLength != 0)
            { error = "非String类型的字符串字节数必须为0。"; return false; }

            int requiredVariables = operation.Action == PlcAccessAction.Read
                || operation.WriteSource == PlcWriteSource.Variables ? operation.ElementCount : 0;
            if ((operation.VariableNames?.Count ?? 0) != requiredVariables)
            { error = $"PLC变量数量必须为{requiredVariables}。"; return false; }
            if (operation.Action == PlcAccessAction.Write && operation.WriteSource == PlcWriteSource.Constant
                && operation.ElementCount != 1)
            { error = "固定常量只支持单元素写入。"; return false; }

            request = new PlcMapConfig
            {
                Name = "流程直接读写",
                Area = operation.Area,
                StartAddress = operation.StartAddress,
                DataType = operation.DataType,
                Direction = operation.Action == PlcAccessAction.Read
                    ? PlcMapDirection.ReadFromPlc : PlcMapDirection.WriteToPlc,
                ElementCount = operation.ElementCount,
                StringByteLength = operation.StringByteLength,
                VariableNames = operation.VariableNames?.ToList() ?? new List<string>()
            };
            int span = PlcConfigStore.GetAddressSpan(request);
            if ((long)request.StartAddress + span - 1 > 65535)
            { error = "PLC访问范围超过65535。"; request = null; return false; }
            return ValidateOperationVariables(operation, out error);
        }

        private bool ValidateOperationVariables(PlcReadWrite operation, out string error)
        {
            error = null;
            if (operation.VariableNames == null) return true;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            string expectedType = operation.DataType == PlcDataType.String ? "string" : "double";
            foreach (string name in operation.VariableNames)
            {
                if (string.IsNullOrWhiteSpace(name) || !unique.Add(name))
                { error = "PLC变量名称为空或重复。"; return false; }
                if (Context.ValueStore == null || !Context.ValueStore.TryGetValueByName(name, out DicValue value))
                { error = $"PLC变量不存在:{name}"; return false; }
                if (!string.Equals(value?.Type, expectedType, StringComparison.OrdinalIgnoreCase))
                { error = $"PLC变量[{name}]必须是{expectedType}类型。"; return false; }
            }
            return true;
        }

        private bool TryGetWriteValues(PlcReadWrite operation, out object[] values, out string error)
        {
            error = null;
            if (operation.WriteSource == PlcWriteSource.Constant)
            {
                if (operation.ConstantValue == null) { values = null; error = "PLC写入常量为空。"; return false; }
                values = new object[] { operation.ConstantValue };
            }
            else
            {
                values = new object[operation.VariableNames.Count];
                for (int i = 0; i < operation.VariableNames.Count; i++)
                {
                    DicValue value = Context.ValueStore.GetValueByName(operation.VariableNames[i]);
                    if (operation.DataType == PlcDataType.String) values[i] = value.Value ?? string.Empty;
                    else if (!double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                        || double.IsNaN(number) || double.IsInfinity(number))
                    { error = $"变量[{value.Name}]不是有效有限数。"; return false; }
                    else values[i] = number;
                }
            }
            return HslModbusAdapter.TryNormalizeValues(operation.DataType, values, out _, out error);
        }

        private bool SetReadVariables(PlcReadWrite operation, IReadOnlyList<object> values, out string error)
        {
            error = null;
            if (values == null || values.Count != operation.VariableNames.Count)
            { error = "PLC返回数量与变量数量不一致。"; return false; }
            for (int i = 0; i < values.Count; i++)
            {
                object value = operation.DataType == PlcDataType.String
                    ? (object)(values[i]?.ToString() ?? string.Empty)
                    : operation.DataType == PlcDataType.Boolean
                        ? ((bool)values[i] ? 1d : 0d)
                        : Convert.ToDouble(values[i], CultureInfo.InvariantCulture);
                if (!Context.ValueStore.setValueByName(operation.VariableNames[i], value, "PLC读写"))
                { error = $"PLC读取结果写入变量失败:{operation.VariableNames[i]}"; return false; }
            }
            return true;
        }

        private bool FailPlc(ProcHandle evt, string message)
        {
            MarkAlarm(evt, message);
            return false;
        }
    }
}
