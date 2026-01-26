using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Automation;

namespace Automation.AIFlow
{
    public static class AiFlowDecompiler
    {
        private static readonly HashSet<string> ReservedFields = new HashSet<string>(StringComparer.Ordinal)
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

        private static readonly HashSet<string> CountFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "Count",
            "IOCount",
            "ProcCount"
        };

        public static AiCoreFlow DecompileWork(string workDir, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            if (string.IsNullOrWhiteSpace(workDir))
            {
                issues.Add(new AiFlowIssue("DECOMP_PATH_EMPTY", "Work 路径为空", "decompile"));
                return null;
            }
            string path = workDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(path))
            {
                issues.Add(new AiFlowIssue("DECOMP_WORK_MISSING", $"Work 目录不存在:{path}", "decompile"));
                return null;
            }

            var indices = new List<int>();
            foreach (string file in Directory.EnumerateFiles(path, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(name, out int index))
                {
                    indices.Add(index);
                }
            }
            indices.Sort();

            var procs = new List<AiCoreProc>();
            foreach (int index in indices)
            {
                Proc proc = ReadProc(path, index.ToString(), issues);
                if (proc == null)
                {
                    continue;
                }
                procs.Add(DecompileProc(proc, index));
            }

            return new AiCoreFlow
            {
                Version = AiFlowCompiler.CoreVersion,
                Procs = procs
            };
        }

        public static AiSpecFlow BuildSpec(AiCoreFlow core)
        {
            return new AiSpecFlow
            {
                Version = AiFlowCompiler.SpecVersion,
                Kind = "core",
                Core = core
            };
        }

        private static Proc ReadProc(string workDir, string name, List<AiFlowIssue> issues)
        {
            string file = Path.Combine(workDir, name + ".json");
            if (!File.Exists(file))
            {
                return null;
            }
            try
            {
                string json = File.ReadAllText(file);
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All
                };
                return JsonConvert.DeserializeObject<Proc>(json, settings);
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("DECOMP_READ_FAIL", ex.Message, $"proc:{name}"));
                return null;
            }
        }

        private static AiCoreProc DecompileProc(Proc proc, int procIndex)
        {
            var coreProc = new AiCoreProc
            {
                Id = $"p{procIndex}",
                Name = proc.head?.Name ?? $"流程{procIndex}",
                AutoStart = proc.head?.AutoStart ?? false,
                PauseIo = proc.head?.PauseIoParams?.Select(p => p?.IOName).Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
                PauseValue = proc.head?.PauseValueParams?.Select(p => p?.ValueName).Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
                Steps = new List<AiCoreStep>()
            };

            if (proc.steps == null)
            {
                return coreProc;
            }

            for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
            {
                Step step = proc.steps[stepIndex];
                var coreStep = new AiCoreStep
                {
                    Id = $"p{procIndex}-s{stepIndex}",
                    Name = step?.Name ?? $"步骤{stepIndex}",
                    Ops = new List<AiCoreOp>()
                };

                if (step?.Ops != null)
                {
                    for (int opIndex = 0; opIndex < step.Ops.Count; opIndex++)
                    {
                        OperationType op = step.Ops[opIndex];
                        if (op == null)
                        {
                            continue;
                        }
                        coreStep.Ops.Add(DecompileOp(op, procIndex, stepIndex, opIndex));
                    }
                }
                coreProc.Steps.Add(coreStep);
            }

            return coreProc;
        }

        private static AiCoreOp DecompileOp(OperationType op, int procIndex, int stepIndex, int opIndex)
        {
            JObject args = JObject.FromObject(op, new JsonSerializer { TypeNameHandling = TypeNameHandling.None });
            foreach (string field in ReservedFields)
            {
                args.Remove(field);
            }
            foreach (string field in CountFields)
            {
                args.Remove(field);
            }

            return new AiCoreOp
            {
                Id = $"p{procIndex}-s{stepIndex}-o{opIndex}",
                OpCode = op.GetType().Name,
                Name = op.Name,
                Disabled = op.Disable,
                Breakpoint = op.isStopPoint,
                Note = op.Note,
                Alarm = BuildAlarm(op),
                Args = args
            };
        }

        private static AiCoreAlarm BuildAlarm(OperationType op)
        {
            if (op == null)
            {
                return null;
            }
            bool hasAlarm = !string.IsNullOrWhiteSpace(op.AlarmType)
                || !string.IsNullOrWhiteSpace(op.AlarmInfoID)
                || !string.IsNullOrWhiteSpace(op.Goto1)
                || !string.IsNullOrWhiteSpace(op.Goto2)
                || !string.IsNullOrWhiteSpace(op.Goto3);
            if (!hasAlarm)
            {
                return null;
            }
            return new AiCoreAlarm
            {
                Type = op.AlarmType,
                AlarmInfoId = op.AlarmInfoID,
                Goto1 = op.Goto1,
                Goto2 = op.Goto2,
                Goto3 = op.Goto3
            };
        }
    }
}
