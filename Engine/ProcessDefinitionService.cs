using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Automation.OperationTypePartial;

namespace Automation
{
    public sealed class ProcessDefinitionValidationContext
    {
        public ProcessDefinitionValidationContext(
            IEnumerable<string> variableNames,
            IEnumerable<string> tcpNames,
            IEnumerable<string> serialNames,
            IEnumerable<string> alarmInfoIds = null,
            IEnumerable<string> plcNames = null,
            IEnumerable<KeyValuePair<string, DicValue>> variableDefinitions = null)
        {
            VariableNames = new HashSet<string>(variableNames ?? Array.Empty<string>(), StringComparer.Ordinal);
            TcpNames = new HashSet<string>(tcpNames ?? Array.Empty<string>(), StringComparer.Ordinal);
            SerialNames = new HashSet<string>(serialNames ?? Array.Empty<string>(), StringComparer.Ordinal);
            AlarmInfoIds = new HashSet<string>(alarmInfoIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            HasAlarmInfoCatalog = alarmInfoIds != null;
            PlcNames = new HashSet<string>(plcNames ?? Array.Empty<string>(), StringComparer.Ordinal);
            HasPlcCatalog = plcNames != null;
            VariableDefinitions = (variableDefinitions ?? Array.Empty<KeyValuePair<string, DicValue>>())
                .Where(item => !string.IsNullOrWhiteSpace(item.Key) && item.Value != null)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        }

        public IReadOnlyCollection<string> VariableNames { get; }

        public IReadOnlyCollection<string> TcpNames { get; }

        public IReadOnlyCollection<string> SerialNames { get; }

        public IReadOnlyCollection<string> AlarmInfoIds { get; }

        public bool HasAlarmInfoCatalog { get; }

        public IReadOnlyCollection<string> PlcNames { get; }

        public bool HasPlcCatalog { get; }

        public IReadOnlyDictionary<string, DicValue> VariableDefinitions { get; }
    }

    public static class ProcessDefinitionService
    {
        internal const string DeletedGotoPrefix = "#DELETED-GOTO#";
        internal const string PendingGotoPrefix = "#PENDING-GOTO#";

        public static string BuildPendingGoto(string operationMapKey)
        {
            return PendingGotoPrefix + Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(operationMapKey ?? string.Empty));
        }

        public static int ResolvePendingGotoTargets(int procIndex, Proc proc)
        {
            if (proc?.steps == null) return 0;
            var locations = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
            {
                Step step = proc.steps[stepIndex];
                if (step?.Ops == null) continue;
                for (int operationIndex = 0; operationIndex < step.Ops.Count; operationIndex++)
                {
                    OperationType operation = step.Ops[operationIndex];
                    if (operation == null || string.IsNullOrWhiteSpace(operation.AiKey)) continue;
                    string address = $"{procIndex}-{stepIndex}-{operationIndex}";
                    if (step.Id != Guid.Empty)
                    {
                        locations[AiOperationCompileContext.BuildOperationKeyForStepId(
                            step.Id, operation.AiKey)] = address;
                    }
                    if (!string.IsNullOrWhiteSpace(step.AiKey))
                    {
                        locations[AiOperationCompileContext.BuildOperationKeyForStepKey(
                            step.AiKey, operation.AiKey)] = address;
                    }
                }
            }

            int resolved = 0;
            foreach (Step step in proc.steps)
            {
                foreach (OperationType operation in step?.Ops ?? Enumerable.Empty<OperationType>())
                {
                    resolved += ResolvePendingGotoTargets(operation, locations);
                }
            }
            return resolved;
        }

