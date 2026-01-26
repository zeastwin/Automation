using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Automation.AIFlow
{
    public static class AiFlowIo
    {
        private static readonly JsonSerializerSettings StrictSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include,
            TypeNameHandling = TypeNameHandling.None,
            DateParseHandling = DateParseHandling.None
        };

        public static AiCoreFlow ReadCore(string path, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(new AiFlowIssue("IO_PATH_EMPTY", "core 路径为空", "io"));
                return null;
            }
            if (!File.Exists(path))
            {
                issues.Add(new AiFlowIssue("IO_NOT_FOUND", $"core 文件不存在:{path}", "io"));
                return null;
            }
            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<AiCoreFlow>(json, StrictSettings);
            }
            catch (JsonException ex)
            {
                issues.Add(new AiFlowIssue("IO_PARSE_ERROR", ex.Message, "io"));
                return null;
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("IO_READ_ERROR", ex.Message, "io"));
                return null;
            }
        }

        public static AiSpecFlow ReadSpec(string path, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(new AiFlowIssue("IO_PATH_EMPTY", "spec 路径为空", "io"));
                return null;
            }
            if (!File.Exists(path))
            {
                issues.Add(new AiFlowIssue("IO_NOT_FOUND", $"spec 文件不存在:{path}", "io"));
                return null;
            }
            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<AiSpecFlow>(json, StrictSettings);
            }
            catch (JsonException ex)
            {
                issues.Add(new AiFlowIssue("IO_PARSE_ERROR", ex.Message, "io"));
                return null;
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("IO_READ_ERROR", ex.Message, "io"));
                return null;
            }
        }

        public static AiFlowDelta ReadDelta(string path, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(new AiFlowIssue("IO_PATH_EMPTY", "delta 路径为空", "io"));
                return null;
            }
            if (!File.Exists(path))
            {
                issues.Add(new AiFlowIssue("IO_NOT_FOUND", $"delta 文件不存在:{path}", "io"));
                return null;
            }
            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<AiFlowDelta>(json, StrictSettings);
            }
            catch (JsonException ex)
            {
                issues.Add(new AiFlowIssue("IO_PARSE_ERROR", ex.Message, "io"));
                return null;
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("IO_READ_ERROR", ex.Message, "io"));
                return null;
            }
        }

        public static AiFlowScenario ReadScenario(string path, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(new AiFlowIssue("IO_PATH_EMPTY", "scenario 路径为空", "io"));
                return null;
            }
            if (!File.Exists(path))
            {
                issues.Add(new AiFlowIssue("IO_NOT_FOUND", $"scenario 文件不存在:{path}", "io"));
                return null;
            }
            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<AiFlowScenario>(json, StrictSettings);
            }
            catch (JsonException ex)
            {
                issues.Add(new AiFlowIssue("IO_PARSE_ERROR", ex.Message, "io"));
                return null;
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("IO_READ_ERROR", ex.Message, "io"));
                return null;
            }
        }

        public static AiFlowContractSet ReadContracts(string path, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(new AiFlowIssue("IO_PATH_EMPTY", "contract 路径为空", "io"));
                return null;
            }
            if (!File.Exists(path))
            {
                issues.Add(new AiFlowIssue("IO_NOT_FOUND", $"contract 文件不存在:{path}", "io"));
                return null;
            }
            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<AiFlowContractSet>(json, StrictSettings);
            }
            catch (JsonException ex)
            {
                issues.Add(new AiFlowIssue("IO_PARSE_ERROR", ex.Message, "io"));
                return null;
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("IO_READ_ERROR", ex.Message, "io"));
                return null;
            }
        }

        public static void WriteCore(string path, AiCoreFlow core, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(new AiFlowIssue("IO_PATH_EMPTY", "core 输出路径为空", "io"));
                return;
            }
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, JsonConvert.SerializeObject(core, Formatting.Indented));
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("IO_WRITE_ERROR", ex.Message, "io"));
            }
        }

        public static void WriteSpec(string path, AiSpecFlow spec, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(new AiFlowIssue("IO_PATH_EMPTY", "spec 输出路径为空", "io"));
                return;
            }
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, JsonConvert.SerializeObject(spec, Formatting.Indented));
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("IO_WRITE_ERROR", ex.Message, "io"));
            }
        }

        public static void WriteDiff(string path, AiFlowDiffResult diff, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(new AiFlowIssue("IO_PATH_EMPTY", "diff 输出路径为空", "io"));
                return;
            }
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, JsonConvert.SerializeObject(diff, Formatting.Indented));
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("IO_WRITE_ERROR", ex.Message, "io"));
            }
        }

        public static void WriteTrace(string path, AiFlowTrace trace, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(new AiFlowIssue("IO_PATH_EMPTY", "trace 输出路径为空", "io"));
                return;
            }
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, JsonConvert.SerializeObject(trace, Formatting.Indented));
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("IO_WRITE_ERROR", ex.Message, "io"));
            }
        }

        public static void PrintDiff(AiFlowDiffResult diff)
        {
            if (diff == null)
            {
                Console.WriteLine("diff 为空");
                return;
            }
            PrintList("AddedProcs", diff.AddedProcs);
            PrintList("RemovedProcs", diff.RemovedProcs);
            PrintList("ModifiedProcs", diff.ModifiedProcs);
            PrintList("AddedSteps", diff.AddedSteps);
            PrintList("RemovedSteps", diff.RemovedSteps);
            PrintList("ModifiedSteps", diff.ModifiedSteps);
            PrintList("AddedOps", diff.AddedOps);
            PrintList("RemovedOps", diff.RemovedOps);
            PrintList("ModifiedOps", diff.ModifiedOps);
        }

        private static void PrintList(string title, List<string> items)
        {
            Console.WriteLine($"{title}: {items?.Count ?? 0}");
            if (items == null)
            {
                return;
            }
            foreach (string item in items)
            {
                Console.WriteLine($"  - {item}");
            }
        }
    }
}
