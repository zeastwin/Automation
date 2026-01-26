using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Automation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation.AIFlow
{
    public enum AiFlowInputKind
    {
        Spec,
        Core
    }

    public static class AiFlowCompiler
    {
        public const string SpecVersion = "spec-1";
        public const string CoreVersion = "core-1";

        private static readonly HashSet<string> AlarmTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "报警停止",
            "报警忽略",
            "自动处理",
            "弹框确定",
            "弹框确定与否",
            "弹框确定与否与取消"
        };

        private static readonly HashSet<string> ReservedOpArgs = new HashSet<string>(StringComparer.Ordinal)
        {
            "Num",
            "Name",
            "OperaType",
            "AlarmType",
            "AlarmInfoID",
            "Goto1",
            "Goto2",
            "Goto3",
            "Note",
            "isStopPoint",
            "Disable"
        };

        private static readonly JsonSerializerSettings StrictSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include,
            TypeNameHandling = TypeNameHandling.None,
            DateParseHandling = DateParseHandling.None
        };

        private static readonly JsonSerializer StrictSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include,
            TypeNameHandling = TypeNameHandling.None,
            DateParseHandling = DateParseHandling.None,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        });

        private static readonly Dictionary<string, Type> OpTypeMap = BuildOpTypeMap();

        public static AiFlowCompileResult CompileFromFile(string inputPath, AiFlowInputKind kind)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                return new AiFlowCompileResult(new List<AiFlowIssue>
                {
                    new AiFlowIssue("INPUT_PATH_EMPTY", "输入文件路径为空", "input")
                }, new List<Proc>());
            }
            if (!File.Exists(inputPath))
            {
                return new AiFlowCompileResult(new List<AiFlowIssue>
                {
                    new AiFlowIssue("INPUT_FILE_NOT_FOUND", $"输入文件不存在:{inputPath}", "input")
                }, new List<Proc>());
            }

            try
            {
                string json = File.ReadAllText(inputPath);
                if (kind == AiFlowInputKind.Spec)
                {
                    AiSpecFlow spec = JsonConvert.DeserializeObject<AiSpecFlow>(json, StrictSettings);
                    return CompileSpec(spec);
                }
                AiCoreFlow core = JsonConvert.DeserializeObject<AiCoreFlow>(json, StrictSettings);
                return CompileCore(core);
            }
            catch (JsonException ex)
            {
                return new AiFlowCompileResult(new List<AiFlowIssue>
                {
                    new AiFlowIssue("JSON_PARSE_ERROR", ex.Message, "input")
                }, new List<Proc>());
            }
            catch (Exception ex)
            {
                return new AiFlowCompileResult(new List<AiFlowIssue>
                {
                    new AiFlowIssue("INPUT_READ_ERROR", ex.Message, "input")
                }, new List<Proc>());
            }
        }

        public static AiFlowCompileResult CompileSpec(AiSpecFlow spec)
        {
            var issues = new List<AiFlowIssue>();
            if (spec == null)
            {
                issues.Add(new AiFlowIssue("SPEC_NULL", "spec 为空", "spec"));
                return new AiFlowCompileResult(issues, new List<Proc>());
            }
            if (!string.Equals(spec.Version, SpecVersion, StringComparison.Ordinal))
            {
                issues.Add(new AiFlowIssue("SPEC_VERSION", $"spec 版本不匹配:{spec.Version}", "spec.version"));
            }
            if (!string.Equals(spec.Kind, "core", StringComparison.Ordinal))
            {
                issues.Add(new AiFlowIssue("SPEC_KIND", $"spec.kind 必须为 core，当前:{spec.Kind}", "spec.kind"));
            }
            if (spec.Core == null)
            {
                issues.Add(new AiFlowIssue("SPEC_CORE_NULL", "spec.core 为空", "spec.core"));
            }
            if (issues.Count > 0)
            {
                return new AiFlowCompileResult(issues, new List<Proc>());
            }
            return CompileCore(spec.Core);
        }

        public static AiFlowCompileResult CompileCore(AiCoreFlow core)
        {
            var issues = new List<AiFlowIssue>();
            if (core == null)
            {
                issues.Add(new AiFlowIssue("CORE_NULL", "core 为空", "core"));
                return new AiFlowCompileResult(issues, new List<Proc>());
            }
            if (!string.Equals(core.Version, CoreVersion, StringComparison.Ordinal))
            {
                issues.Add(new AiFlowIssue("CORE_VERSION", $"core 版本不匹配:{core.Version}", "core.version"));
            }
            if (core.Procs == null)
            {
                issues.Add(new AiFlowIssue("CORE_PROCS_NULL", "core.procs 为空", "core.procs"));
            }
            if (issues.Count > 0)
            {
                return new AiFlowCompileResult(issues, new List<Proc>());
            }

            var procIds = new HashSet<string>(StringComparer.Ordinal);
            var procs = new List<Proc>();

            for (int procIndex = 0; procIndex < core.Procs.Count; procIndex++)
            {
                AiCoreProc procSpec = core.Procs[procIndex];
                string procLoc = BuildProcLocation(procSpec, procIndex);
                bool procValid = true;
                if (procSpec == null)
                {
                    issues.Add(new AiFlowIssue("PROC_NULL", "流程为空", procLoc));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(procSpec.Id))
                {
                    issues.Add(new AiFlowIssue("PROC_ID_EMPTY", "流程 id 为空", procLoc));
                    procValid = false;
                }
                else if (!procIds.Add(procSpec.Id))
                {
                    issues.Add(new AiFlowIssue("PROC_ID_DUP", $"流程 id 重复:{procSpec.Id}", procLoc));
                    procValid = false;
                }
                if (string.IsNullOrWhiteSpace(procSpec.Name))
                {
                    issues.Add(new AiFlowIssue("PROC_NAME_EMPTY", "流程名称为空", procLoc));
                    procValid = false;
                }
                if (procSpec.Steps == null)
                {
                    issues.Add(new AiFlowIssue("PROC_STEPS_NULL", "步骤列表为空", procLoc));
                    procValid = false;
                }
                if (!procValid)
                {
                    continue;
                }

                ProcHead head = new ProcHead
                {
                    Name = procSpec.Name,
                    AutoStart = procSpec.AutoStart ?? false
                };

                ApplyPauseConfig(head, procSpec, issues, procLoc);

                var stepIds = new HashSet<string>(StringComparer.Ordinal);
                var steps = new List<Step>();

                for (int stepIndex = 0; stepIndex < procSpec.Steps.Count; stepIndex++)
                {
                    AiCoreStep stepSpec = procSpec.Steps[stepIndex];
                    string stepLoc = BuildStepLocation(procSpec, procIndex, stepSpec, stepIndex);
                    bool stepValid = true;
                    if (stepSpec == null)
                    {
                        issues.Add(new AiFlowIssue("STEP_NULL", "步骤为空", stepLoc));
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(stepSpec.Id))
                    {
                        issues.Add(new AiFlowIssue("STEP_ID_EMPTY", "步骤 id 为空", stepLoc));
                        stepValid = false;
                    }
                    else if (!stepIds.Add(stepSpec.Id))
                    {
                        issues.Add(new AiFlowIssue("STEP_ID_DUP", $"步骤 id 重复:{stepSpec.Id}", stepLoc));
                        stepValid = false;
                    }
                    if (string.IsNullOrWhiteSpace(stepSpec.Name))
                    {
                        issues.Add(new AiFlowIssue("STEP_NAME_EMPTY", "步骤名称为空", stepLoc));
                        stepValid = false;
                    }
                    if (stepSpec.Ops == null)
                    {
                        issues.Add(new AiFlowIssue("STEP_OPS_NULL", "操作列表为空", stepLoc));
                        stepValid = false;
                    }
                    if (!stepValid)
                    {
                        continue;
                    }

                    var opIds = new HashSet<string>(StringComparer.Ordinal);
                    var ops = new List<OperationType>();

                    for (int opIndex = 0; opIndex < stepSpec.Ops.Count; opIndex++)
                    {
                        AiCoreOp opSpec = stepSpec.Ops[opIndex];
                        string opLoc = BuildOpLocation(procSpec, procIndex, stepSpec, stepIndex, opSpec, opIndex);
                        bool opValid = true;
                        if (opSpec == null)
                        {
                            issues.Add(new AiFlowIssue("OP_NULL", "操作为空", opLoc));
                            continue;
                        }
                        if (string.IsNullOrWhiteSpace(opSpec.Id))
                        {
                            issues.Add(new AiFlowIssue("OP_ID_EMPTY", "操作 id 为空", opLoc));
                            opValid = false;
                        }
                        else if (!opIds.Add(opSpec.Id))
                        {
                            issues.Add(new AiFlowIssue("OP_ID_DUP", $"操作 id 重复:{opSpec.Id}", opLoc));
                            opValid = false;
                        }
                        if (string.IsNullOrWhiteSpace(opSpec.OpCode))
                        {
                            issues.Add(new AiFlowIssue("OP_CODE_EMPTY", "操作 opCode 为空", opLoc));
                            opValid = false;
                        }
                        if (!opValid)
                        {
                            continue;
                        }

                        OperationType operation = CreateOperation(opSpec.OpCode, opLoc, issues);
                        if (operation == null)
                        {
                            continue;
                        }

                        ApplyBaseFields(operation, opSpec);
                        ValidateAndPopulateArgs(operation, opSpec, opLoc, issues);
                        NormalizeCounts(operation, opLoc, issues);
                        ValidateAlarmType(operation, opLoc, issues);

                        ops.Add(operation);
                    }

                    Step step = new Step
                    {
                        Name = stepSpec.Name,
                        Ops = ops
                    };
                    steps.Add(step);
                }

                Proc proc = new Proc
                {
                    head = head,
                    steps = steps
                };
                procs.Add(proc);
            }

            if (issues.Count > 0)
            {
                return new AiFlowCompileResult(issues, new List<Proc>());
            }

            AssignOperationNumbers(procs);
            ValidateGotoTargets(procs, issues);

            return new AiFlowCompileResult(issues, procs);
        }

        public static bool ApplyToWorkPath(List<Proc> procs, string workPath, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            if (procs == null)
            {
                issues.Add(new AiFlowIssue("APPLY_PROCS_NULL", "流程为空", "apply"));
                return false;
            }
            if (string.IsNullOrWhiteSpace(workPath))
            {
                issues.Add(new AiFlowIssue("APPLY_PATH_EMPTY", "输出路径为空", "apply"));
                return false;
            }
            string expectedWork;
            try
            {
                expectedWork = Path.GetFullPath(SF.workPath ?? string.Empty)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("APPLY_PATH_INVALID", $"Config 路径无效:{ex.Message}", "apply"));
                return false;
            }
            if (string.IsNullOrWhiteSpace(expectedWork))
            {
                issues.Add(new AiFlowIssue("APPLY_PATH_INVALID", "Config 路径为空", "apply"));
                return false;
            }
            string actualWork;
            try
            {
                actualWork = Path.GetFullPath(workPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("APPLY_PATH_INVALID", $"Work 路径无效:{ex.Message}", "apply"));
                return false;
            }
            if (!string.Equals(actualWork, expectedWork, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new AiFlowIssue("APPLY_PATH_FORBIDDEN", $"只允许使用 Config\\\\Work 路径:{expectedWork}", "apply"));
                return false;
            }

            try
            {
                WriteWorkConfig(workPath, procs);
                if (SF.frmProc != null)
                {
                    SF.frmProc.Refresh();
                }
                return true;
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("APPLY_ERROR", ex.Message, "apply"));
                return false;
            }
        }

        private static void ApplyBaseFields(OperationType operation, AiCoreOp opSpec)
        {
            if (!string.IsNullOrWhiteSpace(opSpec.Name))
            {
                operation.Name = opSpec.Name;
            }
            if (!string.IsNullOrWhiteSpace(opSpec.Note))
            {
                operation.Note = opSpec.Note;
            }
            if (opSpec.Disabled.HasValue)
            {
                operation.Disable = opSpec.Disabled.Value;
            }
            if (opSpec.Breakpoint.HasValue)
            {
                operation.isStopPoint = opSpec.Breakpoint.Value;
            }
            if (opSpec.Alarm != null)
            {
                if (!string.IsNullOrWhiteSpace(opSpec.Alarm.Type))
                {
                    operation.AlarmType = opSpec.Alarm.Type;
                }
                if (!string.IsNullOrWhiteSpace(opSpec.Alarm.AlarmInfoId))
                {
                    operation.AlarmInfoID = opSpec.Alarm.AlarmInfoId;
                }
                if (!string.IsNullOrWhiteSpace(opSpec.Alarm.Goto1))
                {
                    operation.Goto1 = opSpec.Alarm.Goto1;
                }
                if (!string.IsNullOrWhiteSpace(opSpec.Alarm.Goto2))
                {
                    operation.Goto2 = opSpec.Alarm.Goto2;
                }
                if (!string.IsNullOrWhiteSpace(opSpec.Alarm.Goto3))
                {
                    operation.Goto3 = opSpec.Alarm.Goto3;
                }
            }
        }

        private static void ApplyPauseConfig(ProcHead head, AiCoreProc procSpec, List<AiFlowIssue> issues, string procLoc)
        {
            if (head == null || procSpec == null)
            {
                return;
            }

            if (procSpec.PauseIo != null)
            {
                head.PauseIoCount = procSpec.PauseIo.Count.ToString();
                head.PauseIoParams = new OperationTypePartial.CustomList<PauseIoParam>();
                for (int i = 0; i < procSpec.PauseIo.Count; i++)
                {
                    string name = procSpec.PauseIo[i];
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        issues.Add(new AiFlowIssue("PAUSE_IO_EMPTY", $"暂停IO名称为空(序号:{i + 1})", procLoc));
                        continue;
                    }
                    head.PauseIoParams.Add(new PauseIoParam { IOName = name });
                }
            }

            if (procSpec.PauseValue != null)
            {
                head.PauseValueCount = procSpec.PauseValue.Count.ToString();
                head.PauseValueParams = new OperationTypePartial.CustomList<PauseValueParam>();
                for (int i = 0; i < procSpec.PauseValue.Count; i++)
                {
                    string name = procSpec.PauseValue[i];
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        issues.Add(new AiFlowIssue("PAUSE_VALUE_EMPTY", $"暂停变量名称为空(序号:{i + 1})", procLoc));
                        continue;
                    }
                    head.PauseValueParams.Add(new PauseValueParam { ValueName = name });
                }
            }
        }

        private static void ValidateAndPopulateArgs(OperationType operation, AiCoreOp opSpec, string opLoc, List<AiFlowIssue> issues)
        {
            if (operation == null || opSpec == null)
            {
                return;
            }
            if (opSpec.Args == null)
            {
                return;
            }

            int issueCountBefore = issues.Count;
            ValidateCountArgsNotProvided(operation, opSpec, opLoc, issues);
            var props = GetWritablePropertyNames(operation.GetType());
            foreach (JProperty prop in opSpec.Args.Properties())
            {
                if (ReservedOpArgs.Contains(prop.Name))
                {
                    issues.Add(new AiFlowIssue("OP_ARGS_RESERVED", $"args 包含保留字段:{prop.Name}", opLoc));
                    continue;
                }
                if (!props.Contains(prop.Name))
                {
                    issues.Add(new AiFlowIssue("OP_ARGS_UNKNOWN", $"args 字段不存在:{prop.Name}", opLoc));
                }
            }
            if (issues.Count > issueCountBefore)
            {
                return;
            }
            try
            {
                using (var reader = opSpec.Args.CreateReader())
                {
                    StrictSerializer.Populate(reader, operation);
                }
            }
            catch (JsonException ex)
            {
                issues.Add(new AiFlowIssue("OP_ARGS_PARSE", ex.Message, opLoc));
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("OP_ARGS_ERROR", ex.Message, opLoc));
            }
        }

        private static void NormalizeCounts(OperationType operation, string opLoc, List<AiFlowIssue> issues)
        {
            ApplyCountRule(operation, "IoParams", "IOCount", opLoc, issues);
            ApplyCountRule(operation, "procParams", "ProcCount", opLoc, issues);
            ApplyCountRule(operation, "Params", "Count", opLoc, issues);
            ApplyCountRule(operation, "Params", "ProcCount", opLoc, issues);
        }

        private static void ApplyCountRule(OperationType operation, string listPropName, string countPropName, string opLoc, List<AiFlowIssue> issues)
        {
            if (operation == null)
            {
                return;
            }
            Type type = operation.GetType();
            PropertyInfo listProp = type.GetProperty(listPropName, BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo countProp = type.GetProperty(countPropName, BindingFlags.Public | BindingFlags.Instance);
            if (listProp == null || countProp == null)
            {
                return;
            }
            if (!typeof(IList).IsAssignableFrom(listProp.PropertyType))
            {
                return;
            }
            if (countProp.PropertyType != typeof(string))
            {
                return;
            }

            IList list = listProp.GetValue(operation) as IList;
            string countValue = countProp.GetValue(operation) as string;

            if (list == null)
            {
                if (!string.IsNullOrWhiteSpace(countValue)
                    && int.TryParse(countValue, out int count)
                    && count > 0)
                {
                    issues.Add(new AiFlowIssue("COUNT_LIST_MISSING", $"{listPropName} 为空但 {countPropName}={count}", opLoc));
                    return;
                }
                listProp.SetValue(operation, Activator.CreateInstance(listProp.PropertyType));
                list = listProp.GetValue(operation) as IList;
            }

            int listCount = list?.Count ?? 0;
            if (string.IsNullOrWhiteSpace(countValue))
            {
                countProp.SetValue(operation, listCount.ToString());
                return;
            }
            if (!int.TryParse(countValue, out int parsed))
            {
                issues.Add(new AiFlowIssue("COUNT_INVALID", $"{countPropName} 无法解析:{countValue}", opLoc));
                return;
            }
            if (parsed != listCount)
            {
                issues.Add(new AiFlowIssue("COUNT_MISMATCH", $"{countPropName}={parsed} 与 {listPropName}.Count={listCount} 不一致", opLoc));
            }
        }

        private static void ValidateCountArgsNotProvided(OperationType operation, AiCoreOp opSpec, string opLoc, List<AiFlowIssue> issues)
        {
            if (operation == null || opSpec?.Args == null)
            {
                return;
            }
            Type type = operation.GetType();
            ValidateCountField(type, opSpec.Args, "IoParams", "IOCount", opLoc, issues);
            ValidateCountField(type, opSpec.Args, "procParams", "ProcCount", opLoc, issues);
            ValidateCountField(type, opSpec.Args, "Params", "Count", opLoc, issues);
            ValidateCountField(type, opSpec.Args, "Params", "ProcCount", opLoc, issues);
        }

        private static void ValidateCountField(Type type, JObject args, string listPropName, string countPropName, string opLoc, List<AiFlowIssue> issues)
        {
            PropertyInfo listProp = type.GetProperty(listPropName, BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo countProp = type.GetProperty(countPropName, BindingFlags.Public | BindingFlags.Instance);
            if (listProp == null || countProp == null)
            {
                return;
            }
            if (!typeof(IList).IsAssignableFrom(listProp.PropertyType))
            {
                return;
            }
            if (countProp.PropertyType != typeof(string))
            {
                return;
            }
            if (args.Property(countPropName) != null)
            {
                issues.Add(new AiFlowIssue("COUNT_FIELD_FORBIDDEN", $"禁止直接设置 {countPropName}", opLoc));
            }
        }


        private static void ValidateAlarmType(OperationType operation, string opLoc, List<AiFlowIssue> issues)
        {
            if (operation == null)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(operation.AlarmType))
            {
                issues.Add(new AiFlowIssue("ALARM_TYPE_EMPTY", "报警类型为空", opLoc));
                return;
            }
            if (!AlarmTypes.Contains(operation.AlarmType))
            {
                issues.Add(new AiFlowIssue("ALARM_TYPE_INVALID", $"报警类型无效:{operation.AlarmType}", opLoc));
            }
        }

        private static void AssignOperationNumbers(List<Proc> procs)
        {
            if (procs == null)
            {
                return;
            }
            for (int i = 0; i < procs.Count; i++)
            {
                Proc proc = procs[i];
                if (proc?.steps == null)
                {
                    continue;
                }
                for (int j = 0; j < proc.steps.Count; j++)
                {
                    Step step = proc.steps[j];
                    if (step?.Ops == null)
                    {
                        continue;
                    }
                    for (int k = 0; k < step.Ops.Count; k++)
                    {
                        step.Ops[k].Num = k;
                    }
                }
            }
        }

        private static void ValidateGotoTargets(List<Proc> procs, List<AiFlowIssue> issues)
        {
            if (procs == null)
            {
                return;
            }
            for (int procIndex = 0; procIndex < procs.Count; procIndex++)
            {
                Proc proc = procs[procIndex];
                if (proc?.steps == null)
                {
                    continue;
                }
                for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
                {
                    Step step = proc.steps[stepIndex];
                    if (step?.Ops == null)
                    {
                        continue;
                    }
                    for (int opIndex = 0; opIndex < step.Ops.Count; opIndex++)
                    {
                        OperationType op = step.Ops[opIndex];
                        if (op == null)
                        {
                            continue;
                        }
                        string opLoc = $"proc:{procIndex}/step:{stepIndex}/op:{opIndex}";
                        foreach (PropertyInfo prop in GetGotoProperties(op.GetType()))
                        {
                            string value = prop.GetValue(op) as string;
                            if (string.IsNullOrWhiteSpace(value))
                            {
                                continue;
                            }
                            if (!TryParseGoto(value, out int gotoProc, out int gotoStep, out int gotoOp))
                            {
                                issues.Add(new AiFlowIssue("GOTO_FORMAT", $"跳转格式无效:{value}", opLoc));
                                continue;
                            }
                            if (gotoProc != procIndex)
                            {
                                issues.Add(new AiFlowIssue("GOTO_CROSS_PROC", $"跨流程跳转:{value}", opLoc));
                                continue;
                            }
                            if (gotoStep < 0 || gotoStep >= proc.steps.Count)
                            {
                                issues.Add(new AiFlowIssue("GOTO_STEP_RANGE", $"跳转步骤超界:{value}", opLoc));
                                continue;
                            }
                            Step targetStep = proc.steps[gotoStep];
                            if (targetStep?.Ops == null || gotoOp < 0 || gotoOp >= targetStep.Ops.Count)
                            {
                                issues.Add(new AiFlowIssue("GOTO_OP_RANGE", $"跳转操作超界:{value}", opLoc));
                            }
                        }
                    }
                }
            }
        }

        private static OperationType CreateOperation(string opCode, string opLoc, List<AiFlowIssue> issues)
        {
            if (!OpTypeMap.TryGetValue(opCode, out Type opType))
            {
                issues.Add(new AiFlowIssue("OP_CODE_UNKNOWN", $"未知 opCode:{opCode}", opLoc));
                return null;
            }
            try
            {
                return Activator.CreateInstance(opType) as OperationType;
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("OP_CREATE_FAIL", ex.Message, opLoc));
                return null;
            }
        }

        private static Dictionary<string, Type> BuildOpTypeMap()
        {
            var map = new Dictionary<string, Type>(StringComparer.Ordinal);
            Type baseType = typeof(OperationType);
            foreach (Type type in baseType.Assembly.GetTypes())
            {
                if (type == null || type.IsAbstract)
                {
                    continue;
                }
                if (!baseType.IsAssignableFrom(type))
                {
                    continue;
                }
                if (map.ContainsKey(type.Name))
                {
                    throw new InvalidOperationException($"重复的操作类型名:{type.Name}");
                }
                map[type.Name] = type;
            }
            return map;
        }

        private static HashSet<string> GetWritablePropertyNames(Type type)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite)
                {
                    continue;
                }
                names.Add(prop.Name);
            }
            return names;
        }

        private static IEnumerable<PropertyInfo> GetGotoProperties(Type type)
        {
            var props = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.PropertyType != typeof(string))
                {
                    continue;
                }
                if (prop.GetCustomAttributes(typeof(MarkedGotoAttribute), true).Length > 0)
                {
                    props[prop.Name] = prop;
                }
            }
            foreach (string name in new[] { "goto1", "goto2", "goto3" })
            {
                PropertyInfo prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.PropertyType == typeof(string))
                {
                    props[prop.Name] = prop;
                }
            }
            return props.Values;
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
            if (procIndex < 0 || stepIndex < 0 || opIndex < 0)
            {
                return false;
            }
            return true;
        }

        private static void WriteWorkConfig(string workPath, List<Proc> procs)
        {
            string workDir = workPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string configDir = Path.GetDirectoryName(workDir);
            if (string.IsNullOrWhiteSpace(configDir))
            {
                throw new InvalidOperationException("输出路径无效");
            }

            if (!Directory.Exists(workDir))
            {
                Directory.CreateDirectory(workDir);
            }

            string tempDir = Path.Combine(configDir, "Work_tmp");
            string backupDir = Path.Combine(configDir, "Work_bak");

            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
            Directory.CreateDirectory(tempDir);

            string tempPath = tempDir + Path.DirectorySeparatorChar;
            for (int i = 0; i < procs.Count; i++)
            {
                SaveAsJson(tempPath, i.ToString(), procs[i]);
            }

            try
            {
                if (Directory.Exists(backupDir))
                {
                    Directory.Delete(backupDir, true);
                }
                if (Directory.Exists(workDir))
                {
                    Directory.Move(workDir, backupDir);
                }
                Directory.Move(tempDir, workDir);
                if (Directory.Exists(backupDir))
                {
                    Directory.Delete(backupDir, true);
                }
            }
            catch
            {
                if (!Directory.Exists(workDir) && Directory.Exists(backupDir))
                {
                    try
                    {
                        Directory.Move(backupDir, workDir);
                    }
                    catch
                    {
                    }
                }
                throw;
            }
        }

        private static void SaveAsJson<T>(string filePath, string name, T data)
        {
            string file = filePath + name + ".json";
            string directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            };
            string output = JsonConvert.SerializeObject(data, settings);

            string tempPath = file + ".tmp";
            File.WriteAllText(tempPath, output);
            if (File.Exists(file))
            {
                File.Replace(tempPath, file, null);
            }
            else
            {
                File.Move(tempPath, file);
            }
        }

        private static string BuildProcLocation(AiCoreProc proc, int procIndex)
        {
            if (proc == null)
            {
                return $"proc[{procIndex}]";
            }
            return string.IsNullOrWhiteSpace(proc.Id) ? $"proc[{procIndex}]" : $"proc:{proc.Id}";
        }

        private static string BuildStepLocation(AiCoreProc proc, int procIndex, AiCoreStep step, int stepIndex)
        {
            string procLoc = BuildProcLocation(proc, procIndex);
            if (step == null)
            {
                return $"{procLoc}/step[{stepIndex}]";
            }
            return string.IsNullOrWhiteSpace(step.Id) ? $"{procLoc}/step[{stepIndex}]" : $"{procLoc}/step:{step.Id}";
        }

        private static string BuildOpLocation(AiCoreProc proc, int procIndex, AiCoreStep step, int stepIndex, AiCoreOp op, int opIndex)
        {
            string stepLoc = BuildStepLocation(proc, procIndex, step, stepIndex);
            if (op == null)
            {
                return $"{stepLoc}/op[{opIndex}]";
            }
            return string.IsNullOrWhiteSpace(op.Id) ? $"{stepLoc}/op[{opIndex}]" : $"{stepLoc}/op:{op.Id}";
        }
    }
}
