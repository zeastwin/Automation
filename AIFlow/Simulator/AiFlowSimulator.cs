using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Automation;

namespace Automation.AIFlow
{
    public static class AiFlowSimulator
    {
        public const string ScenarioVersion = "scenario-1";
        private const int MaxSteps = 10000;

        public static AiFlowTrace Simulate(AiCoreFlow core, AiFlowScenario scenario)
        {
            var trace = new AiFlowTrace();
            if (scenario == null)
            {
                trace.Issues.Add(new AiFlowIssue("SIM_SCENARIO_NULL", "scenario 为空", "simulate"));
                return trace;
            }
            if (!string.Equals(scenario.Version, ScenarioVersion, StringComparison.Ordinal))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_SCENARIO_VERSION", $"scenario 版本不匹配:{scenario.Version}", "simulate"));
                return trace;
            }

            AiFlowCompileResult compile = AiFlowCompiler.CompileCore(core);
            if (!compile.Success)
            {
                trace.Issues.AddRange(compile.Issues);
                return trace;
            }

            List<SimProc> plan = BuildPlan(core, compile.Procs, trace.Issues);
            if (trace.Issues.Count > 0)
            {
                return trace;
            }

            if (!TryResolveStart(plan, scenario, out int procIndex, out int stepIndex, out int opIndex, out string error))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_START_INVALID", error, "simulate"));
                return trace;
            }

            var store = new SimValueStore(scenario.ValuesByName, scenario.ValuesByIndex, trace);
            bool allowUnsupported = scenario.AllowUnsupported == true;
            int sequence = 0;
            int guard = 0;

            while (guard++ < MaxSteps)
            {
                if (procIndex < 0 || procIndex >= plan.Count)
                {
                    trace.Issues.Add(new AiFlowIssue("SIM_PROC_RANGE", $"流程索引超界:{procIndex}", "simulate"));
                    break;
                }
                SimProc proc = plan[procIndex];
                if (stepIndex >= proc.Steps.Count)
                {
                    break;
                }
                SimStep step = proc.Steps[stepIndex];
                if (opIndex >= step.Ops.Count)
                {
                    stepIndex++;
                    opIndex = 0;
                    continue;
                }

                SimOp simOp = step.Ops[opIndex];
                trace.Events.Add(new AiFlowTraceEvent
                {
                    Sequence = sequence++,
                    Type = "op",
                    ProcId = proc.ProcId,
                    StepId = step.StepId,
                    OpId = simOp.OpId,
                    OpCode = simOp.OpCode,
                    Message = simOp.OpName
                });

                if (simOp.Operation.Disable)
                {
                    trace.Events.Add(new AiFlowTraceEvent
                    {
                        Sequence = sequence++,
                        Type = "skip",
                        ProcId = proc.ProcId,
                        StepId = step.StepId,
                        OpId = simOp.OpId,
                        OpCode = simOp.OpCode,
                        Message = "禁用跳过"
                    });
                    opIndex++;
                    continue;
                }

                string decisionKey = BuildDecisionKey(proc.ProcId, step.StepId, simOp.OpId);
                if (scenario.Decisions != null && scenario.Decisions.TryGetValue(decisionKey, out AiFlowScenarioDecision decision))
                {
                    if (!ApplyDecision(decision, simOp, procIndex, ref stepIndex, ref opIndex, trace, ref sequence))
                    {
                        if (!allowUnsupported)
                        {
                            trace.Issues.Add(new AiFlowIssue("SIM_DECISION_INVALID", $"决策无效:{decisionKey}", "simulate"));
                            break;
                        }
                    }
                    continue;
                }

                if (!ExecuteOperation(simOp, store, procIndex, ref stepIndex, ref opIndex, trace, ref sequence, allowUnsupported))
                {
                    break;
                }
            }

            if (guard >= MaxSteps)
            {
                trace.Issues.Add(new AiFlowIssue("SIM_STEP_LIMIT", "模拟步数超出上限", "simulate"));
            }

