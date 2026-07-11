using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Automation.OperationTypePartial;

namespace Automation
{
    public static class ProcessDefinitionService
    {
        internal const string DeletedGotoPrefix = "#DELETED-GOTO#";

        public static void NormalizeProc(int procIndex, Proc proc, List<string> errors)
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
                    ValidateGotoTargets(step.Ops[j], procIndex, proc, errors, $"流程{procIndex}步骤{i}指令{j}");
                    ValidateCommunicationOperation(step.Ops[j], errors, $"流程{procIndex}步骤{i}指令{j}");
                }
            }
        }

        private static void ValidateCommunicationOperation(OperationType operation, List<string> errors, string location)
        {
            if (operation == null || operation.Disable)
            {
                return;
            }

            bool HasValue(string name) => !string.IsNullOrWhiteSpace(name)
                && SF.valueStore != null && SF.valueStore.TryGetValueByName(name, out _);
            bool HasTcp(string name) => SF.communicationStore != null
                && SF.communicationStore.TryGetSocket(name, out _);
            bool HasSerial(string name) => SF.communicationStore != null
                && SF.communicationStore.TryGetSerial(name, out _);

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
                if (!HasTcp(sendTcp.ID) || !HasValue(sendTcp.Msg) || sendTcp.TimeOut <= 0)
                {
                    errors.Add($"{location} TCP发送配置无效");
                }
                return;
            }
            if (operation is ReceoveTcpMsg receiveTcp)
            {
                if (!HasTcp(receiveTcp.ID) || !HasValue(receiveTcp.MsgSaveValue) || receiveTcp.TImeOut <= 0)
                {
                    errors.Add($"{location} TCP接收配置无效");
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
                    ValidateGotoTargets(op, procIndex, proc, errors, $"流程{procIndex}步骤{i}指令{j}");
                }
            }
            return errors;
        }

        public static bool TryValidateOperationGoto(OperationType operation, int procIndex, Proc proc, out string error)
        {
            var errors = new List<string>();
            if (operation != null)
            {
                ValidateGotoTargets(operation, procIndex, proc, errors, "指令");
            }
            error = errors.FirstOrDefault();
            return error == null;
        }

        private static void ValidateGotoTargets(object obj, int procIndex, Proc proc, List<string> errors, string context)
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
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        if (value.StartsWith(DeletedGotoPrefix, StringComparison.Ordinal))
                        {
                            errors.Add($"{context}跳转目标已被删除，必须明确指定新的目标：{value.Substring(DeletedGotoPrefix.Length)}");
                        }
                        else if (!TryParseGotoKey(value, out int gotoProc, out int gotoStep, out int gotoOp))
                        {
                            errors.Add($"{context}跳转地址格式错误：{value}");
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
                        ValidateGotoTargets(item, procIndex, proc, errors, context);
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
