using System;
// 模块：引擎 / 执行。
// 职责范围：负责运行绑定、调度、状态管理以及各类流程指令的确定性执行。

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
            if (string.IsNullOrWhiteSpace(operation.DeviceName)) return FailPlc(evt, "PLC设备名称为空。");
            if (operation.ModelVersion != PlcReadWrite.CurrentModelVersion)
                return FailPlc(evt, "PLC读写模型版本无效。");
            if (!Enum.IsDefined(typeof(PlcAccessAction), operation.Action)
                || !Enum.IsDefined(typeof(PlcAccessMode), operation.Mode))
                return FailPlc(evt, "PLC读写枚举参数无效。");

            if (operation.Action == PlcAccessAction.Read)
            {
                if (operation.Mode == PlcAccessMode.Items)
                    return RunPlcDiscreteRead(evt, operation);
                PlcReadBatch batch = operation.ReadBatch;
                if (batch == null) return FailPlc(evt, "PLC连续读取参数为空。");
                if (!TryBuildRequest(batch.Area, batch.StartAddress, batch.DataType,
                    batch.ElementCount, batch.StringByteLength, false, "流程连续读取",
                    out PlcMapConfig request, out string error)) return FailPlc(evt, error);
                if (!ResolveConsecutiveVariables(batch.FirstVariableName, batch.ElementCount,
                    batch.DataType, evt.procId, out List<string> variableNames, out error)) return FailPlc(evt, error);
                request.VariableNames = variableNames.ToList();
                if (!Context.PlcRuntime.TryRead(operation.DeviceName, request, out object[] values, out error))
                    return FailPlc(evt, $"PLC读取失败:{error}", true);
                if (!SetReadVariables(batch.DataType, variableNames, values, evt.procId,
                    evt.GetOperationSource(), out error, out bool responseInvalid))
                    return FailPlc(evt, error, responseInvalid);
                return true;
            }

            if (operation.Mode == PlcAccessMode.Items)
                return RunPlcDiscreteWrite(evt, operation);

            PlcWriteBatch writeBatch = operation.WriteBatch;
            if (writeBatch == null) return FailPlc(evt, "PLC连续写入参数为空。");
            if (!TryBuildRequest(writeBatch.Area, writeBatch.StartAddress, writeBatch.DataType,
                writeBatch.ElementCount, writeBatch.StringByteLength, true, "流程连续写入",
                out PlcMapConfig writeRequest, out string writeError)) return FailPlc(evt, writeError);
            if (!TryGetBatchWriteValues(writeBatch, evt.procId, out object[] writeValues, out writeError))
                return FailPlc(evt, writeError);
            if (!Context.PlcRuntime.TryWrite(operation.DeviceName, writeRequest, writeValues, out writeError))
                return FailPlc(evt, $"PLC写入失败:{writeError}", true);
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

        private bool RunPlcDiscreteRead(ProcHandle evt, PlcReadWrite operation)
        {
            if (operation.ReadItems == null
                || operation.ReadItems.Count < 1
                || operation.ReadItems.Count > 100)
                return FailPlc(evt, "PLC按项读取项数量必须为1..100。");

            var requests = new List<PlcMapConfig>(operation.ReadItems.Count);
            var variableNames = new List<string>(operation.ReadItems.Count);
            var uniqueVariables = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < operation.ReadItems.Count; i++)
            {
                PlcReadItem item = operation.ReadItems[i];
                if (item == null) return FailPlc(evt, $"PLC读取项{i + 1}为空。");
                if (!TryBuildRequest(item.Area, item.StartAddress, item.DataType, 1,
                    item.StringByteLength, false, $"流程按项读取{i + 1}", out PlcMapConfig request,
                    out string error)) return FailPlc(evt, $"PLC读取项{i + 1}:{error}");
                request.Id = $"direct-item-{i}";
                if (!ValidateVariable(item.VariableName, item.DataType, uniqueVariables, evt.procId, out error))
                    return FailPlc(evt, $"PLC读取项{i + 1}:{error}");
                request.VariableNames = new List<string> { item.VariableName };
                requests.Add(request);
                variableNames.Add(item.VariableName);
            }

            if (!Context.PlcRuntime.TryReadBatch(operation.DeviceName, requests,
                out IReadOnlyDictionary<string, object[]> results, out string batchError))
                return FailPlc(evt, $"PLC按项读取失败:{batchError}", true);

            for (int i = 0; i < requests.Count; i++)
            {
                if (!results.TryGetValue(requests[i].Id, out object[] values) || values == null || values.Length != 1)
                    return FailPlc(evt, $"PLC读取项{i + 1}返回数量异常。", true);
                if (!SetReadVariables(operation.ReadItems[i].DataType,
                    new[] { variableNames[i] }, values, evt.procId,
                    evt.GetOperationSource(), out string error, out bool responseInvalid))
                    return FailPlc(evt, error, responseInvalid);
            }
            return true;
        }

        private bool RunPlcDiscreteWrite(ProcHandle evt, PlcReadWrite operation)
        {
            if (operation.WriteItems == null
                || operation.WriteItems.Count < 1
                || operation.WriteItems.Count > 100)
                return FailPlc(evt, "PLC按项写入项数量必须为1..100。");

            for (int index = 0; index < operation.WriteItems.Count; index++)
            {
                PlcWriteItem item = operation.WriteItems[index];
                if (item == null) return FailPlc(evt, $"PLC写入项{index + 1}为空。");
                if (!TryBuildRequest(item.Area, item.StartAddress, item.DataType, 1,
                    item.StringByteLength, true, $"流程按项写入{index + 1}",
                    out PlcMapConfig request, out string error))
                    return FailPlc(evt, $"PLC写入项{index + 1}:{error}");
                if (!TryGetWriteItemValue(item, evt.procId, out object value, out error))
                    return FailPlc(evt, $"PLC写入项{index + 1}:{error}");
                if (!Context.PlcRuntime.TryWrite(operation.DeviceName, request,
                    new[] { value }, out error))
                    return FailPlc(evt, $"PLC写入项{index + 1}失败:{error}", true);
            }
            return true;
        }

        private static bool TryBuildRequest(PlcArea area, int startAddress, PlcDataType dataType,
            int elementCount, int stringByteLength, bool isWrite, string name,
            out PlcMapConfig request, out string error)
        {
            request = null;
            if (!PlcDirectOperationValidator.TryValidateAddress(
                area, startAddress, dataType, elementCount, stringByteLength, isWrite, out error))
                return false;

            request = new PlcMapConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Area = area,
                StartAddress = startAddress,
                DataType = dataType,
                Direction = isWrite ? PlcMapDirection.WriteToPlc : PlcMapDirection.ReadFromPlc,
                ElementCount = elementCount,
                StringByteLength = stringByteLength,
                VariableNames = new List<string>()
            };
            return true;
        }

        private bool ResolveConsecutiveVariables(string firstVariableName, int count, PlcDataType dataType,
            Guid procId, out List<string> variableNames, out string error)
        {
            variableNames = new List<string>();
            error = null;
            if (Context.ValueStore == null) { error = "平台变量库未初始化。"; return false; }
            if (!Context.ValueStore.TryGetValueByNameForProcess(firstVariableName, procId, out DicValue first))
            { error = $"首保存变量不存在:{firstVariableName}"; return false; }
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                if (!Context.ValueStore.TryGetValueByIndexForProcess(first.Index + i, procId, out DicValue value))
                { error = $"从变量[{firstVariableName}]开始的第{i + 1}个连续变量不存在。"; return false; }
                if (!ValidateVariable(value.Name, dataType, unique, procId, out error)) return false;
                variableNames.Add(value.Name);
            }
            return true;
        }

        private bool ValidateVariable(string name, PlcDataType dataType, HashSet<string> unique, Guid procId, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name) || !unique.Add(name))
            { error = "PLC保存变量为空或重复。"; return false; }
            if (Context.ValueStore == null || !Context.ValueStore.TryGetValueByNameForProcess(name, procId, out DicValue value))
            { error = $"PLC变量不存在:{name}"; return false; }
            string expectedType = dataType == PlcDataType.String ? "string" : "double";
            if (!string.Equals(value.Type, expectedType, StringComparison.OrdinalIgnoreCase))
            { error = $"PLC变量[{name}]必须是{expectedType}类型。"; return false; }
            return true;
        }

        private bool TryGetWriteItemValue(PlcWriteItem item, Guid procId, out object value, out string error)
        {
            error = null;
            value = null;
            if (!Enum.IsDefined(typeof(PlcValueSource), item.Source))
            {
                error = "PLC写入数据来源无效。";
                return false;
            }
            if (item.Source == PlcValueSource.Constant)
            {
                if (item.ConstantValue == null) { error = "PLC写入固定值为空。"; return false; }
                value = item.ConstantValue;
                return HslModbusAdapter.TryNormalizeValues(
                    item.DataType, new[] { value }, out _, out error);
            }

            if (Context.ValueStore == null
                || !Context.ValueStore.TryGetValueByNameForProcess(item.VariableName, procId, out DicValue variable))
            {
                error = $"PLC来源变量不存在:{item.VariableName ?? string.Empty}";
                return false;
            }
            value = variable.Value ?? string.Empty;
            return HslModbusAdapter.TryNormalizeValues(
                item.DataType, new[] { value }, out _, out error);
        }

        private bool TryGetBatchWriteValues(PlcWriteBatch batch, Guid procId, out object[] values, out string error)
        {
            error = null;
            values = null;
            if (!Enum.IsDefined(typeof(PlcValueSource), batch.Source))
            {
                error = "PLC连续写入数据来源无效。";
                return false;
            }
            if (batch.Source == PlcValueSource.Constant)
            {
                if (batch.ConstantValue == null) { error = "PLC连续写入固定值为空。"; return false; }
                values = Enumerable.Repeat<object>(batch.ConstantValue, batch.ElementCount).ToArray();
                return HslModbusAdapter.TryNormalizeValues(batch.DataType, values, out _, out error);
            }

            if (!ResolveConsecutiveVariables(batch.FirstVariableName, batch.ElementCount,
                batch.DataType, procId, out List<string> variableNames, out error)) return false;
            values = new object[variableNames.Count];
            for (int index = 0; index < variableNames.Count; index++)
            {
                DicValue variable = Context.ValueStore.GetValueByNameForProcess(variableNames[index], procId);
                values[index] = variable.Value ?? string.Empty;
            }
            return HslModbusAdapter.TryNormalizeValues(batch.DataType, values, out _, out error);
        }

        private bool SetReadVariables(PlcDataType dataType, IReadOnlyList<string> variableNames,
            IReadOnlyList<object> values, Guid procId, string source, out string error,
            out bool responseInvalid)
        {
            error = null;
            responseInvalid = false;
            if (values == null || values.Count != variableNames.Count)
            {
                error = "PLC返回数量与变量数量不一致。";
                responseInvalid = true;
                return false;
            }
            var normalizedValues = new List<object>(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                try
                {
                    normalizedValues.Add(dataType == PlcDataType.String
                        ? (object)(values[i]?.ToString() ?? string.Empty)
                        : dataType == PlcDataType.Boolean
                            ? ((bool)values[i] ? 1d : 0d)
                            : Convert.ToDouble(values[i], CultureInfo.InvariantCulture));
                }
                catch (Exception ex) when (ex is FormatException
                    || ex is InvalidCastException || ex is OverflowException)
                {
                    error = $"PLC返回值{i + 1}无法转换为{dataType}:{ex.Message}";
                    responseInvalid = true;
                    return false;
                }
            }
            var previousValues = new List<KeyValuePair<DicValue, string>>(values.Count);
            for (int i = 0; i < normalizedValues.Count; i++)
            {
                DicValue target = Context.ValueStore.GetValueByNameForProcess(variableNames[i], procId);
                previousValues.Add(new KeyValuePair<DicValue, string>(target, target.Value));
                if (Context.ValueStore.SetValueByNameForProcess(
                    variableNames[i], normalizedValues[i], procId, source)) continue;
                foreach (KeyValuePair<DicValue, string> previous in previousValues)
                {
                    Context.ValueStore.SetValueByIndexForProcess(
                        previous.Key.Index, previous.Value, procId, source + ":PLC读取回滚");
                }
                error = $"PLC读取结果写入变量失败:{variableNames[i]}";
                return false;
            }
            return true;
        }

        private bool FailPlc(ProcHandle evt, string message, bool retryable = false)
        {
            if (retryable)
            {
                throw CreateRetryableCommunicationException(evt, message);
            }
            MarkAlarm(evt, message);
            return false;
        }
    }
}
