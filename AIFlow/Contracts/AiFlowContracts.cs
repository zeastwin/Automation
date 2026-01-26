using System.Collections.Generic;
using Newtonsoft.Json;

namespace Automation.AIFlow
{
    public sealed class AiFlowContractSet
    {
        [JsonProperty("version", Required = Required.Always)]
        public string Version { get; set; }

        [JsonProperty("contracts", Required = Required.Always)]
        public List<AiFlowContract> Contracts { get; set; }
    }

    public sealed class AiFlowContract
    {
        [JsonProperty("id", Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("type", Required = Required.Always)]
        public string Type { get; set; }

        [JsonProperty("commandProcId", Required = Required.Always)]
        public string CommandProcId { get; set; }

        [JsonProperty("ackProcId", Required = Required.Always)]
        public string AckProcId { get; set; }

        [JsonProperty("cmdValueName", Required = Required.Always)]
        public string CmdValueName { get; set; }

        [JsonProperty("ackValueName", Required = Required.Always)]
        public string AckValueName { get; set; }

        [JsonProperty("timeoutMs")]
        public int? TimeoutMs { get; set; }

        [JsonProperty("pollMs")]
        public int? PollMs { get; set; }
    }
}
