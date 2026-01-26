using System.Collections.Generic;
using Newtonsoft.Json;

namespace Automation.AIFlow
{
    public sealed class AiFlowTrace
    {
        [JsonProperty("version")]
        public string Version { get; set; } = "trace-1";

        [JsonProperty("events")]
        public List<AiFlowTraceEvent> Events { get; set; } = new List<AiFlowTraceEvent>();

        [JsonProperty("issues")]
        public List<AiFlowIssue> Issues { get; set; } = new List<AiFlowIssue>();
    }

    public sealed class AiFlowTraceEvent
    {
        [JsonProperty("seq")]
        public int Sequence { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("procId")]
        public string ProcId { get; set; }

        [JsonProperty("stepId")]
        public string StepId { get; set; }

        [JsonProperty("opId")]
        public string OpId { get; set; }

        [JsonProperty("opCode")]
        public string OpCode { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data")]
        public string Data { get; set; }
    }
}
