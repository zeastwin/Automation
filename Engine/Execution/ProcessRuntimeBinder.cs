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
        public ParamGotoConditionRuntimeBinding[] Conditions { get; set; } =
            Array.Empty<ParamGotoConditionRuntimeBinding>();
        public RuntimeGotoTarget TrueTarget { get; set; }
        public RuntimeGotoTarget FalseTarget { get; set; }
    }

    internal enum ParamGotoJudgeMode
    {
        LessThanBoundary,
        GreaterThanBoundary,
        InsideRange,
        EqualText
    }

    internal enum ParamGotoLogicalOperator
    {
        None,
        And,
        Or
    }

    internal readonly struct ParamGotoConditionRuntimeBinding
    {
        public ParamGotoConditionRuntimeBinding(
            ValueRef source,
            ParamGotoJudgeMode judgeMode,
            double down,
            double up,
            bool includeBoundary,
            string expectedText,
            ParamGotoLogicalOperator logicalOperator)
        {
            Source = source;
            JudgeMode = judgeMode;
            Down = down;
            Up = up;
            IncludeBoundary = includeBoundary;
            ExpectedText = expectedText;
            LogicalOperator = logicalOperator;
        }

        public ValueRef Source { get; }
        public ParamGotoJudgeMode JudgeMode { get; }
        public double Down { get; }
        public double Up { get; }
        public bool IncludeBoundary { get; }
        public string ExpectedText { get; }
        public ParamGotoLogicalOperator LogicalOperator { get; }
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
        public bool NeedsNumericValues { get; set; }
        public bool LiteralNumericValueValidated { get; set; }
        public Func<string, string, string> Calculate { get; set; }
    }

    internal sealed class CommunicationResponseRuntimeBinding
    {
        public ValueRef[] Sources { get; set; } = Array.Empty<ValueRef>();
    }

    internal sealed class StringFormatRuntimeBinding
    {
        public ValueRef[] Sources { get; set; } = Array.Empty<ValueRef>();
        public ValueRef Output { get; set; }
    }

    internal sealed class SplitRuntimeBinding
    {
        public ValueRef Source { get; set; }
        public ValueRef Output { get; set; }
    }

    internal sealed class ReplaceRuntimeBinding
    {
        public ValueRef Source { get; set; }
        public bool UsesLiteralReplaceText { get; set; }
        public string ReplaceText { get; set; }
        public ValueRef ReplaceTextSource { get; set; }
        public bool UsesLiteralNewText { get; set; }
        public string NewText { get; set; }
        public ValueRef NewTextSource { get; set; }
        public ValueRef Output { get; set; }
    }

    internal sealed class GetDataStructRuntimeBinding
    {
        public ValueRef FirstOutput { get; set; }
        public ValueRef[] Outputs { get; set; } = Array.Empty<ValueRef>();
    }

    internal sealed class SetDataStructRuntimeBinding
    {
        public bool[] UsesLiteralValues { get; set; } = Array.Empty<bool>();
        public ValueRef[] ValueSources { get; set; } = Array.Empty<ValueRef>();
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

        internal static bool TryBindStandalone(
            Guid procId,
            ValueConfigStore valueStore,
            OperationType operation,
            out string error)
        {
            if (operation == null)
            {
                error = "指令为空";
                return false;
            }
            return TryBindOperation(
                null, 0, procId, valueStore, operation, out error);
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
                case StringFormat stringFormat:
                    return TryBindStringFormat(procId, valueStore, stringFormat, out error);
                case Split split:
                    return TryBindSplit(procId, valueStore, split, out error);
                case Replace replace:
                    return TryBindReplace(procId, valueStore, replace, out error);
                case SetDataStructItem setDataStructItem:
                    return TryBindSetDataStructItem(
                        procId, valueStore, setDataStructItem, out error);
                case GetDataStructItem getDataStructItem:
                    return TryBindGetDataStructItem(
                        procId, valueStore, getDataStructItem, out error);
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
            var conditions = new ParamGotoConditionRuntimeBinding[count];
            for (int i = 0; i < count; i++)
            {
                ParamGotoParam item = operation.Params[i];
                if (item == null)
                {
                    error = $"逻辑判断参数{i}为空";
                    return false;
                }
                if (!ValueRef.TryCreate(item.ValueIndex, item.ValueIndex2Index, item.ValueName,
                    item.ValueName2Index, false, "判断变量", out ValueRef source, out error))
                {
                    return false;
                }
                if (!source.TryBindStatic(
                    valueStore, procId, "判断变量", out source, out error))
                {
                    return false;
                }
                ParamGotoJudgeMode judgeMode;
                switch (item.JudgeMode)
                {
                    case "值在区间左":
                        judgeMode = ParamGotoJudgeMode.LessThanBoundary;
                        break;
                    case "值在区间右":
                        judgeMode = ParamGotoJudgeMode.GreaterThanBoundary;
                        break;
                    case "值在区间内":
                        judgeMode = ParamGotoJudgeMode.InsideRange;
                        break;
                    case "等于特征字符":
                        judgeMode = ParamGotoJudgeMode.EqualText;
                        break;
                    default:
                        error = $"判断模式无效:{item.JudgeMode ?? "空"}";
                        return false;
                }
                ParamGotoLogicalOperator logicalOperator =
                    ParamGotoLogicalOperator.None;
                if (i > 0)
                {
                    if (item.Operator == "且")
                    {
                        logicalOperator = ParamGotoLogicalOperator.And;
                    }
                    else if (item.Operator == "或")
                    {
                        logicalOperator = ParamGotoLogicalOperator.Or;
                    }
                    else
                    {
                        error = $"逻辑运算符无效:{item.Operator ?? "空"}";
                        return false;
                    }
                }
                conditions[i] = new ParamGotoConditionRuntimeBinding(
                    source,
                    judgeMode,
                    item.Down,
                    item.Up,
                    item.IncludeBoundary,
                    item.ExpectedText,
                    logicalOperator);
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
            bool needsNumericValues = operation.ModifyType == "叠加"
                || operation.ModifyType == "乘法"
                || operation.ModifyType == "除法"
                || operation.ModifyType == "求余"
                || operation.ModifyType == "绝对值";
            bool literalNumericValueValidated = false;
            if (needsNumericValues
                && operation.ModifyType != "绝对值"
                && usesLiteral)
            {
                if (!double.TryParse(operation.ChangeValue, out _))
                {
                    error = "修改值不是有效数字";
                    return false;
                }
                literalNumericValueValidated = true;
            }
            Func<string, string, string> calculate;
            switch (operation.ModifyType)
            {
                case "替换":
                    calculate = (sourceText, changeText) => changeText;
                    break;
                case "叠加":
                    double addSourceSign = operation.NegateSource ? -1d : 1d;
                    double addOperandSign = operation.NegateOperand ? -1d : 1d;
                    calculate = (sourceText, changeText) =>
                        (addSourceSign * double.Parse(sourceText)
                            + addOperandSign * double.Parse(changeText)).ToString();
                    break;
                case "乘法":
                    double multiplySourceSign = operation.NegateSource ? -1d : 1d;
                    double multiplyOperandSign = operation.NegateOperand ? -1d : 1d;
                    calculate = (sourceText, changeText) =>
                        (multiplySourceSign * double.Parse(sourceText)
                            * multiplyOperandSign * double.Parse(changeText)).ToString();
                    break;
                case "除法":
                    double divideSourceSign = operation.NegateSource ? -1d : 1d;
                    double divideOperandSign = operation.NegateOperand ? -1d : 1d;
                    calculate = (sourceText, changeText) =>
                        ((divideSourceSign * double.Parse(sourceText))
                            / (divideOperandSign * double.Parse(changeText))).ToString();
                    break;
                case "求余":
                    calculate = (sourceText, changeText) =>
                        (double.Parse(sourceText) % double.Parse(changeText)).ToString();
                    break;
                case "绝对值":
                    calculate = (sourceText, changeText) =>
                        Math.Abs(double.Parse(sourceText)).ToString();
                    break;
                default:
                    error = $"修改模式无效:{operation.ModifyType ?? "空"}";
                    return false;
            }
            operation.RuntimeBinding = new ModifyValueRuntimeBinding
            {
                Source = source,
                UsesLiteralChangeValue = usesLiteral,
                ChangeValue = changeValue,
                Output = output,
                NeedsNumericValues = needsNumericValues,
                LiteralNumericValueValidated = literalNumericValueValidated,
                Calculate = calculate
            };
            return true;
        }

        private static bool TryBindStringFormat(
            Guid procId, ValueConfigStore valueStore, StringFormat operation, out string error)
        {
            error = null;
            int count = operation.Params?.Count ?? 0;
            if (count == 0)
            {
                error = "字符串格式化参数为空";
                return false;
            }
            var sources = new ValueRef[count];
            for (int i = 0; i < count; i++)
            {
                StringFormatParam item = operation.Params[i];
                if (item == null
                    || !ValueRef.TryCreate(
                        item.ValueSourceIndex, null, item.ValueSourceName, null,
                        false, "源变量", out sources[i], out error)
                    || !sources[i].TryBindStatic(
                        valueStore, procId, "源变量", out sources[i], out error))
                {
                    error = error ?? $"字符串格式化参数{i}为空";
                    return false;
                }
            }
            if (!ValueRef.TryCreate(
                    operation.OutputValueIndex, null, operation.OutputValueName, null,
                    false, "存储变量", out ValueRef output, out error)
                || !output.TryBindStatic(
                    valueStore, procId, "存储变量", out output, out error))
            {
                return false;
            }
            operation.RuntimeBinding = new StringFormatRuntimeBinding
            {
                Sources = sources,
                Output = output
            };
            return true;
        }

        private static bool TryBindSplit(
            Guid procId, ValueConfigStore valueStore, Split operation, out string error)
        {
            error = null;
            if (!ValueRef.TryCreate(
                    operation.SourceValueIndex, null, operation.SourceValue, null,
                    false, "源变量", out ValueRef source, out error)
                || !source.TryBindStatic(
                    valueStore, procId, "源变量", out source, out error)
                || !ValueRef.TryCreate(
                    operation.OutputIndex, null, operation.Output, null,
                    false, "结果变量", out ValueRef output, out error)
                || !output.TryBindStatic(
                    valueStore, procId, "结果变量", out output, out error))
            {
                return false;
            }
            operation.RuntimeBinding = new SplitRuntimeBinding
            {
                Source = source,
                Output = output
            };
            return true;
        }

        private static bool TryBindReplace(
            Guid procId, ValueConfigStore valueStore, Replace operation, out string error)
        {
            error = null;
            if (!ValueRef.TryCreate(
                    operation.SourceValueIndex, null, operation.SourceValue, null,
                    false, "源变量", out ValueRef source, out error)
                || !source.TryBindStatic(
                    valueStore, procId, "源变量", out source, out error)
                || !TryBindTextValue(
                    procId, valueStore, operation.ReplaceStr,
                    operation.ReplaceStrIndex, operation.ReplaceStrV,
                    "被替换字符", out bool usesLiteralReplaceText,
                    out string replaceText, out ValueRef replaceTextSource, out error)
                || !TryBindTextValue(
                    procId, valueStore, operation.NewStr,
                    operation.NewStrIndex, operation.NewStrV,
                    "新字符", out bool usesLiteralNewText,
                    out string newText, out ValueRef newTextSource, out error)
                || !ValueRef.TryCreate(
                    operation.OutputIndex, null, operation.Output, null,
                    false, "结果变量", out ValueRef output, out error)
                || !output.TryBindStatic(
                    valueStore, procId, "结果变量", out output, out error))
            {
                return false;
            }
            operation.RuntimeBinding = new ReplaceRuntimeBinding
            {
                Source = source,
                UsesLiteralReplaceText = usesLiteralReplaceText,
                ReplaceText = replaceText,
                ReplaceTextSource = replaceTextSource,
                UsesLiteralNewText = usesLiteralNewText,
                NewText = newText,
                NewTextSource = newTextSource,
                Output = output
            };
            return true;
        }

        private static bool TryBindTextValue(
            Guid procId,
            ValueConfigStore valueStore,
            string literal,
            string index,
            string name,
            string label,
            out bool usesLiteral,
            out string literalValue,
            out ValueRef source,
            out string error)
        {
            usesLiteral = !string.IsNullOrEmpty(literal);
            literalValue = literal;
            source = default;
            error = null;
            bool hasReference = !string.IsNullOrEmpty(index) || !string.IsNullOrEmpty(name);
            if (usesLiteral == hasReference)
            {
                error = usesLiteral ? $"{label}配置冲突" : $"{label}不能为空";
                return false;
            }
            if (usesLiteral)
            {
                return true;
            }
            return ValueRef.TryCreate(
                    index, null, name, null, false, label, out source, out error)
                && source.TryBindStatic(
                    valueStore, procId, label, out source, out error);
        }

        private static bool TryBindGetDataStructItem(
            Guid procId,
            ValueConfigStore valueStore,
            GetDataStructItem operation,
            out string error)
        {
            error = null;
            var binding = new GetDataStructRuntimeBinding();
            if (operation.IsAllItem)
            {
                if (!ValueRef.TryCreate(
                        null, null, operation.FirstResultVariableName, null,
                        false, "首个结果变量",
                        out ValueRef firstOutput, out error)
                    || !firstOutput.TryBindStatic(
                        valueStore, procId, "首个结果变量",
                        out firstOutput, out error))
                {
                    return false;
                }
                binding.FirstOutput = firstOutput;
                operation.RuntimeBinding = binding;
                return true;
            }

            int count = operation.Params?.Count ?? 0;
            if (count == 0)
            {
                error = "数据结构读取参数为空";
                return false;
            }
            var outputs = new ValueRef[count];
            for (int i = 0; i < count; i++)
            {
                GetDataStructItemParam item = operation.Params[i];
                if (item == null
                    || !ValueRef.TryCreate(
                        item.OutputValueIndex, null, item.OutputValueName, null,
                        false, "结果变量", out outputs[i], out error)
                    || !outputs[i].TryBindStatic(
                        valueStore, procId, "结果变量",
                        out outputs[i], out error))
                {
                    error = error ?? $"数据结构读取参数{i}为空";
                    return false;
                }
            }
            binding.Outputs = outputs;
            operation.RuntimeBinding = binding;
            return true;
        }

        private static bool TryBindSetDataStructItem(
            Guid procId,
            ValueConfigStore valueStore,
            SetDataStructItem operation,
            out string error)
        {
            error = null;
            int count = operation.Params?.Count ?? 0;
            if (count == 0)
            {
                error = "数据结构设置参数为空";
                return false;
            }
            var usesLiteralValues = new bool[count];
            var valueSources = new ValueRef[count];
            for (int i = 0; i < count; i++)
            {
                SetDataStructItemParam item = operation.Params[i];
                if (item == null)
                {
                    error = $"数据结构设置参数{i}为空";
                    return false;
                }
                bool usesLiteral = !string.IsNullOrEmpty(item.Value);
                bool hasReference = !string.IsNullOrEmpty(item.ValueIndex)
                    || !string.IsNullOrEmpty(item.ValueIndex2Index)
                    || !string.IsNullOrEmpty(item.ValueName)
                    || !string.IsNullOrEmpty(item.ValueName2Index);
                if (usesLiteral == hasReference)
                {
                    error = usesLiteral
                        ? $"写入值[{i}]配置冲突"
                        : $"写入值[{i}]不能为空";
                    return false;
                }
                usesLiteralValues[i] = usesLiteral;
                if (!hasReference)
                {
                    continue;
                }
                if (!ValueRef.TryCreate(
                        item.ValueIndex,
                        item.ValueIndex2Index,
                        item.ValueName,
                        item.ValueName2Index,
                        false,
                        $"写入值[{i}]",
                        out valueSources[i],
                        out error)
                    || !valueSources[i].TryBindStatic(
                        valueStore,
                        procId,
                        $"写入值[{i}]",
                        out valueSources[i],
                        out error))
                {
                    return false;
                }
            }
            operation.RuntimeBinding = new SetDataStructRuntimeBinding
            {
                UsesLiteralValues = usesLiteralValues,
                ValueSources = valueSources
            };
            return true;
        }
    }
}
