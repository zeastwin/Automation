using System.Collections.Generic;
using Automation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation.AIFlow
{
    public sealed class AiSpecFlow
    {
        [JsonProperty("version", Required = Required.Always)]
        public string Version { get; set; }

        [JsonProperty("kind", Required = Required.Always)]
        public string Kind { get; set; }

        [JsonProperty("core", Required = Required.Always)]
        public AiCoreFlow Core { get; set; }
    }

    public sealed class AiCoreFlow
    {
        [JsonProperty("version", Required = Required.Always)]
        public string Version { get; set; }

        [JsonProperty("procs", Required = Required.Always)]
        public List<AiCoreProc> Procs { get; set; }
    }

    public sealed class AiCoreProc
    {
        [JsonProperty("id", Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; }

        [JsonProperty("autoStart")]
        public bool? AutoStart { get; set; }

        [JsonProperty("pauseIo")]
        public List<string> PauseIo { get; set; }

        [JsonProperty("pauseValue")]
        public List<string> PauseValue { get; set; }

        [JsonProperty("steps", Required = Required.Always)]
        public List<AiCoreStep> Steps { get; set; }
    }

    public sealed class AiCoreStep
    {
        [JsonProperty("id", Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; }

        [JsonProperty("ops", Required = Required.Always)]
        public List<AiCoreOp> Ops { get; set; }
    }

    public sealed class AiCoreOp
    {
        [JsonProperty("id", Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("opCode", Required = Required.Always)]
        public string OpCode { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("disabled")]
        public bool? Disabled { get; set; }

        [JsonProperty("breakpoint")]
        public bool? Breakpoint { get; set; }

        [JsonProperty("note")]
        public string Note { get; set; }

        [JsonProperty("alarm")]
        public AiCoreAlarm Alarm { get; set; }

        [JsonProperty("args")]
        public JObject Args { get; set; }
    }

    public sealed class AiCoreAlarm
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("alarmInfoId")]
        public string AlarmInfoId { get; set; }

        [JsonProperty("goto1")]
        public string Goto1 { get; set; }

        [JsonProperty("goto2")]
        public string Goto2 { get; set; }

        [JsonProperty("goto3")]
        public string Goto3 { get; set; }
    }

    public sealed class AiFlowIssue
    {
        public AiFlowIssue(string code, string message, string location)
        {
            Code = code;
            Message = message;
            Location = location;
        }

        public string Code { get; }
        public string Message { get; }
        public string Location { get; }
    }

    public sealed class AiFlowCompileResult
    {
        public AiFlowCompileResult(List<AiFlowIssue> issues, List<Proc> procs)
        {
            Issues = issues ?? new List<AiFlowIssue>();
            Procs = procs ?? new List<Proc>();
        }

        public List<AiFlowIssue> Issues { get; }
        public List<Proc> Procs { get; }

        public bool Success => Issues.Count == 0;
    }
}
