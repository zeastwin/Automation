using Automation.Protocol;
using Automation.DeviceSdk;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// 模块：编辑器 / Machine Agent 智能交互。
// 职责范围：维护独立 Goose 会话、最终对话历史与前台执行确认；不复用原 AI 助手会话和能力切换状态。

namespace Automation
{
    public sealed partial class FrmMachineAgent
    {
        private readonly List<MachineAgentConversationMessage> agentMessages =
            new List<MachineAgentConversationMessage>();
        private GooseAcpClient agentClient;
        private CancellationTokenSource agentCancellation;
        private bool agentBusy;
        private bool agentHistoryLoaded;
        private bool agentSessionPrompted;
        private bool agentStreamHasContent;
        private JObject activeExecutionPreview;

        private void InitializeAgentSurface()
        {
            if (!agentHistoryLoaded)
            {
                agentHistoryLoaded = true;
                try
                {
                    agentMessages.AddRange(MachineAgentConversationStorage.Load());
                }
                catch (Exception ex)
                {
                    Workspace.Info?.PrintInfo(
                        "Machine Agent 独立会话历史读取失败：" + ex.Message,
                        FrmInfo.Level.Error);
                }
            }
            PostMessage(new JObject
            {
                ["type"] = "agentBootstrap",
                ["messages"] = new JArray(agentMessages.Select(item => new JObject
                {
                    ["role"] = item.Role,
                    ["text"] = item.Text,
                    ["timeUtc"] = item.TimeUtc.ToString("O")
                })),
                ["busy"] = agentBusy
            });
        }

        private async Task SendAgentMessageAsync(string text)
        {
            string userText = (text ?? string.Empty).Trim();
            if (userText.Length == 0 || agentBusy) return;
            if (!Workspace.Runtime.Accounts.Authorize(
                PlatformPermissionCodes.PlatformAiUse,
                "使用Machine Agent分析设备",
                out string permissionError))
            {
                PostAgentError(permissionError);
                return;
            }

            var userMessage = new MachineAgentConversationMessage
            {
                Role = "user",
                Text = userText,
                TimeUtc = DateTime.UtcNow
            };
            agentMessages.Add(userMessage);
            SaveAgentMessages();
            PostMessage(new JObject
            {
                ["type"] = "agentUser",
                ["text"] = userText,
                ["timeUtc"] = userMessage.TimeUtc.ToString("O")
            });

            agentBusy = true;
            agentStreamHasContent = false;
            agentCancellation = new CancellationTokenSource();
            PostAgentStatus(true, "正在理解设备现场…", "working");
            try
            {
                await EnsureAgentClientAsync(agentCancellation.Token).ConfigureAwait(true);
                string machinePrompt = BuildMachineAgentUserPrompt(userText);
                agentSessionPrompted = true;
                await agentClient.PromptAsync(
                    machinePrompt,
                    Array.Empty<GooseFileAttachment>(),
                    agentCancellation.Token,
                    "Machine Agent：" + userText).ConfigureAwait(true);
                string finalText = (agentClient.LastAssistantResponse ?? string.Empty).Trim();
                if (finalText.Length == 0)
                    finalText = agentStreamHasContent ? string.Empty : "本次没有形成可展示的结论。";
                if (finalText.Length > 0)
                {
                    agentMessages.Add(new MachineAgentConversationMessage
                    {
                        Role = "assistant",
                        Text = finalText,
                        TimeUtc = DateTime.UtcNow
                    });
                    SaveAgentMessages();
                }
                PostMessage(new JObject
                {
                    ["type"] = "agentComplete",
                    ["text"] = agentStreamHasContent ? string.Empty : finalText
                });
                PostAgentStatus(false, activeExecutionPreview == null
                    ? "分析完成"
                    : "预演已生成 · 等待前台确认", "success");
            }
            catch (OperationCanceledException)
            {
                PostMessage(new JObject { ["type"] = "agentComplete", ["text"] = string.Empty });
                PostAgentStatus(false, "已停止", "neutral");
            }
            catch (Exception ex)
            {
                PostAgentError("Machine Agent 分析失败：" + ex.Message);
                Workspace.Info?.PrintInfo("Machine Agent 分析失败：" + ex, FrmInfo.Level.Error);
            }
            finally
            {
                agentCancellation?.Dispose();
                agentCancellation = null;
                agentBusy = false;
            }
        }

