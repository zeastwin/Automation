using System.Collections.Generic;
using Newtonsoft.Json;

namespace Automation.AIFlow
{
    public sealed class AiFlowScenario
    {
        [JsonProperty("version", Required = Required.Always)]
        public string Version { get; set; }

        [JsonProperty("start")]
        public AiFlowScenarioStart Start { get; set; }

        [JsonProperty("valuesByName")]
        public Dictionary<string, string> ValuesByName { get; set; }

        [JsonProperty("valuesByIndex")]
        public Dictionary<string, string> ValuesByIndex { get; set; }

        [JsonProperty("decisions")]
        public Dictionary<string, AiFlowScenarioDecision> Decisions { get; set; }

        [JsonProperty("allowUnsupported")]
        public bool? AllowUnsupported { get; set; }
    }

    public sealed class AiFlowScenarioStart
    {
        [JsonProperty("procId")]
        public string ProcId { get; set; }

        [JsonProperty("stepId")]
        public string StepId { get; set; }

        [JsonProperty("opId")]
        public string OpId { get; set; }

        [JsonProperty("procIndex")]
        public int? ProcIndex { get; set; }

        [JsonProperty("stepIndex")]
        public int? StepIndex { get; set; }

        [JsonProperty("opIndex")]
        public int? OpIndex { get; set; }
    }

    public sealed class AiFlowScenarioDecision
    {
        [JsonProperty("type", Required = Required.Always)]
        public string Type { get; set; }

        [JsonProperty("target")]
        public string Target { get; set; }

        [JsonProperty("result")]
        public bool? Result { get; set; }

        [JsonProperty("note")]
        public string Note { get; set; }
    }
}
