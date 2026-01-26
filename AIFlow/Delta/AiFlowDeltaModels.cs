using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation.AIFlow
{
    public sealed class AiFlowDelta
    {
        [JsonProperty("version", Required = Required.Always)]
        public string Version { get; set; }

        [JsonProperty("baseRevision")]
        public string BaseRevision { get; set; }

        [JsonProperty("ops", Required = Required.Always)]
        public List<AiFlowDeltaOp> Ops { get; set; }
    }

    public sealed class AiFlowDeltaOp
    {
        [JsonProperty("type", Required = Required.Always)]
        public string Type { get; set; }

        [JsonProperty("procId")]
        public string ProcId { get; set; }

        [JsonProperty("stepId")]
        public string StepId { get; set; }

        [JsonProperty("opId")]
        public string OpId { get; set; }

        [JsonProperty("index")]
        public int? Index { get; set; }

        [JsonProperty("proc")]
        public AiCoreProc Proc { get; set; }

        [JsonProperty("step")]
        public AiCoreStep Step { get; set; }

        [JsonProperty("op")]
        public AiCoreOp Op { get; set; }

        [JsonProperty("args")]
        public JObject Args { get; set; }
    }
}