        private static int ResolvePendingGotoTargets(
            object obj, IReadOnlyDictionary<string, string> locations)
        {
            if (obj == null) return 0;
            int resolved = 0;
            foreach (PropertyInfo property in obj.GetType().GetProperties())
            {
                if (property.GetIndexParameters().Length > 0) continue;
                object value = property.GetValue(obj);
                if (property.PropertyType == typeof(string)
                    && property.CanWrite
                    && property.GetCustomAttribute<MarkedGotoAttribute>() != null
                    && value is string text
                    && TryReadPendingGoto(text, out string mapKey)
                    && locations.TryGetValue(mapKey, out string address))
                {
                    property.SetValue(obj, address);
                    resolved++;
                    continue;
                }
                if (value is System.Collections.IEnumerable enumerable && !(value is string))
                {
                    foreach (object item in enumerable)
                    {
                        resolved += ResolvePendingGotoTargets(item, locations);
                    }
                }
            }
            return resolved;
        }

        internal static bool TryReadPendingGoto(string value, out string operationMapKey)
        {
            operationMapKey = null;
            if (string.IsNullOrWhiteSpace(value)
                || !value.StartsWith(PendingGotoPrefix, StringComparison.Ordinal))
            {
                return false;
            }
            try
            {
                operationMapKey = System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(value.Substring(PendingGotoPrefix.Length)));
                return !string.IsNullOrWhiteSpace(operationMapKey);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static void NormalizeProc(int procIndex, Proc proc, List<string> errors,
            ProcessDefinitionValidationContext validationContext = null)
        {
            if (proc == null)
            {
                errors.Add($"流程{procIndex}为空");
                return;
            }
            if (proc.head == null)
            {
                proc.head = new ProcHead();
                errors.Add($"流程{procIndex}头信息缺失");
            }
            if (proc.head.Id == Guid.Empty)
            {
                errors.Add($"流程{procIndex}缺少稳定ID");
            }
            if (string.IsNullOrWhiteSpace(proc.head.Name))
            {
                errors.Add($"流程{procIndex}名称为空");
            }
            if (proc.head.PauseIoParams == null)
            {
                proc.head.PauseIoParams = new CustomList<PauseIoParam>();
            }
            if (proc.head.PauseValueParams == null)
            {
                proc.head.PauseValueParams = new CustomList<PauseValueParam>();
            }
            if (proc.steps == null)
            {
                proc.steps = new List<Step>();
                errors.Add($"流程{procIndex}步骤列表缺失");
            }
            var stepIds = new HashSet<Guid>();
            var operationIds = new HashSet<Guid>();
            for (int i = 0; i < proc.steps.Count; i++)
            {
                if (proc.steps[i] == null)
                {
                    proc.steps[i] = new Step();
                    errors.Add($"流程{procIndex}步骤{i}为空");
                }
                Step step = proc.steps[i];
                if (step.Id == Guid.Empty)
                {
                    errors.Add($"流程{procIndex}步骤{i}缺少稳定ID");
                }
                else if (!stepIds.Add(step.Id))
                {
                    errors.Add($"流程{procIndex}步骤{i}的ID重复：{step.Id:D}");
                }
                if (string.IsNullOrWhiteSpace(step.Name))
                {
                    errors.Add($"流程{procIndex}步骤{i}名称为空");
                }
                if (step.Ops == null)
                {
                    step.Ops = new List<OperationType>();
                    errors.Add($"流程{procIndex}步骤{i}指令列表缺失");
                }
                for (int j = 0; j < step.Ops.Count; j++)
                {
                    if (step.Ops[j] == null)
                    {
                        step.Ops[j] = new OperationType
                        {
                            Name = "空指令",
                            OperaType = "无效指令",
                            Disable = true
                        };
                        errors.Add($"流程{procIndex}步骤{i}指令{j}为空");
                    }
                    if (step.Ops[j].Id == Guid.Empty)
                    {
                        errors.Add($"流程{procIndex}步骤{i}指令{j}缺少稳定ID");
                    }
                    else if (!operationIds.Add(step.Ops[j].Id))
                    {
                        errors.Add($"流程{procIndex}步骤{i}指令{j}的ID重复：{step.Ops[j].Id:D}");
                    }
                    step.Ops[j].Num = j;
                }
            }
            for (int i = 0; i < proc.steps.Count; i++)
            {
                Step step = proc.steps[i];
                for (int j = 0; j < step.Ops.Count; j++)
                {
                    ValidateGotoTargets(step.Ops[j], step.Ops[j], procIndex, proc, errors, $"流程{procIndex}步骤{i}指令{j}");
                }
            }
        }

        public static IReadOnlyList<string> ValidateOperationRuntimeConfiguration(
            OperationType operation, string location,
            ProcessDefinitionValidationContext validationContext = null)
        {
            var errors = new List<string>();
            ValidateCommunicationOperation(operation, errors, location, validationContext);
            ValidatePlcOperation(operation, errors, location, validationContext);
            return errors;
        }

        private static void ValidatePlcOperation(OperationType operation, List<string> errors,
            string location, ProcessDefinitionValidationContext validationContext)
        {
            if (operation == null || operation.Disable) return;

            bool HasPlc(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return false;
                if (validationContext?.HasPlcCatalog == true)
                    return validationContext.PlcNames.Contains(name);
                return (SF.plcStore?.GetSnapshot().Devices ?? new List<PlcDeviceConfig>())
                    .Any(item => item != null && string.Equals(item.Name, name, StringComparison.Ordinal));
            }
            bool TryGetVariable(string name, out DicValue value)
            {
                value = null;
                if (string.IsNullOrWhiteSpace(name)) return false;
                if (validationContext != null && validationContext.VariableDefinitions.Count > 0)
                    return validationContext.VariableDefinitions.TryGetValue(name, out value);
                return SF.valueStore != null && SF.valueStore.TryGetValueByName(name, out value);
            }
            bool ValidateVariable(string name, PlcDataType dataType, HashSet<string> unique, string field)
            {
                if (string.IsNullOrWhiteSpace(name) || !unique.Add(name))
                {
                    errors.Add($"{location} 的 {field} 为空或重复。");
                    return false;
                }
                if (!TryGetVariable(name, out DicValue value))
                {
                    errors.Add($"{location} 的 {field} 引用的变量不存在：{name}。");
                    return false;
                }
                string expected = dataType == PlcDataType.String ? "string" : "double";
                if (!string.Equals(value.Type, expected, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{location} 的变量[{name}]必须是{expected}类型。");
                    return false;
                }
                return true;
            }

            string deviceName;
            if (operation is PlcMappingControl mapping)
            {
                deviceName = mapping.DeviceName;
                if (!Enum.IsDefined(typeof(PlcMappingAction), mapping.Action))
                    errors.Add($"{location} 的PLC映射控制动作无效。");
                if (!string.IsNullOrWhiteSpace(deviceName) && !HasPlc(deviceName))
                    errors.Add($"{location} 引用的PLC设备尚未配置：{deviceName ?? string.Empty}。");
                return;
            }
            if (!(operation is PlcReadWrite readWrite)) return;

            deviceName = readWrite.DeviceName;
            if (!string.IsNullOrWhiteSpace(deviceName) && !HasPlc(deviceName))
                errors.Add($"{location} 引用的PLC设备尚未配置：{deviceName ?? string.Empty}。");
            if (readWrite.ModelVersion != PlcReadWrite.CurrentModelVersion)
            {
                errors.Add($"{location} 的PLC读写模型版本无效，必须为{PlcReadWrite.CurrentModelVersion}。");
                return;
            }
            if (!Enum.IsDefined(typeof(PlcAccessAction), readWrite.Action)
                || !Enum.IsDefined(typeof(PlcAccessMode), readWrite.Mode))
            {
                errors.Add($"{location} 的PLC读写枚举参数无效。");
                return;
            }

            if (readWrite.Action == PlcAccessAction.Read && readWrite.Mode == PlcAccessMode.Items)
            {
                if (readWrite.ReadItemCount < 1 || readWrite.ReadItemCount > 100
                    || readWrite.ReadItems == null || readWrite.ReadItems.Count != readWrite.ReadItemCount)
                {
                    errors.Add($"{location} 的PLC按项读取数量必须为1..100，并与读取项数量一致。");
                    return;
                }
                var unique = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < readWrite.ReadItems.Count; index++)
                {
                    PlcReadItem item = readWrite.ReadItems[index];
                    if (item == null)
                    {
                        errors.Add($"{location} 的PLC读取项{index + 1}为空。");
                        continue;
                    }
                    if (!PlcDirectOperationValidator.TryValidateAddress(
                        item.Area, item.StartAddress, item.DataType, 1,
                        item.StringByteLength, false, out string itemError))
                    {
                        errors.Add($"{location} 的PLC读取项{index + 1}：{itemError}");
                    }
                    ValidateVariable(item.VariableName, item.DataType, unique, $"ReadItems[{index}].VariableName");
                }
                return;
            }

            if (readWrite.Action == PlcAccessAction.Write && readWrite.Mode == PlcAccessMode.Items)
            {
                if (readWrite.WriteItemCount < 1 || readWrite.WriteItemCount > 100
                    || readWrite.WriteItems == null || readWrite.WriteItems.Count != readWrite.WriteItemCount)
                {
                    errors.Add($"{location} 的PLC按项写入数量必须为1..100，并与写入项数量一致。");
                    return;
                }
                for (int index = 0; index < readWrite.WriteItems.Count; index++)
                {
                    PlcWriteItem item = readWrite.WriteItems[index];
                    if (item == null)
                    {
                        errors.Add($"{location} 的PLC写入项{index + 1}为空。");
                        continue;
                    }
                    if (!Enum.IsDefined(typeof(PlcValueSource), item.Source))
                    {
                        errors.Add($"{location} 的PLC写入项{index + 1}数据来源无效。");
                        continue;
                    }
                    if (!PlcDirectOperationValidator.TryValidateAddress(
                        item.Area, item.StartAddress, item.DataType, 1,
                        item.StringByteLength, true, out string itemError))
                    {
                        errors.Add($"{location} 的PLC写入项{index + 1}：{itemError}");
                    }
                    if (item.Source == PlcValueSource.Variable)
                    {
                        ValidateVariable(item.VariableName, item.DataType,
                            new HashSet<string>(StringComparer.Ordinal), $"WriteItems[{index}].VariableName");
                    }
                    else
                    {
                        string constantError = null;
                        if (item.ConstantValue == null
                            || !HslModbusAdapter.TryNormalizeValues(item.DataType,
                                new object[] { item.ConstantValue }, out _, out constantError))
                        {
                            errors.Add($"{location} 的PLC写入项{index + 1}固定值无效：{constantError ?? "固定值为空。"}");
                        }
                    }
                }
                return;
            }

            if (readWrite.Action == PlcAccessAction.Read)
            {
                PlcReadBatch batch = readWrite.ReadBatch;
                if (batch == null)
                {
                    errors.Add($"{location} 尚未配置连续批量读取参数。");
                    return;
                }
                if (!PlcDirectOperationValidator.TryValidateAddress(
                    batch.Area, batch.StartAddress, batch.DataType, batch.ElementCount,
                    batch.StringByteLength, false, out string addressError))
                {
                    errors.Add($"{location} 的PLC连续读取参数无效：{addressError}");
                    return;
                }
                ValidateConsecutiveVariables(batch.FirstVariableName, batch.ElementCount,
                    batch.DataType, "首保存变量");
                return;
            }

            PlcWriteBatch writeBatch = readWrite.WriteBatch;
            if (writeBatch == null)
            {
                errors.Add($"{location} 尚未配置连续批量写入参数。");
                return;
            }
            if (!Enum.IsDefined(typeof(PlcValueSource), writeBatch.Source))
            {
                errors.Add($"{location} 的PLC连续写入数据来源无效。");
                return;
            }
            if (!PlcDirectOperationValidator.TryValidateAddress(
                writeBatch.Area, writeBatch.StartAddress, writeBatch.DataType,
                writeBatch.ElementCount, writeBatch.StringByteLength, true, out string writeAddressError))
            {
                errors.Add($"{location} 的PLC连续写入参数无效：{writeAddressError}");
                return;
            }
            if (writeBatch.Source == PlcValueSource.Variable)
            {
                ValidateConsecutiveVariables(writeBatch.FirstVariableName, writeBatch.ElementCount,
                    writeBatch.DataType, "首来源变量");
            }
            else
            {
                string batchConstantError = null;
                if (writeBatch.ConstantValue == null
                    || !HslModbusAdapter.TryNormalizeValues(writeBatch.DataType,
                        Enumerable.Repeat<object>(writeBatch.ConstantValue, writeBatch.ElementCount).ToArray(),
                        out _, out batchConstantError))
                {
                    errors.Add($"{location} 的PLC连续写入固定值无效：{batchConstantError ?? "固定值为空。"}");
                }
            }

            void ValidateConsecutiveVariables(
                string firstVariableName, int count, PlcDataType dataType, string fieldName)
            {
                if (!TryGetVariable(firstVariableName, out DicValue firstVariable))
                {
                    errors.Add($"{location} 的PLC{fieldName}不存在：{firstVariableName ?? string.Empty}。");
                    return;
                }
                var continuousVariables = new HashSet<string>(StringComparer.Ordinal);
                for (int offset = 0; offset < count; offset++)
                {
                    DicValue variable = null;
                    if (validationContext != null && validationContext.VariableDefinitions.Count > 0)
                    {
                        variable = validationContext.VariableDefinitions.Values.FirstOrDefault(
                            item => item != null && item.Index == firstVariable.Index + offset);
                    }
                    else
                    {
                        SF.valueStore?.TryGetValueByIndex(firstVariable.Index + offset, out variable);
                    }
                    if (variable == null)
                    {
                        errors.Add($"{location} 从变量[{firstVariableName}]开始的第{offset + 1}个连续变量不存在。");
                        continue;
                    }
                    ValidateVariable(variable.Name, dataType, continuousVariables, $"连续变量[{offset}]");
                }
            }
        }

        private static void ValidateCommunicationOperation(OperationType operation, List<string> errors,
            string location, ProcessDefinitionValidationContext validationContext)
        {
            if (operation == null || operation.Disable)
            {
                return;
            }

            bool HasValue(string name) => !string.IsNullOrWhiteSpace(name)
                && (validationContext != null
                    ? validationContext.VariableNames.Contains(name)
                    : SF.valueStore != null && SF.valueStore.TryGetValueByName(name, out _));
            bool HasTcp(string name) => !string.IsNullOrWhiteSpace(name)
                && (validationContext != null
                    ? validationContext.TcpNames.Contains(name)
                    : SF.communicationStore != null && SF.communicationStore.TryGetSocket(name, out _));
            bool HasSerial(string name) => !string.IsNullOrWhiteSpace(name)
                && (validationContext != null
                    ? validationContext.SerialNames.Contains(name)
                    : SF.communicationStore != null && SF.communicationStore.TryGetSerial(name, out _));

            if (operation is TcpOps tcpOps)
            {
                if (tcpOps.Params == null || tcpOps.Params.Count == 0)
                {
                    errors.Add($"{location} TCP操作参数为空");
                    return;
                }
                foreach (TcpOpsParam item in tcpOps.Params)
                {
                    if (item == null || !HasTcp(item.Name)
                        || (item.Ops != "启动" && item.Ops != "断开"))
                    {
                        errors.Add($"{location} TCP操作配置无效");
                    }
                }
                return;
            }
            if (operation is WaitTcp waitTcp)
            {
                if (waitTcp.Params == null || waitTcp.Params.Count == 0
                    || waitTcp.Params.Any(item => item == null || !HasTcp(item.Name) || item.TimeOut <= 0))
                {
                    errors.Add($"{location} 等待TCP配置无效");
                }
                return;
            }
            if (operation is SendTcpMsg sendTcp)
            {
                if (!HasTcp(sendTcp.ID))
                {
                    errors.Add($"{location} TCP对象不存在：{sendTcp.ID ?? string.Empty}");
                }
                if (!HasValue(sendTcp.Msg))
                {
                    errors.Add($"{location} TCP发送变量不存在：{sendTcp.Msg ?? string.Empty}");
                }
                if (sendTcp.TimeOut <= 0)
                {
                    errors.Add($"{location} TCP发送超时必须大于0：{sendTcp.TimeOut}");
                }
                return;
            }
            if (operation is ReceoveTcpMsg receiveTcp)
            {
                if (!HasTcp(receiveTcp.ID))
                {
                    errors.Add($"{location} TCP对象不存在：{receiveTcp.ID ?? string.Empty}");
                }
                if (!HasValue(receiveTcp.MsgSaveValue))
                {
                    errors.Add($"{location} TCP接收保存变量不存在：{receiveTcp.MsgSaveValue ?? string.Empty}");
                }
                if (receiveTcp.TImeOut <= 0)
                {
                    errors.Add($"{location} TCP接收超时必须大于0：{receiveTcp.TImeOut}");
                }
                return;
            }
            if (operation is SerialPortOps serialOps)
            {
                if (serialOps.Params == null || serialOps.Params.Count == 0
                    || serialOps.Params.Any(item => item == null || !HasSerial(item.Name)
                        || (item.Ops != "启动" && item.Ops != "断开")))
                {
                    errors.Add($"{location} 串口操作配置无效");
                }
                return;
            }
            if (operation is WaitSerialPort waitSerial)
            {
                if (waitSerial.Params == null || waitSerial.Params.Count == 0
                    || waitSerial.Params.Any(item => item == null || !HasSerial(item.Name) || item.TimeOut <= 0))
                {
                    errors.Add($"{location} 等待串口配置无效");
                }
                return;
            }
            if (operation is SendSerialPortMsg sendSerial)
            {
                if (!HasSerial(sendSerial.ID) || !HasValue(sendSerial.Msg) || sendSerial.TimeOut <= 0)
                {
                    errors.Add($"{location} 串口发送配置无效");
                }
                return;
            }
            if (operation is ReceoveSerialPortMsg receiveSerial)
            {
                if (!HasSerial(receiveSerial.ID) || !HasValue(receiveSerial.MsgSaveValue) || receiveSerial.TImeOut <= 0)
                {
                    errors.Add($"{location} 串口接收配置无效");
                }
                return;
            }
            if (operation is SendReceoveCommMsg request)
            {
                bool validChannel = request.CommType == "TCP"
                    ? HasTcp(request.ID)
                    : request.CommType == "串口" && HasSerial(request.ID);
                if (!validChannel || !HasValue(request.SendMsg) || request.TimeOut <= 0
                    || (!string.IsNullOrWhiteSpace(request.ReceiveSaveValue) && !HasValue(request.ReceiveSaveValue)))
                {
                    errors.Add($"{location} 通讯请求响应配置无效");
                }
            }
        }

        public static Dictionary<int, string> BuildProcFileIndexMap(string path, out int maxIndex)
        {
            Dictionary<int, string> indexMap = new Dictionary<int, string>();
            maxIndex = -1;
            foreach (string file in Directory.EnumerateFiles(path, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!int.TryParse(name, out int index))
                {
                    continue;
                }
                indexMap[index] = file;
                if (index > maxIndex)
                {
                    maxIndex = index;
                }
            }
            return indexMap;
        }

        public static List<string> ValidateProcFileContinuity(Dictionary<int, string> indexMap, int maxIndex)
        {
            List<string> errors = new List<string>();
            if (indexMap == null || indexMap.Count == 0)
            {
                return errors;
            }
            if (!indexMap.ContainsKey(0))
            {
                errors.Add("流程文件索引必须从0开始。");
            }
            for (int i = 0; i <= maxIndex; i++)
            {
                if (!indexMap.ContainsKey(i))
                {
                    errors.Add($"流程文件缺失：{i}.json");
                }
            }
            return errors;
        }

        public static List<string> ValidateProcGotoTargets(int procIndex, Proc proc)
        {
            List<string> errors = new List<string>();
            if (proc?.steps == null)
            {
                return errors;
            }
            for (int i = 0; i < proc.steps.Count; i++)
            {
                Step step = proc.steps[i];
                if (step?.Ops == null)
                {
                    continue;
                }
                for (int j = 0; j < step.Ops.Count; j++)
                {
                    OperationType op = step.Ops[j];
                    if (op == null)
                    {
                        continue;
                    }
                    ValidateGotoTargets(op, op, procIndex, proc, errors, $"流程{procIndex}步骤{i}指令{j}");
                }
            }
            return errors;
        }

        public static bool TryValidateOperationGoto(OperationType operation, int procIndex, Proc proc, out string error)
        {
            var errors = new List<string>();
            if (operation != null)
            {
                ValidateGotoTargets(operation, operation, procIndex, proc, errors, "指令");
            }
            error = errors.FirstOrDefault();
            return error == null;
        }

        private static void ValidateGotoTargets(object obj, OperationType rootOperation, int procIndex, Proc proc, List<string> errors, string context)
        {
            foreach (var propertyInfo in obj.GetType().GetProperties())
            {
                if (propertyInfo.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                if (propertyInfo.PropertyType == typeof(string) && propertyInfo.GetCustomAttribute<MarkedGotoAttribute>() != null)
                {
                    string value = propertyInfo.GetValue(obj) as string;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        // 配置阶段允许先保存不完整跳转；启动闸门会给出明确阻断原因。
                    }
                    else
                    {
                        if (value.StartsWith(DeletedGotoPrefix, StringComparison.Ordinal))
                        {
                            // 删除目标后的跳转保留为草稿，后续阶段可以再指定新目标。
                        }
                        else if (value.StartsWith(PendingGotoPrefix, StringComparison.Ordinal))
                        {
                            // 目标可在后续 ChangeSet 中补齐，当前保留为待解析引用。
                        }
                        else if (!TryParseGotoKey(value, out int gotoProc, out int gotoStep, out int gotoOp))
                        {
                            errors.Add($"{context}跳转地址格式错误：{value}。只能填写三段式数字地址 procIndex-stepIndex-opIndex，例如 0-2-3；界面显示文字不能作为地址。");
                        }
                        else if (gotoProc != procIndex)
                        {
                            errors.Add($"{context}跳转地址跨流程：{value}");
                        }
                        else if (!TryValidateGotoRange(proc, procIndex, gotoStep, gotoOp, out string rangeError))
                        {
                            errors.Add($"{context} {rangeError}");
                        }
                    }
                }

                var propertyValue = propertyInfo.GetValue(obj);
                if (propertyValue is System.Collections.IEnumerable enumerable && !(propertyValue is string))
                {
                    foreach (var item in enumerable)
                    {
                        if (item == null)
                        {
                            continue;
                        }
                        ValidateGotoTargets(item, rootOperation, procIndex, proc, errors, context);
                    }
                }
            }
        }

        private static bool TryValidateGotoRange(Proc proc, int procIndex, int stepIndex, int opIndex, out string error)
        {
            error = null;
            if (proc?.steps == null || stepIndex < 0 || stepIndex >= proc.steps.Count)
            {
                error = $"跳转地址步骤越界：{procIndex}-{stepIndex}-{opIndex}";
                return false;
            }
            Step step = proc.steps[stepIndex];
            if (step?.Ops == null || opIndex < 0 || opIndex >= step.Ops.Count)
            {
                error = $"跳转地址指令越界：{procIndex}-{stepIndex}-{opIndex}";
                return false;
            }
            return true;
        }

        public static bool TryParseGotoKey(string value, out int procIndex, out int stepIndex, out int opIndex)
        {
            procIndex = -1;
            stepIndex = -1;
            opIndex = -1;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            string[] parts = value.Split('-');
            if (parts.Length != 3)
            {
                return false;
            }
            return int.TryParse(parts[0], out procIndex)
                && int.TryParse(parts[1], out stepIndex)
                && int.TryParse(parts[2], out opIndex);
        }


    }
}
