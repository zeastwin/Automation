using Automation.Protocol;
using Newtonsoft.Json.Linq;

// 模块：Bridge / Machine Agent。
// 职责范围：向独立 Machine Agent MCP 暴露设备上下文、状态历史和无副作用的流程动作预演。

namespace Automation.Bridge
{
    internal sealed partial class AutomationBridgeService
    {
        private JObject HandleGetMachineContext(JObject request)
        {
            int eventLimit = ReadBoundedMachineInt(request, "eventLimit", 40, 1, 80);
            int nodeOffset = ReadBoundedMachineInt(request, "nodeOffset", 0, 0, 2000);
            int nodeLimit = ReadBoundedMachineInt(request, "nodeLimit", 80, 1, 200);
            int relationOffset = ReadBoundedMachineInt(request, "relationOffset", 0, 0, 5000);
            int relationLimit = ReadBoundedMachineInt(request, "relationLimit", 160, 1, 500);
            return runtime.MachineAgent.BuildContext(
                eventLimit,
                nodeOffset,
                nodeLimit,
                relationOffset,
                relationLimit);
        }

        private JObject HandleGetMachineStateHistory(JObject request)
        {
            long? afterSequence = request?["afterSequence"]?.Type == JTokenType.Null
                ? null
                : request?["afterSequence"]?.Value<long?>();
            int limit = ReadBoundedMachineInt(request, "limit", 120, 1, 500);
            return runtime.MachineAgent.BuildStateHistory(afterSequence, limit);
        }

        private JObject HandlePreviewMachineProcessEntry(JObject request)
        {
            MachineProcessEntryPreviewRequest definition;
            try
            {
                definition = request?.ToObject<MachineProcessEntryPreviewRequest>();
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                throw new BridgeRequestException(400, "MACHINE_PREVIEW_INVALID", "指令入口预演参数格式无效。", ex.Message);
            }
            try
            {
                return runtime.MachineAgent.PreviewProcessEntry(definition);
            }
            catch (MachineAgentControlException ex)
            {
                throw new BridgeRequestException(409, ex.Code, ex.Message);
            }
        }

        private JObject HandlePreviewMachineProcessStop(JObject request)
        {
            MachineProcessStopPreviewRequest definition;
            try
            {
                definition = request?.ToObject<MachineProcessStopPreviewRequest>();
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                throw new BridgeRequestException(
                    400, "MACHINE_STOP_PREVIEW_INVALID", "流程停止预演参数格式无效。", ex.Message);
            }
            try
            {
                return runtime.MachineAgent.PreviewProcessStop(definition);
            }
            catch (MachineAgentControlException ex)
            {
                throw new BridgeRequestException(409, ex.Code, ex.Message);
            }
        }

        private JObject HandleDiscardMachineProcessEntry(JObject request)
        {
            string previewId = ReadRequiredString(request, "previewId");
            return new JObject
            {
                ["previewId"] = previewId,
                ["discarded"] = runtime.MachineAgent.DiscardPreview(previewId)
            };
        }

        private static int ReadBoundedMachineInt(
            JObject request, string fieldName, int defaultValue, int minimum, int maximum)
        {
            int value = ReadOptionalInt(request, fieldName) ?? defaultValue;
            if (value < minimum || value > maximum)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT",
                    $"字段 {fieldName} 必须在 {minimum}..{maximum} 范围内。");
            }
            return value;
        }
    }
}
