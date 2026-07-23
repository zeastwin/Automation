using System;
// 模块：引擎 / 执行。
// 职责范围：负责运行绑定、调度、状态管理以及各类流程指令的确定性执行。
// 排查入口：运行前引用解析失败从 Binder 定位；不要在指令执行循环里重复搜索名称或静默使用旧索引。

using System.Collections.Generic;

namespace Automation
{
    internal readonly struct RuntimeGotoTarget
    {
        public RuntimeGotoTarget(int procIndex, int stepIndex, int opIndex, string sourceText)
        {
            ProcIndex = procIndex;
            StepIndex = stepIndex;
            OpIndex = opIndex;
            SourceText = sourceText;
        }

        public int ProcIndex { get; }
        public int StepIndex { get; }
        public int OpIndex { get; }
        public string SourceText { get; }

        public bool TryApply(ProcHandle handle, out string error)
        {
            error = null;
            if (handle == null)
            {
                error = "跳转失败：流程句柄为空";
                return false;
            }
            if (ProcIndex != handle.procNum)
            {
                error = $"跳转失败：流程索引不一致 {SourceText}";
                return false;
            }
            Proc proc = handle.Proc;
            if (proc?.steps == null || StepIndex < 0 || StepIndex >= proc.steps.Count)
            {
                error = $"跳转失败：步骤索引超界 {SourceText}";
                return false;
            }
            Step step = proc.steps[StepIndex];
            if (step?.Ops == null || OpIndex < 0 || OpIndex >= step.Ops.Count)
            {
                error = $"跳转失败：操作索引超界 {SourceText}";
                return false;
            }
            handle.stepNum = StepIndex;
            handle.opsNum = OpIndex;
            return true;
        }

        public static bool TryCreate(string text, int expectedProcIndex, Proc proc,
            out RuntimeGotoTarget target, out string error)
        {
            target = default;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "跳转失败：跳转位置为空";
                return false;
            }
            int length = text.Length;
            int cursor = 0;

            bool TryReadNumber(out int value)
            {
                value = 0;
                if (cursor >= length || text[cursor] < '0' || text[cursor] > '9')
                {
                    return false;
                }
                long accumulator = 0;
                while (cursor < length)
                {
                    char current = text[cursor];
                    if (current < '0' || current > '9')
                    {
                        break;
                    }
                    accumulator = accumulator * 10 + current - '0';
                    if (accumulator > int.MaxValue)
                    {
                        return false;
                    }
                    cursor++;
                }
                value = (int)accumulator;
                return true;
            }

            if (!TryReadNumber(out int procIndex) || cursor >= length || text[cursor++] != '-'
                || !TryReadNumber(out int stepIndex) || cursor >= length || text[cursor++] != '-'
                || !TryReadNumber(out int opIndex) || cursor != length)
            {
                error = $"跳转失败：格式错误 {text}";
                return false;
            }
            if (procIndex != expectedProcIndex)
            {
                error = $"跳转失败：流程索引不一致 {text}";
                return false;
            }
            if (proc?.steps == null || stepIndex < 0 || stepIndex >= proc.steps.Count)
            {
                error = $"跳转失败：步骤索引超界 {text}";
                return false;
            }
            Step step = proc.steps[stepIndex];
            if (step?.Ops == null || opIndex < 0 || opIndex >= step.Ops.Count)
            {
                error = $"跳转失败：操作索引超界 {text}";
                return false;
            }
            target = new RuntimeGotoTarget(procIndex, stepIndex, opIndex, text);
            return true;
        }
    }

    internal sealed class GotoRuntimeBinding
    {
        public ValueRef Source { get; set; }
        public GotoCaseRuntimeBinding[] Cases { get; set; } = Array.Empty<GotoCaseRuntimeBinding>();
        public bool HasDefaultTarget { get; set; }
        public RuntimeGotoTarget DefaultTarget { get; set; }
    }

    internal readonly struct GotoCaseRuntimeBinding
    {
        public GotoCaseRuntimeBinding(string literal, ValueRef valueRef, bool usesValueRef,
            RuntimeGotoTarget target)
        {
            Literal = literal;
            ValueRef = valueRef;
            UsesValueRef = usesValueRef;
            Target = target;
        }

        public string Literal { get; }
        public ValueRef ValueRef { get; }
        public bool UsesValueRef { get; }
        public RuntimeGotoTarget Target { get; }
    }

    internal sealed class ParamGotoRuntimeBinding
    {
        public ValueRef[] Conditions { get; set; } = Array.Empty<ValueRef>();
        public RuntimeGotoTarget TrueTarget { get; set; }
        public RuntimeGotoTarget FalseTarget { get; set; }
    }

    internal sealed class BranchRuntimeBinding
    {
        public RuntimeGotoTarget TrueTarget { get; set; }
        public RuntimeGotoTarget FalseTarget { get; set; }
    }

    internal sealed class WaitProcRuntimeBinding
    {
        public RuntimeGotoTarget ReadyTarget { get; set; }
        public RuntimeGotoTarget AbnormalTarget { get; set; }
        public RuntimeGotoTarget RunningTarget { get; set; }
        public ValueRef StateOutput { get; set; }
    }

    internal sealed class GetValueRuntimeBinding
    {
        public ValueRef[] Sources { get; set; } = Array.Empty<ValueRef>();
        public ValueRef[] Destinations { get; set; } = Array.Empty<ValueRef>();
    }

    internal sealed class ModifyValueRuntimeBinding
    {
        public ValueRef Source { get; set; }
        public bool UsesLiteralChangeValue { get; set; }
        public ValueRef ChangeValue { get; set; }
        public ValueRef Output { get; set; }
    }

    internal sealed class CommunicationResponseRuntimeBinding
    {
        public ValueRef[] Sources { get; set; } = Array.Empty<ValueRef>();
    }

    internal static class ProcessRuntimeBinder
    {
        public static bool TryBind(
            Proc proc, int procIndex, ValueConfigStore valueStore, out string error)
        {
            error = null;
            if (proc?.steps == null)
            {
                error = "流程运行计划编译失败：步骤为空";
                return false;
            }
            for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
            {
                Step step = proc.steps[stepIndex];
                if (step?.Ops == null)
                {
                    error = $"流程运行计划编译失败：步骤{stepIndex}指令为空";
                    return false;
                }
                for (int opIndex = 0; opIndex < step.Ops.Count; opIndex++)
                {
                    OperationType operation = step.Ops[opIndex];
                    if (operation == null)
                    {
                        error = $"流程运行计划编译失败：{procIndex}-{stepIndex}-{opIndex}指令为空";
                        return false;
                    }
                    if (!TryBindOperation(
                        proc, procIndex, proc.head.Id, valueStore, operation, out error))
                    {
                        error = $"流程运行计划编译失败：{procIndex}-{stepIndex}-{opIndex} {error}";
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool TryBindOperation(
            Proc proc,
            int procIndex,
            Guid procId,
            ValueConfigStore valueStore,
            OperationType operation,
            out string error)
        {
            error = null;
            switch (operation)
            {
                case WaitProc waitProc:
                    return TryBindWaitProc(proc, procIndex, procId, valueStore, waitProc, out error);
                case Goto gotoOperation:
                    return TryBindGoto(proc, procIndex, procId, valueStore, gotoOperation, out error);
                case ParamGoto paramGoto:
                    return TryBindParamGoto(proc, procIndex, procId, valueStore, paramGoto, out error);
                case IoLogicGoto ioLogicGoto:
                    return TryBindBranch(proc, procIndex, ioLogicGoto, ioLogicGoto.TrueGoto,
                        ioLogicGoto.FalseGoto, out error);
                case GetValue getValue:
                    return TryBindGetValue(procId, valueStore, getValue, out error);
                case ModifyValue modifyValue:
                    return TryBindModifyValue(procId, valueStore, modifyValue, out error);
                case ResponseCommunicationOperationType communication:
                    return TryBindCommunicationResponse(procId, valueStore, communication, out error);
                default:
                    operation.RuntimeBinding = null;
                    return true;
            }
        }

        private static bool TryBindWaitProc(
            Proc proc, int procIndex, Guid procId, ValueConfigStore valueStore,
            WaitProc operation, out string error)
        {
            error = null;
            var binding = new WaitProcRuntimeBinding();
            if (string.Equals(operation.WorkMode, WaitProc.WaitReadyMode, StringComparison.Ordinal))
            {
                operation.RuntimeBinding = binding;
                return true;
            }
            if (string.Equals(operation.WorkMode, WaitProc.StateJumpMode, StringComparison.Ordinal))
            {
                if (!RuntimeGotoTarget.TryCreate(operation.ReadyGoto, procIndex, proc,
                        out RuntimeGotoTarget readyTarget, out error)
                    || !RuntimeGotoTarget.TryCreate(operation.AbnormalGoto, procIndex, proc,
                        out RuntimeGotoTarget abnormalTarget, out error)
                    || !RuntimeGotoTarget.TryCreate(operation.RunningGoto, procIndex, proc,
                        out RuntimeGotoTarget runningTarget, out error))
                {
                    return false;
                }
                binding.ReadyTarget = readyTarget;
                binding.AbnormalTarget = abnormalTarget;
                binding.RunningTarget = runningTarget;
                operation.RuntimeBinding = binding;
                return true;
            }
            if (string.Equals(operation.WorkMode, WaitProc.GetStateMode, StringComparison.Ordinal))
            {
                if (!ValueRef.TryCreate(null, null, operation.StateVariableName, null,
                        false, "流程状态变量", out ValueRef output, out error)
                    || !output.TryBindStatic(
                        valueStore, procId, "流程状态变量", out output, out error))
                {
                    return false;
                }
                binding.StateOutput = output;
                operation.RuntimeBinding = binding;
                return true;
            }
            error = $"工作模式无效:{operation.WorkMode ?? "空"}";
            return false;
        }

        private static bool TryBindCommunicationResponse(
            Guid procId,
            ValueConfigStore valueStore,
            ResponseCommunicationOperationType operation,
            out string error)
        {
            error = null;
            if (operation.RetryCount <= 0 || !operation.ShouldEvaluateResponseConditions)
            {
                operation.RuntimeBinding = new CommunicationResponseRuntimeBinding();
                return true;
            }
            int count = operation.ResponseConditions?.Count ?? 0;
            var sources = new ValueRef[count];
            for (int index = 0; index < count; index++)
            {
                CommunicationResponseCondition condition = operation.ResponseConditions[index];
                if (condition == null
                    || !ValueRef.TryCreate(null, null, condition.SourceVariableName, null,
                        false, "通信结果判定变量", out sources[index], out error)
                    || !sources[index].TryBindStatic(
                        valueStore, procId, "通信结果判定变量", out sources[index], out error))
                {
                    return false;
                }
            }
            operation.RuntimeBinding = new CommunicationResponseRuntimeBinding
            {
                Sources = sources
            };
            return true;
        }

        private static bool TryBindGoto(
            Proc proc, int procIndex, Guid procId, ValueConfigStore valueStore,
            Goto operation, out string error)
        {
            error = null;
            var binding = new GotoRuntimeBinding();
            int caseCount = operation.Params?.Count ?? 0;
            ValueRef source = default;
            if (caseCount > 0 && !ValueRef.TryCreate(operation.ValueIndex, operation.ValueIndex2Index,
                operation.ValueName, operation.ValueName2Index, false, "跳转变量", out source, out error))
            {
                return false;
            }
            if (caseCount > 0
                && !source.TryBindStatic(valueStore, procId, "跳转变量", out source, out error))
            {
                return false;
            }
            binding.Source = source;
            var cases = new List<GotoCaseRuntimeBinding>(caseCount);
            for (int i = 0; i < caseCount; i++)
            {
                GotoParam item = operation.Params[i];
                bool hasLiteral = !string.IsNullOrEmpty(item.MatchValue);
                bool hasReference = !string.IsNullOrEmpty(item.MatchValueIndex)
                    || !string.IsNullOrEmpty(item.MatchValueV);
                if (hasLiteral == hasReference)
                {
                    error = hasLiteral ? "匹配值配置冲突" : "匹配值不能为空";
                    return false;
                }
                ValueRef matchReference = default;
                if (hasReference && !ValueRef.TryCreate(item.MatchValueIndex, null, item.MatchValueV, null,
                    false, "匹配值", out matchReference, out error))
                {
                    return false;
                }
                if (hasReference
                    && !matchReference.TryBindStatic(
                        valueStore, procId, "匹配值", out matchReference, out error))
                {
                    return false;
                }
                if (!RuntimeGotoTarget.TryCreate(item.Goto, procIndex, proc, out RuntimeGotoTarget target, out error))
                {
                    return false;
                }
                cases.Add(new GotoCaseRuntimeBinding(item.MatchValue, matchReference, hasReference, target));
            }
            binding.Cases = cases.ToArray();
            if (!string.IsNullOrWhiteSpace(operation.DefaultGoto))
            {
                if (!RuntimeGotoTarget.TryCreate(operation.DefaultGoto, procIndex, proc,
                    out RuntimeGotoTarget defaultTarget, out error))
                {
                    return false;
                }
                binding.HasDefaultTarget = true;
                binding.DefaultTarget = defaultTarget;
            }
            operation.RuntimeBinding = binding;
            return true;
        }

        private static bool TryBindParamGoto(
            Proc proc, int procIndex, Guid procId, ValueConfigStore valueStore,
            ParamGoto operation, out string error)
        {
            error = null;
            int count = operation.Params?.Count ?? 0;
            if (count == 0)
            {
                error = "逻辑判断参数为空";
                return false;
            }
            var conditions = new ValueRef[count];
            for (int i = 0; i < count; i++)
            {
                ParamGotoParam item = operation.Params[i];
                if (!ValueRef.TryCreate(item.ValueIndex, item.ValueIndex2Index, item.ValueName,
                    item.ValueName2Index, false, "判断变量", out conditions[i], out error))
                {
                    return false;
                }
                if (!conditions[i].TryBindStatic(
                    valueStore, procId, "判断变量", out conditions[i], out error))
                {
                    return false;
                }
            }
            if (!RuntimeGotoTarget.TryCreate(operation.TrueGoto, procIndex, proc,
                out RuntimeGotoTarget trueTarget, out error)
                || !RuntimeGotoTarget.TryCreate(operation.FalseGoto, procIndex, proc,
                    out RuntimeGotoTarget falseTarget, out error))
            {
                return false;
            }
            operation.RuntimeBinding = new ParamGotoRuntimeBinding
            {
                Conditions = conditions,
                TrueTarget = trueTarget,
                FalseTarget = falseTarget
            };
            return true;
        }

        private static bool TryBindBranch(Proc proc, int procIndex, OperationType operation,
            string trueGoto, string falseGoto, out string error)
        {
            if (!RuntimeGotoTarget.TryCreate(trueGoto, procIndex, proc,
                out RuntimeGotoTarget trueTarget, out error)
                || !RuntimeGotoTarget.TryCreate(falseGoto, procIndex, proc,
                    out RuntimeGotoTarget falseTarget, out error))
            {
                return false;
            }
            operation.RuntimeBinding = new BranchRuntimeBinding
            {
                TrueTarget = trueTarget,
                FalseTarget = falseTarget
            };
            return true;
        }

        private static bool TryBindGetValue(
            Guid procId, ValueConfigStore valueStore, GetValue operation, out string error)
        {
            error = null;
            int count = operation.Params?.Count ?? 0;
            if (count == 0)
            {
                error = "获取变量参数为空";
                return false;
            }
            var sources = new ValueRef[count];
            var destinations = new ValueRef[count];
            for (int i = 0; i < count; i++)
            {
                var item = operation.Params[i];
                if (!ValueRef.TryCreate(item.ValueSourceIndex, item.ValueSourceIndex2Index,
                    item.ValueSourceName, item.ValueSourceName2Index, false, "源变量", out sources[i], out error)
                    || !ValueRef.TryCreate(item.ValueSaveIndex, item.ValueSaveIndex2Index,
                        item.ValueSaveName, item.ValueSaveName2Index, false, "存储变量", out destinations[i], out error))
                {
                    return false;
                }
                if (!sources[i].TryBindStatic(
                        valueStore, procId, "源变量", out sources[i], out error)
                    || !destinations[i].TryBindStatic(
                        valueStore, procId, "存储变量", out destinations[i], out error))
                {
                    return false;
                }
            }
            operation.RuntimeBinding = new GetValueRuntimeBinding
            {
                Sources = sources,
                Destinations = destinations
            };
            return true;
        }

        private static bool TryBindModifyValue(
            Guid procId, ValueConfigStore valueStore, ModifyValue operation, out string error)
        {
            error = null;
            if (!ValueRef.TryCreate(operation.ValueSourceIndex, operation.ValueSourceIndex2Index,
                operation.ValueSourceName, operation.ValueSourceName2Index, false, "源变量",
                out ValueRef source, out error)
                || !ValueRef.TryCreate(operation.OutputValueIndex, operation.OutputValueIndex2Index,
                    operation.OutputValueName, operation.OutputValueName2Index, false, "结果变量",
                    out ValueRef output, out error))
            {
                return false;
            }
            if (!source.TryBindStatic(valueStore, procId, "源变量", out source, out error)
                || !output.TryBindStatic(valueStore, procId, "结果变量", out output, out error))
            {
                return false;
            }
            bool usesLiteral = !string.IsNullOrEmpty(operation.ChangeValue);
            bool hasReference = !string.IsNullOrEmpty(operation.ChangeValueIndex)
                || !string.IsNullOrEmpty(operation.ChangeValueIndex2Index)
                || !string.IsNullOrEmpty(operation.ChangeValueName)
                || !string.IsNullOrEmpty(operation.ChangeValueName2Index);
            if (usesLiteral == hasReference)
            {
                error = usesLiteral ? "修改值配置冲突" : "修改值不能为空";
                return false;
            }
            ValueRef changeValue = default;
            if (hasReference && !ValueRef.TryCreate(operation.ChangeValueIndex, operation.ChangeValueIndex2Index,
                operation.ChangeValueName, operation.ChangeValueName2Index, false, "修改值",
                out changeValue, out error))
            {
                return false;
            }
            if (hasReference
                && !changeValue.TryBindStatic(
                    valueStore, procId, "修改值", out changeValue, out error))
            {
                return false;
            }
            operation.RuntimeBinding = new ModifyValueRuntimeBinding
            {
                Source = source,
                UsesLiteralChangeValue = usesLiteral,
                ChangeValue = changeValue,
                Output = output
            };
            return true;
        }
    }
}