        private async Task EnsureAgentClientAsync(CancellationToken cancellationToken)
        {
            if (agentClient != null) return;
            if (!Workspace.Main.TryEnsureMachineAgentInfrastructureStarted(out string infrastructureError))
                throw new InvalidOperationException(infrastructureError);
            if (!GooseConfigStorage.TryGetCached(out GooseConfig stored, out string configError))
                throw new InvalidOperationException(configError);
            string mcpUri = await Workspace.Main.McpServerManager
                .EnsureMachineAgentStartedAsync(cancellationToken).ConfigureAwait(true);
            GooseConfig config = CreateMachineAgentConfig(stored, mcpUri);
            agentClient = new GooseAcpClient(Workspace.Runtime, config);
            agentClient.PermissionRequestHandler = HandleMachinePermissionRequest;
            agentClient.EventReceived += HandleAgentEvent;
        }

        internal static GooseConfig CreateMachineAgentConfig(GooseConfig source, string mcpUri)
        {
            return new GooseConfig
            {
                GooseExecutablePath = source.GooseExecutablePath,
                WorkingDirectory = source.WorkingDirectory,
                McpUri = mcpUri,
                SessionName = "machine_agent_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                Provider = source.Provider,
                Model = source.Model,
                ModelServiceId = source.ModelServiceId,
                ModelServices = GooseConfigStorage.CloneModelServices(source.ModelServices),
                Temperature = source.Temperature,
                MaxTurns = source.MaxTurns,
                MaxOutputTokens = source.MaxOutputTokens,
                ThinkingEffort = source.ThinkingEffort,
                AutoApproveMode = false,
                ToolProfile = AutomationToolProfiles.MachineAgent
            };
        }

        private string BuildMachineAgentUserPrompt(string userText)
        {
            var request = new JObject
            {
                ["type"] = "machine_agent.user_request.v1",
                ["request"] = userText ?? string.Empty
            };
            if (agentMessages.Count > 1 && !agentSessionPrompted)
            {
                int historyCount = Math.Min(8, agentMessages.Count - 1);
                int historyStart = Math.Max(0, agentMessages.Count - 1 - historyCount);
                request["recentConversationContext"] = new JArray(agentMessages
                    .Skip(historyStart)
                    .Take(historyCount)
                    .Select(item => new JObject
                    {
                        ["role"] = item.Role,
                        ["text"] = LimitText(item.Text, 900)
                    }));
            }
            return request.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string LimitText(string value, int maximum)
        {
            string text = value ?? string.Empty;
            return text.Length <= maximum ? text : text.Substring(0, maximum) + "…";
        }

        private void HandleAgentEvent(GooseAcpEvent item)
        {
            if (item == null || IsDisposed) return;
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<GooseAcpEvent>(HandleAgentEvent), item);
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }
            if (string.Equals(item.Kind, "assistant_chunk", StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(item.Text))
                {
                    agentStreamHasContent = true;
                    PostMessage(new JObject { ["type"] = "agentChunk", ["text"] = item.Text });
                }
            }
            else if (string.Equals(item.Kind, "tool_call", StringComparison.Ordinal))
            {
                PostMessage(new JObject
                {
                    ["type"] = "agentTool",
                    ["status"] = item.Text ?? "正在读取设备事实"
                });
            }
            else if (string.Equals(item.Kind, "tool_result", StringComparison.Ordinal))
            {
                PostMessage(new JObject
                {
                    ["type"] = "agentTool",
                    ["status"] = item.Text ?? "现场事实已返回"
                });
                if (TryExtractExecutionPreview(item.Raw, out JObject preview))
                {
                    activeExecutionPreview = preview;
                    PostMessage(new JObject
                    {
                        ["type"] = "agentPreview",
                        ["preview"] = preview
                    });
                }
            }
            else if (string.Equals(item.Kind, "error", StringComparison.Ordinal)
                || string.Equals(item.Kind, "stderr", StringComparison.Ordinal)
                || string.Equals(item.Kind, "exit", StringComparison.Ordinal))
            {
                PostAgentStatus(true, item.Text ?? "AI 运行异常", "danger");
            }
        }

