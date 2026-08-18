using Newtonsoft.Json;
// 模块：Bridge / 服务。
// 职责范围：AI headless 回归测试的管道触发与状态查询；执行体复用 FrmAiAssistant 共享 headless 执行器。

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation.Bridge
{
    internal sealed partial class AutomationBridgeService
    {
        // 管道触发 headless 测试：本机 Named Pipe 信任模型与 /bridge/proc/start 一致；
        // autoApprove 必须显式传 true，调用方明示接受自动批准产生的真实配置写入。
        private JObject HandleStartAiTest(JObject request)
        {
            if (request["autoApprove"]?.Value<bool>() != true)
            {
                throw new BridgeRequestException(400, "AI_TEST_AUTO_APPROVE_REQUIRED",
                    "headless 测试必须显式传 autoApprove=true；该模式会自动确认预演并产生真实配置写入。");
            }
            var prompts = (request["prompts"] as JArray)?.Values<string>().ToList();
            string promptError = HeadlessAiTestOptions.ValidatePrompts(prompts);
            if (promptError != null)
            {
                throw new BridgeRequestException(400, "AI_TEST_PROMPTS_INVALID", promptError);
            }
            int timeoutMinutes = request["turnTimeoutMinutes"]?.Value<int>()
                ?? HeadlessAiTestOptions.DefaultTurnTimeoutMinutes;
            if (timeoutMinutes < 1 || timeoutMinutes > 120)
            {
                throw new BridgeRequestException(400, "AI_TEST_PROMPTS_INVALID",
                    "turnTimeoutMinutes 必须在 1..120。");
            }
            FrmMain main = owner;
            FrmAiAssistant assistant = main?.frmAiAssistant;
            if (main == null || assistant == null)
            {
                throw new BridgeRequestException(503, "AI_TEST_EDITOR_UNAVAILABLE",
                    "平台编辑器或 AI 助手尚未初始化；HMI 模式请等待隐藏预加载完成后重试。");
            }
            if (assistant.HeadlessRunState.Running)
            {
                throw new BridgeRequestException(409, "AI_TEST_ALREADY_RUNNING",
                    "已有 headless 测试在运行；先用 /bridge/ai-test/status 查询完成后再启动。");
            }
            if (assistant.HeadlessEditorBusy)
            {
                throw new BridgeRequestException(409, "AI_TEST_EDITOR_BUSY",
                    "AI 助手当前有任务在执行，请等待完成后再触发 headless 测试。");
            }
            main.EnsureAiInfrastructureStarted();
            if (!assistant.PrepareHeadlessTest(out string prepareError))
            {
                throw new BridgeRequestException(503, "AI_TEST_PREPARE_FAILED", prepareError);
            }
            // 在 UI 线程 fire-and-forget 启动；执行器内部全异常捕获并维护状态。
            _ = assistant.RunHeadlessPromptsAsync(prompts, timeoutMinutes, "bridge_pipe");
            return new JObject
            {
                ["started"] = true,
                ["runId"] = assistant.HeadlessRunState.RunId,
                ["totalPrompts"] = prompts.Count,
                ["turnTimeoutMinutes"] = timeoutMinutes
            };
        }

        private JObject HandleAiTestStatus()
        {
            FrmAiAssistant assistant = owner?.frmAiAssistant;
            if (assistant == null)
            {
                throw new BridgeRequestException(503, "AI_TEST_EDITOR_UNAVAILABLE",
                    "平台编辑器或 AI 助手尚未初始化。");
            }
            return assistant.HeadlessRunState.ToJson();
        }
    }
}