            return trace;
        }

        private static bool ApplyDecision(AiFlowScenarioDecision decision, SimOp simOp, int procIndex, ref int stepIndex, ref int opIndex, AiFlowTrace trace, ref int sequence)
        {
            if (decision == null || string.IsNullOrWhiteSpace(decision.Type))
            {
                return false;
            }
            if (decision.Type == "skip")
            {
                trace.Events.Add(new AiFlowTraceEvent
                {
                    Sequence = sequence++,
                    Type = "skip",
                    ProcId = simOp.ProcId,
                    StepId = simOp.StepId,
                    OpId = simOp.OpId,
                    OpCode = simOp.OpCode,
                    Message = "决策跳过"
                });
                opIndex++;
                return true;
            }
            if (decision.Type == "goto")
            {
                if (!TryParseGoto(decision.Target, out int gotoProc, out int gotoStep, out int gotoOp))
                {
                    return false;
                }
                if (gotoProc != procIndex)
                {
                    trace.Issues.Add(new AiFlowIssue("SIM_GOTO_CROSS_PROC", $"跨流程跳转:{decision.Target}", "simulate"));
                    return false;
                }
                trace.Events.Add(new AiFlowTraceEvent
                {
                    Sequence = sequence++,
                    Type = "goto",
                    ProcId = simOp.ProcId,
                    StepId = simOp.StepId,
                    OpId = simOp.OpId,
                    OpCode = simOp.OpCode,
                    Message = decision.Target
                });
                stepIndex = gotoStep;
                opIndex = gotoOp;
                return true;
            }
            if (decision.Type == "result" && simOp.Operation is ParamGoto paramGoto)
            {
                bool result = decision.Result == true;
                string target = result ? paramGoto.goto1 : paramGoto.goto2;
                if (!TryParseGoto(target, out int gotoProc, out int gotoStep, out int gotoOp))
                {
                    return false;
                }
                if (gotoProc != procIndex)
                {
                    trace.Issues.Add(new AiFlowIssue("SIM_GOTO_CROSS_PROC", $"跨流程跳转:{target}", "simulate"));
                    return false;
                }
                trace.Events.Add(new AiFlowTraceEvent
                {
                    Sequence = sequence++,
                    Type = "goto",
                    ProcId = simOp.ProcId,
                    StepId = simOp.StepId,
                    OpId = simOp.OpId,
                    OpCode = simOp.OpCode,
                    Message = target
                });
                stepIndex = gotoStep;
                opIndex = gotoOp;
                return true;
            }
            return false;
        }

        private static bool ExecuteOperation(SimOp simOp, SimValueStore store, int procIndex, ref int stepIndex, ref int opIndex, AiFlowTrace trace, ref int sequence, bool allowUnsupported)
        {
            switch (simOp.Operation)
            {
                case Delay delay:
                    trace.Events.Add(new AiFlowTraceEvent
                    {
                        Sequence = sequence++,
                        Type = "delay",
                        ProcId = simOp.ProcId,
                        StepId = simOp.StepId,
                        OpId = simOp.OpId,
                        OpCode = simOp.OpCode,
                        Message = delay.timeMiniSecond
                    });
                    opIndex++;
                    return true;
                case Goto gotoOp:
                    return ExecuteGoto(simOp, gotoOp, store, procIndex, ref stepIndex, ref opIndex, trace, ref sequence);
                case ParamGoto paramGoto:
                    return ExecuteParamGoto(simOp, paramGoto, store, procIndex, ref stepIndex, ref opIndex, trace, ref sequence);
                case ModifyValue modifyValue:
                    return ExecuteModifyValue(simOp, modifyValue, store, ref opIndex, trace, ref sequence);
                case GetValue getValue:
                    return ExecuteGetValue(simOp, getValue, store, ref opIndex, trace, ref sequence);
                case StringFormat stringFormat:
                    return ExecuteStringFormat(simOp, stringFormat, store, ref opIndex, trace, ref sequence);
                case Split split:
                    return ExecuteSplit(simOp, split, store, ref opIndex, trace, ref sequence);
                case Replace replace:
                    return ExecuteReplace(simOp, replace, store, ref opIndex, trace, ref sequence);
                default:
                    if (allowUnsupported)
                    {
                        trace.Events.Add(new AiFlowTraceEvent
                        {
                            Sequence = sequence++,
                            Type = "unsupported",
                            ProcId = simOp.ProcId,
                            StepId = simOp.StepId,
                            OpId = simOp.OpId,
                            OpCode = simOp.OpCode,
                            Message = "未实现操作"
                        });
                        opIndex++;
                        return true;
                    }
                    trace.Issues.Add(new AiFlowIssue("SIM_UNSUPPORTED_OP", $"不支持的操作:{simOp.OpCode}", "simulate"));
                    return false;
            }
        }

        private static bool ExecuteGoto(SimOp simOp, Goto gotoOp, SimValueStore store, int procIndex, ref int stepIndex, ref int opIndex, AiFlowTrace trace, ref int sequence)
        {
            if (!store.TryResolveValue(gotoOp.ValueName, gotoOp.ValueIndex, gotoOp.ValueName2Index, gotoOp.ValueIndex2Index, out string sourceValue, out string error))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_GOTO_VALUE", error, "simulate"));
                return false;
            }
            if (gotoOp.Params != null)
            {
                foreach (var item in gotoOp.Params)
                {
                    if (item == null)
                    {
                        continue;
                    }
                    string matchValue;
                    if (!string.IsNullOrWhiteSpace(item.MatchValue))
                    {
                        matchValue = item.MatchValue;
                    }
                    else
                    {
                        if (!store.TryResolveValue(item.MatchValueV, item.MatchValueIndex, null, null, out matchValue, out string matchError))
                        {
                            trace.Issues.Add(new AiFlowIssue("SIM_GOTO_MATCH", matchError, "simulate"));
                            return false;
                        }
                    }
                    if (sourceValue == matchValue)
                    {
                        return ApplyGotoTarget(simOp, item.Goto, procIndex, ref stepIndex, ref opIndex, trace, ref sequence);
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(gotoOp.DefaultGoto))
            {
                return ApplyGotoTarget(simOp, gotoOp.DefaultGoto, procIndex, ref stepIndex, ref opIndex, trace, ref sequence);
            }
            trace.Issues.Add(new AiFlowIssue("SIM_GOTO_EMPTY", "跳转失败：未匹配到条件且默认跳转为空", "simulate"));
            return false;
        }

        private static bool ExecuteParamGoto(SimOp simOp, ParamGoto paramGoto, SimValueStore store, int procIndex, ref int stepIndex, ref int opIndex, AiFlowTrace trace, ref int sequence)
        {
            if (paramGoto.Params == null || paramGoto.Params.Count == 0)
            {
                trace.Issues.Add(new AiFlowIssue("SIM_PARAMGOTO_EMPTY", "逻辑判断参数为空", "simulate"));
                return false;
            }
            bool isFirst = true;
            bool output = true;
            foreach (var item in paramGoto.Params)
            {
                if (item == null)
                {
                    continue;
                }
                bool isNumericJudge = item.JudgeMode != "等于特征字符";
                double numericValue = 0;
                string textValue = null;

                if (!store.TryResolveValue(item.ValueName, item.ValueIndex, item.ValueName2Index, item.ValueIndex2Index, out string value, out string error))
                {
                    trace.Issues.Add(new AiFlowIssue("SIM_PARAMGOTO_VALUE", error, "simulate"));
                    return false;
                }
                if (isNumericJudge)
                {
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out numericValue))
                    {
                        trace.Issues.Add(new AiFlowIssue("SIM_PARAMGOTO_NUM", $"数值解析失败:{value}", "simulate"));
                        return false;
                    }
                }
                else
                {
                    textValue = value ?? string.Empty;
                }

                bool tempValue = false;
                if (item.JudgeMode == "值在区间左")
                {
                    tempValue = item.equal ? item.Down >= numericValue : item.Down > numericValue;
                }
                else if (item.JudgeMode == "值在区间右")
                {
                    tempValue = item.equal ? item.Down <= numericValue : item.Down < numericValue;
                }
                else if (item.JudgeMode == "值在区间内")
                {
                    tempValue = item.equal ? item.Down <= numericValue && numericValue <= item.Up : item.Down < numericValue && numericValue < item.Up;
                }
                else if (item.JudgeMode == "等于特征字符")
                {
                    tempValue = textValue == item.keyString;
                }

                if (isFirst)
                {
                    output = tempValue;
                    isFirst = false;
                }
                else
                {
                    output = item.Operator == "或" ? (output || tempValue) : (output && tempValue);
                }
            }

            string target = output ? paramGoto.goto1 : paramGoto.goto2;
            return ApplyGotoTarget(simOp, target, procIndex, ref stepIndex, ref opIndex, trace, ref sequence);
        }

        private static bool ExecuteModifyValue(SimOp simOp, ModifyValue modifyValue, SimValueStore store, ref int opIndex, AiFlowTrace trace, ref int sequence)
        {
            if (!store.TryResolveValue(modifyValue.ValueSourceName, modifyValue.ValueSourceIndex, modifyValue.ValueSourceName2Index, modifyValue.ValueSourceIndex2Index, out string sourceValue, out string error))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_MODIFY_SOURCE", error, "simulate"));
                return false;
            }
            string changeValue = null;
            if (!string.IsNullOrWhiteSpace(modifyValue.ChangeValue))
            {
                changeValue = modifyValue.ChangeValue;
            }
            else
            {
                if (!store.TryResolveValue(modifyValue.ChangeValueName, modifyValue.ChangeValueIndex, modifyValue.ChangeValueName2Index, modifyValue.ChangeValueIndex2Index, out changeValue, out string changeError))
                {
                    trace.Issues.Add(new AiFlowIssue("SIM_MODIFY_CHANGE", changeError, "simulate"));
                    return false;
                }
            }
            if (changeValue == null)
            {
                trace.Issues.Add(new AiFlowIssue("SIM_MODIFY_CHANGE_EMPTY", "修改值为空", "simulate"));
                return false;
            }

            string outputValue = changeValue;
            if (modifyValue.ModifyType != "替换")
            {
                if (!double.TryParse(sourceValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double sourceNumber))
                {
                    trace.Issues.Add(new AiFlowIssue("SIM_MODIFY_NUM", "源变量不是数值", "simulate"));
                    return false;
                }
                if (modifyValue.ModifyType != "绝对值" && !double.TryParse(changeValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double changeNumber))
                {
                    trace.Issues.Add(new AiFlowIssue("SIM_MODIFY_NUM", "修改值不是数值", "simulate"));
                    return false;
                }
                double result = sourceNumber;
                switch (modifyValue.ModifyType)
                {
                    case "叠加":
                        result = (modifyValue.sourceR ? -1 : 1) * sourceNumber + (modifyValue.ChangeR ? -1 : 1) * double.Parse(changeValue, CultureInfo.InvariantCulture);
                        break;
                    case "乘法":
                        result = (modifyValue.sourceR ? -1 : 1) * sourceNumber * (modifyValue.ChangeR ? -1 : 1) * double.Parse(changeValue, CultureInfo.InvariantCulture);
                        break;
                    case "除法":
                        result = (modifyValue.sourceR ? -1 : 1) * sourceNumber / ((modifyValue.ChangeR ? -1 : 1) * double.Parse(changeValue, CultureInfo.InvariantCulture));
                        break;
                    case "求余":
                        result = (modifyValue.sourceR ? -1 : 1) * sourceNumber % ((modifyValue.ChangeR ? -1 : 1) * double.Parse(changeValue, CultureInfo.InvariantCulture));
                        break;
                    case "绝对值":
                        result = Math.Abs(sourceNumber);
                        break;
                }
                outputValue = result.ToString(CultureInfo.InvariantCulture);
            }

            if (!store.TrySetValue(modifyValue.OutputValueName, modifyValue.OutputValueIndex, modifyValue.OutputValueName2Index, modifyValue.OutputValueIndex2Index, outputValue, out string setError))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_MODIFY_OUTPUT", setError, "simulate"));
                return false;
            }

            trace.Events.Add(new AiFlowTraceEvent
            {
                Sequence = sequence++,
                Type = "write",
                ProcId = simOp.ProcId,
                StepId = simOp.StepId,
                OpId = simOp.OpId,
                OpCode = simOp.OpCode,
                Message = outputValue
            });
            opIndex++;
            return true;
        }

        private static bool ExecuteGetValue(SimOp simOp, GetValue getValue, SimValueStore store, ref int opIndex, AiFlowTrace trace, ref int sequence)
        {
            if (getValue.Params == null || getValue.Params.Count == 0)
            {
                trace.Issues.Add(new AiFlowIssue("SIM_GETVALUE_EMPTY", "获取变量参数为空", "simulate"));
                return false;
            }
            foreach (var item in getValue.Params)
            {
                if (item == null)
                {
                    continue;
                }
                if (!store.TryResolveValue(item.ValueSourceName, item.ValueSourceIndex, item.ValueSourceName2Index, item.ValueSourceIndex2Index, out string sourceValue, out string error))
                {
                    trace.Issues.Add(new AiFlowIssue("SIM_GETVALUE_SOURCE", error, "simulate"));
                    return false;
                }
                if (!store.TrySetValue(item.ValueSaveName, item.ValueSaveIndex, item.ValueSaveName2Index, item.ValueSaveIndex2Index, sourceValue, out string setError))
                {
                    trace.Issues.Add(new AiFlowIssue("SIM_GETVALUE_SAVE", setError, "simulate"));
                    return false;
                }
            }
            trace.Events.Add(new AiFlowTraceEvent
            {
                Sequence = sequence++,
                Type = "copy",
                ProcId = simOp.ProcId,
                StepId = simOp.StepId,
                OpId = simOp.OpId,
                OpCode = simOp.OpCode
            });
            opIndex++;
            return true;
        }

        private static bool ExecuteStringFormat(SimOp simOp, StringFormat stringFormat, SimValueStore store, ref int opIndex, AiFlowTrace trace, ref int sequence)
        {
            if (stringFormat.Params == null)
            {
                trace.Issues.Add(new AiFlowIssue("SIM_FORMAT_EMPTY", "拼接参数为空", "simulate"));
                return false;
            }
            var values = new List<string>();
            foreach (var item in stringFormat.Params)
            {
                if (item == null)
                {
                    continue;
                }
                if (!store.TryResolveValue(item.ValueSourceName, item.ValueSourceIndex, null, null, out string value, out string error))
                {
                    trace.Issues.Add(new AiFlowIssue("SIM_FORMAT_VALUE", error, "simulate"));
                    return false;
                }
                values.Add(value ?? string.Empty);
            }
            string formatted;
            try
            {
                formatted = string.Format(stringFormat.Format ?? string.Empty, values.ToArray());
            }
            catch (Exception ex)
            {
                trace.Issues.Add(new AiFlowIssue("SIM_FORMAT_FAIL", ex.Message, "simulate"));
                return false;
            }
            if (!store.TrySetValue(stringFormat.OutputValueName, stringFormat.OutputValueIndex, null, null, formatted, out string setError))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_FORMAT_SAVE", setError, "simulate"));
                return false;
            }
            trace.Events.Add(new AiFlowTraceEvent
            {
                Sequence = sequence++,
                Type = "write",
                ProcId = simOp.ProcId,
                StepId = simOp.StepId,
                OpId = simOp.OpId,
                OpCode = simOp.OpCode,
                Message = formatted
            });
            opIndex++;
            return true;
        }

        private static bool ExecuteSplit(SimOp simOp, Split split, SimValueStore store, ref int opIndex, AiFlowTrace trace, ref int sequence)
        {
            if (!store.TryResolveValue(split.SourceValue, split.SourceValueIndex, null, null, out string sourceValue, out string error))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_SPLIT_SOURCE", error, "simulate"));
                return false;
            }
            if (string.IsNullOrWhiteSpace(split.OutputIndex))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_SPLIT_OUTPUT", "Split 需要 OutputIndex", "simulate"));
                return false;
            }
            if (!int.TryParse(split.OutputIndex, out int outputIndex))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_SPLIT_OUTPUT", "Split OutputIndex 无法解析", "simulate"));
                return false;
            }
            if (!int.TryParse(split.startIndex, out int startIndex))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_SPLIT_START", "Split startIndex 无法解析", "simulate"));
                return false;
            }
            string[] parts = (sourceValue ?? string.Empty).Split(split.SplitMark);
            int count = parts.Length;
            if (!string.IsNullOrWhiteSpace(split.Count) && int.TryParse(split.Count, out int parsed))
            {
                count = parsed;
            }
            for (int i = 0; i < count; i++)
            {
                string value = (startIndex + i) < parts.Length ? parts[startIndex + i] : string.Empty;
                store.SetByIndex((outputIndex + i).ToString(), value);
            }
            trace.Events.Add(new AiFlowTraceEvent
            {
                Sequence = sequence++,
                Type = "split",
                ProcId = simOp.ProcId,
                StepId = simOp.StepId,
                OpId = simOp.OpId,
                OpCode = simOp.OpCode
            });
            opIndex++;
            return true;
        }

        private static bool ExecuteReplace(SimOp simOp, Replace replace, SimValueStore store, ref int opIndex, AiFlowTrace trace, ref int sequence)
        {
            if (!store.TryResolveValue(replace.SourceValue, replace.SourceValueIndex, null, null, out string sourceValue, out string error))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_REPLACE_SOURCE", error, "simulate"));
                return false;
            }
            string replaceStr;
            if (!ResolveLiteralOrValue(store, replace.ReplaceStr, replace.ReplaceStrIndex, replace.ReplaceStrV, "被替换字符", out replaceStr, out error))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_REPLACE_SRC", error, "simulate"));
                return false;
            }
            string newStr;
            if (!ResolveLiteralOrValue(store, replace.NewStr, replace.NewStrIndex, replace.NewStrV, "新字符", out newStr, out error))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_REPLACE_NEW", error, "simulate"));
                return false;
            }
            string output = sourceValue ?? string.Empty;
            if (replace.ReplaceType == "替换指定字符")
            {
                output = output.Replace(replaceStr ?? string.Empty, newStr ?? string.Empty);
            }
            else if (replace.ReplaceType == "替换指定区间")
            {
                if (!int.TryParse(replace.StartIndex, out int startIndex) || !int.TryParse(replace.Count, out int count))
                {
                    trace.Issues.Add(new AiFlowIssue("SIM_REPLACE_RANGE", "替换区间参数无效", "simulate"));
                    return false;
                }
                if (startIndex < 0 || count < 0 || startIndex > output.Length)
                {
                    trace.Issues.Add(new AiFlowIssue("SIM_REPLACE_RANGE", "替换区间越界", "simulate"));
                    return false;
                }
                string before = output.Substring(0, startIndex);
                string after = startIndex + count < output.Length ? output.Substring(startIndex + count) : string.Empty;
                output = before + (newStr ?? string.Empty) + after;
            }
            if (!store.TrySetValue(replace.Output, replace.OutputIndex, null, null, output, out string setError))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_REPLACE_SAVE", setError, "simulate"));
                return false;
            }
            trace.Events.Add(new AiFlowTraceEvent
            {
                Sequence = sequence++,
                Type = "write",
                ProcId = simOp.ProcId,
                StepId = simOp.StepId,
                OpId = simOp.OpId,
                OpCode = simOp.OpCode,
                Message = output
            });
            opIndex++;
            return true;
        }

        private static bool ResolveLiteralOrValue(SimValueStore store, string literal, string index, string name, string label, out string value, out string error)
        {
            error = null;
            value = null;
            bool hasLiteral = !string.IsNullOrWhiteSpace(literal);
            bool hasRef = !string.IsNullOrWhiteSpace(index) || !string.IsNullOrWhiteSpace(name);
            if (hasLiteral && hasRef)
            {
                error = $"{label}配置冲突";
                return false;
            }
            if (hasLiteral)
            {
                value = literal;
                return true;
            }
            return store.TryResolveValue(name, index, null, null, out value, out error);
        }

        private static bool ApplyGotoTarget(SimOp simOp, string target, int procIndex, ref int stepIndex, ref int opIndex, AiFlowTrace trace, ref int sequence)
        {
            if (!TryParseGoto(target, out int gotoProc, out int gotoStep, out int gotoOp))
            {
                trace.Issues.Add(new AiFlowIssue("SIM_GOTO_FORMAT", $"跳转格式无效:{target}", "simulate"));
                return false;
            }
            if (gotoProc != procIndex)
            {
                trace.Issues.Add(new AiFlowIssue("SIM_GOTO_CROSS_PROC", $"跨流程跳转:{target}", "simulate"));
                return false;
            }
            trace.Events.Add(new AiFlowTraceEvent
            {
                Sequence = sequence++,
                Type = "goto",
                ProcId = simOp.ProcId,
                StepId = simOp.StepId,
                OpId = simOp.OpId,
                OpCode = simOp.OpCode,
                Message = target
            });
            stepIndex = gotoStep;
            opIndex = gotoOp;
            return true;
        }

        private static List<SimProc> BuildPlan(AiCoreFlow core, List<Proc> procs, List<AiFlowIssue> issues)
        {
            var result = new List<SimProc>();
            if (core == null || core.Procs == null || procs == null)
            {
                issues.Add(new AiFlowIssue("SIM_CORE_NULL", "core 为空", "simulate"));
                return result;
            }
            if (core.Procs.Count != procs.Count)
            {
                issues.Add(new AiFlowIssue("SIM_CORE_MISMATCH", "core 与编译结果流程数量不一致", "simulate"));
                return result;
            }
            for (int i = 0; i < core.Procs.Count; i++)
            {
                AiCoreProc coreProc = core.Procs[i];
                Proc proc = procs[i];
                if (coreProc == null || proc == null)
                {
                    issues.Add(new AiFlowIssue("SIM_PROC_NULL", "流程为空", "simulate"));
                    continue;
                }
                if (coreProc.Steps == null || proc.steps == null || coreProc.Steps.Count != proc.steps.Count)
                {
                    issues.Add(new AiFlowIssue("SIM_STEP_MISMATCH", "步骤数量不一致", "simulate"));
                    continue;
                }
                var simProc = new SimProc(coreProc.Id);
                for (int j = 0; j < coreProc.Steps.Count; j++)
                {
                    AiCoreStep coreStep = coreProc.Steps[j];
                    Step step = proc.steps[j];
                    if (coreStep == null || step == null)
                    {
                        issues.Add(new AiFlowIssue("SIM_STEP_NULL", "步骤为空", "simulate"));
                        continue;
                    }
                    if (coreStep.Ops == null || step.Ops == null || coreStep.Ops.Count != step.Ops.Count)
                    {
                        issues.Add(new AiFlowIssue("SIM_OP_MISMATCH", "操作数量不一致", "simulate"));
                        continue;
                    }
                    var simStep = new SimStep(coreStep.Id);
                    for (int k = 0; k < coreStep.Ops.Count; k++)
                    {
                        AiCoreOp coreOp = coreStep.Ops[k];
                        OperationType op = step.Ops[k];
                        if (coreOp == null || op == null)
                        {
                            issues.Add(new AiFlowIssue("SIM_OP_NULL", "操作为空", "simulate"));
                            continue;
                        }
                        simStep.Ops.Add(new SimOp(coreProc.Id, coreStep.Id, coreOp.Id, coreOp.OpCode, coreOp.Name, op));
                    }
                    simProc.Steps.Add(simStep);
                }
                result.Add(simProc);
            }
            return result;
        }

        private static bool TryResolveStart(List<SimProc> plan, AiFlowScenario scenario, out int procIndex, out int stepIndex, out int opIndex, out string error)
        {
            procIndex = 0;
            stepIndex = 0;
            opIndex = 0;
            error = null;
            if (scenario.Start == null)
            {
                return true;
            }
            if (!string.IsNullOrWhiteSpace(scenario.Start.ProcId))
            {
                procIndex = plan.FindIndex(p => p.ProcId == scenario.Start.ProcId);
                if (procIndex < 0)
                {
                    error = $"流程不存在:{scenario.Start.ProcId}";
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(scenario.Start.StepId))
                {
                    stepIndex = plan[procIndex].Steps.FindIndex(s => s.StepId == scenario.Start.StepId);
                    if (stepIndex < 0)
                    {
                        error = $"步骤不存在:{scenario.Start.StepId}";
                        return false;
                    }
                }
                if (!string.IsNullOrWhiteSpace(scenario.Start.OpId))
                {
                    opIndex = plan[procIndex].Steps[stepIndex].Ops.FindIndex(o => o.OpId == scenario.Start.OpId);
                    if (opIndex < 0)
                    {
                        error = $"操作不存在:{scenario.Start.OpId}";
                        return false;
                    }
                }
                return true;
            }
            if (scenario.Start.ProcIndex.HasValue)
            {
                procIndex = scenario.Start.ProcIndex.Value;
            }
            if (scenario.Start.StepIndex.HasValue)
            {
                stepIndex = scenario.Start.StepIndex.Value;
            }
            if (scenario.Start.OpIndex.HasValue)
            {
                opIndex = scenario.Start.OpIndex.Value;
            }
            return true;
        }

        private static bool TryParseGoto(string value, out int procIndex, out int stepIndex, out int opIndex)
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
            if (!int.TryParse(parts[0], out procIndex))
            {
                return false;
            }
            if (!int.TryParse(parts[1], out stepIndex))
            {
                return false;
            }
            if (!int.TryParse(parts[2], out opIndex))
            {
                return false;
            }
            return procIndex >= 0 && stepIndex >= 0 && opIndex >= 0;
        }

        private static string BuildDecisionKey(string procId, string stepId, string opId)
        {
            return $"{procId}/{stepId}/{opId}";
        }

        private sealed class SimProc
        {
            public SimProc(string procId)
            {
                ProcId = procId;
            }

            public string ProcId { get; }
            public List<SimStep> Steps { get; } = new List<SimStep>();
        }

        private sealed class SimStep
        {
            public SimStep(string stepId)
            {
                StepId = stepId;
            }

            public string StepId { get; }
            public List<SimOp> Ops { get; } = new List<SimOp>();
        }

        private sealed class SimOp
        {
            public SimOp(string procId, string stepId, string opId, string opCode, string opName, OperationType operation)
            {
                ProcId = procId;
                StepId = stepId;
                OpId = opId;
                OpCode = opCode;
                OpName = opName;
                Operation = operation;
            }

            public string ProcId { get; }
            public string StepId { get; }
            public string OpId { get; }
            public string OpCode { get; }
            public string OpName { get; }
            public OperationType Operation { get; }
        }

        private sealed class SimValueStore
        {
            private readonly Dictionary<string, string> byName;
            private readonly Dictionary<string, string> byIndex;
            public SimValueStore(Dictionary<string, string> valuesByName, Dictionary<string, string> valuesByIndex, AiFlowTrace trace)
            {
                byName = valuesByName ?? new Dictionary<string, string>();
                byIndex = valuesByIndex ?? new Dictionary<string, string>();
            }

            public bool TryResolveValue(string name, string index, string name2Index, string index2Index, out string value, out string error)
            {
                value = null;
                error = null;
                if (!string.IsNullOrWhiteSpace(name2Index) || !string.IsNullOrWhiteSpace(index2Index))
                {
                    error = "模拟暂不支持二级索引";
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(name) && byName.TryGetValue(name, out value))
                {
                    return true;
                }
                if (!string.IsNullOrWhiteSpace(index) && byIndex.TryGetValue(index, out value))
                {
                    return true;
                }
                error = $"变量不存在:{name ?? index}";
                return false;
            }

            public bool TrySetValue(string name, string index, string name2Index, string index2Index, string value, out string error)
            {
                error = null;
                if (!string.IsNullOrWhiteSpace(name2Index) || !string.IsNullOrWhiteSpace(index2Index))
                {
                    error = "模拟暂不支持二级索引";
                    return false;
                }
                bool wrote = false;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    byName[name] = value;
                    wrote = true;
                }
                if (!string.IsNullOrWhiteSpace(index))
                {
                    byIndex[index] = value;
                    wrote = true;
                }
                if (!wrote)
                {
                    error = "写入目标为空";
                    return false;
                }
                return true;
            }

            public void SetByIndex(string index, string value)
            {
                if (string.IsNullOrWhiteSpace(index))
                {
                    return;
                }
                byIndex[index] = value;
            }
        }
    }
}