        private static bool TryExtractExecutionPreview(JToken raw, out JObject preview)
        {
            preview = null;
            if (raw == null) return false;
            IEnumerable<JToken> tokens = raw is JContainer container
                ? new[] { raw }.Concat(container.Descendants())
                : new[] { raw };
            foreach (JToken token in tokens)
            {
                if (token is JObject obj)
                {
                    if (string.Equals(obj["contract"]?.Value<string>(),
                        "machine.process_entry.preview.v1", StringComparison.Ordinal))
                    {
                        preview = (JObject)obj.DeepClone();
                        return true;
                    }
                    if (string.Equals(obj["contract"]?.Value<string>(),
                        "machine.process_stop.preview.v1", StringComparison.Ordinal))
                    {
                        preview = (JObject)obj.DeepClone();
                        return true;
                    }
                    if (string.Equals(obj["type"]?.Value<string>(),
                        "machine.process_entry.preview", StringComparison.Ordinal)
                        && obj["data"] is JObject data)
                    {
                        preview = (JObject)data.DeepClone();
                        return true;
                    }
                    if (string.Equals(obj["type"]?.Value<string>(),
                        "machine.process_stop.preview", StringComparison.Ordinal)
                        && obj["data"] is JObject stopData)
                    {
                        preview = (JObject)stopData.DeepClone();
                        return true;
                    }
                }
                if (token.Type != JTokenType.String) continue;
                string value = token.Value<string>()?.Trim();
                if (string.IsNullOrEmpty(value) || value.Length > 1024 * 1024 || value[0] != '{') continue;
                try
                {
                    JObject parsed = JObject.Parse(value);
                    if (TryExtractExecutionPreview(parsed, out preview)) return true;
                }
                catch (Newtonsoft.Json.JsonException)
                {
                }
            }
            return false;
        }

        internal static JObject HandleMachinePermissionRequest(JObject request)
        {
            JToken toolCall = request?["toolCall"];
            string extensionName = toolCall?["_meta"]?["goose"]?["toolCall"]?["extensionName"]?.Value<string>()
                ?? toolCall?["extensionName"]?.Value<string>()
                ?? string.Empty;
            string toolName = toolCall?["_meta"]?["goose"]?["toolCall"]?["toolName"]?.Value<string>()
                ?? toolCall?["name"]?.Value<string>()
                ?? string.Empty;
            bool automationExtension = string.Equals(
                extensionName, "automation", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(extensionName))
            {
                automationExtension = HasExactAutomationToolPrefix(toolName);
            }
            string leafName = ReadToolLeafName(toolName);
            bool allowedTool = AutomationToolProfiles.GetTaskToolNames(
                    AutomationToolProfiles.MachineAgent)
                .Contains(leafName, StringComparer.Ordinal);
            return new JObject
            {
                ["outcome"] = new JObject
                {
                    ["outcome"] = automationExtension && allowedTool ? "allowed" : "cancelled"
                }
            };
        }

        private static bool HasExactAutomationToolPrefix(string toolName)
        {
            string value = (toolName ?? string.Empty).Trim();
            return value.StartsWith("automation__", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("automation/", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("automation.", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("automation:", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadToolLeafName(string toolName)
        {
            string value = (toolName ?? string.Empty).Trim();
            int doubleUnderscore = value.LastIndexOf("__", StringComparison.Ordinal);
            int slash = value.LastIndexOf('/');
            int dot = value.LastIndexOf('.');
            int colon = value.LastIndexOf(':');
            int separator = Math.Max(doubleUnderscore, Math.Max(slash, Math.Max(dot, colon)));
            return separator < 0
                ? value
                : value.Substring(separator + (separator == doubleUnderscore ? 2 : 1));
        }

        private void ExecuteAgentPreview(string previewId)
        {
            string normalized = (previewId ?? string.Empty).Trim();
            if (agentBusy)
            {
                PostAgentError("AI 仍在分析，暂不能确认执行。");
                return;
            }
            if (activeExecutionPreview == null
                || !string.Equals(activeExecutionPreview["previewId"]?.Value<string>(), normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                PostAgentError("预演已失效或不是当前预演，请重新分析。");
                return;
            }
            if (activeExecutionPreview["executable"]?.Value<bool>() != true)
            {
                PostAgentError("该预演存在阻塞项，不能执行。");
                return;
            }
            bool stopPreview = string.Equals(
                    activeExecutionPreview["contract"]?.Value<string>(),
                    "machine.process_stop.preview.v1",
                    StringComparison.Ordinal)
                || string.Equals(
                    activeExecutionPreview["actionKind"]?.Value<string>(),
                    "stop_process",
                    StringComparison.Ordinal);
            if (!stopPreview && !Workspace.Runtime.Accounts.AuthorizeApplicationOperation(
                PlatformPermissionCodes.ProcessRun,
                "通过Machine Agent确认执行流程指令",
                out string permissionError))
            {
                PostAgentError(permissionError);
                return;
            }

            JObject target = activeExecutionPreview["target"] as JObject ?? new JObject();
            string confirmationText;
            string confirmationTitle;
            if (stopPreview)
            {
                confirmationTitle = "Machine Agent · 停止确认";
                confirmationText =
                    "这是停止当前流程运行实例的设备控制动作，不是模拟。\n\n"
                    + "流程：" + (target["procName"]?.Value<string>() ?? "未命名") + "\n"
                    + "运行实例：" + (target["runId"]?.Value<string>() ?? "缺失") + "\n"
                    + "当前状态：" + (target["state"]?.Value<string>() ?? "未知") + "\n"
                    + "当前位置：步骤 " + (target["stepIndex"]?.Value<int?>()?.ToString() ?? "?")
                    + " / 指令 " + (target["opIndex"]?.Value<int?>()?.ToString() ?? "?") + "\n"
                    + "停止原因：" + (activeExecutionPreview["reason"]?.Value<string>() ?? "未填写") + "\n\n"
                    + "只会停止该预演冻结的运行实例；如果流程已经结束或切换为新 runId，执行将被拒绝。\n"
                    + "是否确认停止？";
            }
            else
            {
                JObject skill = activeExecutionPreview["skill"] as JObject ?? new JObject();
                string mode = activeExecutionPreview["mode"]?.Value<string>() ?? string.Empty;
                string modeText = string.Equals(mode, MachineExecutionModes.SingleOperation, StringComparison.Ordinal)
                    ? "只执行该指令一次"
                    : "从该指令开始并沿原流程继续";
                confirmationTitle = "Machine Agent · 执行确认";
                confirmationText =
                    "这是设备控制动作，不是模拟。\n\n"
                    + "流程：" + (target["procName"]?.Value<string>() ?? "未命名") + "\n"
                    + "步骤：" + (target["stepName"]?.Value<string>() ?? "未命名") + "\n"
                    + "指令：" + (target["operationName"]?.Value<string>()
                        ?? target["operationType"]?.Value<string>() ?? "未命名") + "\n"
                    + "模式：" + modeText + "\n"
                    + "节点技能：" + (skill["name"]?.Value<string>()
                        ?? skill["skillId"]?.Value<string>() ?? "未绑定") + "\n"
                    + "设备节点：" + (skill["nodeLabel"]?.Value<string>() ?? "未关联") + "\n"
                    + "外部作用：" + (activeExecutionPreview["operationEffect"]?.Value<string>()
                        ?? "unknown") + "\n"
                    + "动作目标：" + (activeExecutionPreview["objective"]?.Value<string>()
                        ?? "未填写") + "\n"
                    + "预期结果：" + (activeExecutionPreview["expectedOutcome"]?.Value<string>()
                        ?? "未填写") + "\n"
                    + "指令参数：\n" + FormatPreviewParameters(target["parameters"]) + "\n"
                    + FormatPreviewChecks(activeExecutionPreview) + "\n"
                    + "确认现场无人处于危险区域，并执行当前冻结预演吗？";
            }
            DialogResult confirmation = MessageBox.Show(
                confirmationText,
                confirmationTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes) return;

            try
            {
                JObject result = stopPreview
                    ? Workspace.Runtime.MachineAgent.ExecuteProcessStop(normalized)
                    : Workspace.Runtime.MachineAgent.ExecuteProcessEntry(normalized);
                activeExecutionPreview = null;
                PostMessage(new JObject
                {
                    ["type"] = "agentExecution",
                    ["ok"] = true,
                    ["message"] = stopPreview
                        ? result["timelineTracked"]?.Value<bool>() == false
                            ? "停止请求已交给流程控制器；设备时间线本次已降级，请结合流程运行日志确认最终状态。"
                            : "停止请求已交给流程控制器，最终停止结果将进入设备时间线。"
                        : "流程引擎已接受动作，运行状态将继续进入设备时间线。",
                    ["result"] = result
                });
                PublishDashboard(true);
            }
            catch (MachineAgentControlException ex)
            {
                activeExecutionPreview = null;
                PostMessage(new JObject
                {
                    ["type"] = "agentExecution",
                    ["ok"] = false,
                    ["message"] = ex.Message + " 请基于最新现场事实重新预演。"
                });
                PostAgentStatus(false, "需要重新预演", "warning");
            }
            catch (Exception ex)
            {
                PostAgentError("执行失败：" + ex.Message);
            }
        }

        private static string FormatPreviewParameters(JToken parameters)
        {
            if (!(parameters is JObject objectParameters) || !objectParameters.Properties().Any())
            {
                return "  （无）";
            }
            return string.Join("\n", objectParameters.Properties()
                .Take(12)
                .Select(property =>
                {
                    string value = property.Value?.ToString(Newtonsoft.Json.Formatting.None)
                        ?? "null";
                    if (value.Length > 160) value = value.Substring(0, 157) + "...";
                    return "  " + property.Name + " = " + value;
                }));
        }

        private static string FormatPreviewChecks(JObject preview)
        {
            var lines = new List<string>();
            foreach (JObject check in (preview["preconditionChecks"] as JArray
                ?? new JArray()).OfType<JObject>())
            {
                lines.Add(FormatPreviewCheck("技能条件", check, null));
            }
            foreach (JObject relation in (preview["relationChecks"] as JArray
                ?? new JArray()).OfType<JObject>())
            {
                lines.Add(FormatPreviewCheck(
                    "拓扑" + (relation["kind"]?.Value<string>() ?? "关系"),
                    relation["condition"] as JObject ?? new JObject(),
                    relation["blocksExecution"]?.Value<bool>() == true));
            }
            return lines.Count == 0
                ? "约束检查：无显式条件\n"
                : "约束检查：\n" + string.Join("\n", lines.Take(12)) + "\n";
        }

        private static string FormatPreviewCheck(
            string label,
            JObject check,
            bool? relationBlocked)
        {
            bool evaluable = check["evaluable"]?.Value<bool>() == true;
            bool safe = relationBlocked.HasValue
                ? !relationBlocked.Value
                : check["satisfied"]?.Value<bool>() == true;
            string mark = !evaluable ? "?" : safe ? "✓" : "×";
            string expression = check["expression"]?.Value<string>() ?? string.Empty;
            string detail = check["detail"]?.Value<string>() ?? string.Empty;
            return "  " + mark + " " + label
                + (string.IsNullOrWhiteSpace(expression) ? string.Empty : " · " + expression)
                + (string.IsNullOrWhiteSpace(detail) ? string.Empty : " — " + detail);
        }

        private void DiscardAgentPreview(string previewId)
        {
            string normalized = (previewId ?? string.Empty).Trim();
            Workspace.Runtime.MachineAgent.DiscardPreview(normalized);
            if (activeExecutionPreview != null
                && string.Equals(activeExecutionPreview["previewId"]?.Value<string>(), normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                activeExecutionPreview = null;
            }
            PostMessage(new JObject
            {
                ["type"] = "agentExecution",
                ["ok"] = false,
                ["message"] = "预演已放弃，没有执行设备动作。"
            });
        }

        private void CancelAgentRequest()
        {
            agentClient?.Cancel();
            agentCancellation?.Cancel();
        }

        private void PostAgentStatus(bool running, string status, string tone)
        {
            PostMessage(new JObject
            {
                ["type"] = "agentStatus",
                ["running"] = running,
                ["status"] = status ?? string.Empty,
                ["tone"] = tone ?? "neutral"
            });
        }

        private void PostAgentError(string message)
        {
            PostMessage(new JObject
            {
                ["type"] = "agentError",
                ["message"] = message ?? "Machine Agent 发生未知错误。"
            });
            PostAgentStatus(false, "需要处理", "danger");
        }

        private void SaveAgentMessages()
        {
            try
            {
                MachineAgentConversationStorage.Save(agentMessages);
            }
            catch (Exception ex)
            {
                Workspace.Info?.PrintInfo(
                    "Machine Agent 独立会话历史保存失败：" + ex.Message,
                    FrmInfo.Level.Error);
            }
        }

        internal void DisposeGooseClient()
        {
            CancelAgentRequest();
            if (agentClient != null)
            {
                agentClient.EventReceived -= HandleAgentEvent;
                agentClient.Dispose();
                agentClient = null;
            }
            agentCancellation?.Dispose();
            agentCancellation = null;
        }

        private void DisposeAgentResources()
        {
            DisposeGooseClient();
            Workspace.Main?.McpServerManager?.StopMachineAgent();
        }
    }
}
