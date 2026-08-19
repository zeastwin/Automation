using Newtonsoft.Json.Linq;
// 模块：编辑器 / AI。
// 职责范围：无人值守回归测试的 headless 前台入口；复用 FrmAiAssistant 完整能力切换与发送链路。
// 排查入口：headless 测试失败按 PrepareHeadlessTest -> EnsureAiInfrastructureStarted -> RunHeadlessPromptsAsync 顺序定位。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Automation
{
    /// <summary>headless 测试运行状态快照；命令行与管道触发共用，供调用方轮询。</summary>
    internal sealed class HeadlessAiTestRunState
    {
        public string RunId { get; internal set; } = string.Empty;
        public bool Running { get; internal set; }
        public int TotalPrompts { get; internal set; }
        public int CompletedPrompts { get; internal set; }
        public int CurrentPromptIndex { get; internal set; } = -1;
        public int PassedCount { get; internal set; }
        public int FailedCount { get; internal set; }
        public string LastPromptFailure { get; internal set; } = string.Empty;
        public string ReportPath { get; internal set; } = string.Empty;
        public string LastError { get; internal set; } = string.Empty;
        public DateTime StartedAt { get; internal set; }
        public DateTime UpdatedAt { get; internal set; } = DateTime.Now;

        public JObject ToJson()
        {
            return new JObject
            {
                ["runId"] = RunId,
                ["running"] = Running,
                ["totalPrompts"] = TotalPrompts,
                ["completedPrompts"] = CompletedPrompts,
                ["currentPromptIndex"] = CurrentPromptIndex,
                ["passedCount"] = PassedCount,
                ["failedCount"] = FailedCount,
                ["lastPromptFailure"] = LastPromptFailure,
                ["reportPath"] = ReportPath,
                ["lastError"] = LastError,
                ["startedAt"] = StartedAt == DateTime.MinValue
                    ? string.Empty
                    : StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ["updatedAt"] = UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };
        }
    }

    public sealed partial class FrmAiAssistant
    {
        /// <summary>最近一次完成的发送所产出的最终助手输出；headless 测试收集用，UI 模式无人读取。</summary>
        internal string HeadlessLastAssistantText { get; private set; } = string.Empty;

        /// <summary>最近一次失败的机械原因；headless 测试诊断用。成功发送后清空。</summary>
        internal string HeadlessLastFailure { get; private set; }

        /// <summary>headless 测试运行状态；命令行与管道触发共用。</summary>
        internal HeadlessAiTestRunState HeadlessRunState { get; } = new HeadlessAiTestRunState();

        /// <summary>
        /// headless 无人值守准备：静默加载配置填充控件、强制创建句柄（BeginInvoke 依赖）、
        /// 开启预演自动批准（Bridge 在预演创建瞬间直接标记已确认，等价于前台逐次点确认）。
        /// WebView 不初始化：全部渲染调用已有 webDocumentReady / webViewConversation 空保护。
        /// </summary>
        internal bool PrepareHeadlessTest(out string error)
        {
            error = null;
            try
            {
                LoadConfig(silentLoadFailure: true);
                if (!GooseRuntimeEnvironment.TryValidate(txtGooseExecutable.Text.Trim(), out error))
                {
                    return false;
                }
                if (!IsHandleCreated)
                {
                    // 访问 Handle 强制创建本机句柄；不显示窗口。
                    _ = Handle;
                }
                // headless 测试语句按编辑档设计（含流程创建与修改）；Diagnostic 档无写入工具面。
                if (!string.Equals(toolProfile, "Editor", StringComparison.Ordinal))
                {
                    toolProfile = "Editor";
                }
                autoApproveMode = true;
                AiAnalysisLogger.Write(new JObject
                {
                    ["event"] = "ai_headless_test.prepared",
                    ["autoApprove"] = true,
                    ["toolProfile"] = toolProfile
                });
                return true;
            }
            catch (Exception ex)
            {
                error = "headless 测试准备失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>取得或复用当前任务会话（同一会话连续对话），并清空上一次收集的输出与失败原因。</summary>
        internal AiTaskRuntime HeadlessEnsureConversation()
        {
            HeadlessLastAssistantText = string.Empty;
            HeadlessLastFailure = string.Empty;
            return EnsureActiveConversation(false);
        }

        /// <summary>当前活动任务运行时；供 headless 超时取消使用。</summary>
        internal AiTaskRuntime HeadlessActiveRuntime => ActiveTaskRuntime;

        /// <summary>AI 助手是否有任务在执行；headless 触发前的防冲突检查。</summary>
        internal bool HeadlessEditorBusy => HasRunningTasks;

        /// <summary>以 headless 方式发送一句测试语句；返回 false 表示该轮失败或被取消。</summary>
        internal async Task<bool> SendHeadlessPromptAsync(string prompt)
        {
            AiTaskRuntime runtime = HeadlessEnsureConversation();
            if (runtime == null || runtime.Running)
            {
                HeadlessLastFailure = "任务会话不可用或仍在运行。";
                return false;
            }
            return await SendPromptAsync(
                runtime,
                prompt,
                new List<GooseFileAttachment>(),
                false).ConfigureAwait(true);
        }

        /// <summary>headless 单句超时后的停止入口；已停止或已完成时无副作用。</summary>
        internal void CancelHeadlessTask(AiTaskRuntime runtime, string source)
        {
            if (runtime == null || !runtime.Running)
            {
                return;
            }
            conversationCoordinator.Cancel(runtime, source ?? "headless_stop");
        }

        /// <summary>
        /// 逐句执行 headless 测试并维护运行状态；须在 UI 线程调用（fire-and-forget 启动），
        /// 内部捕获全部异常，不会让调用方未观察异常。
        /// </summary>
        internal async Task RunHeadlessPromptsAsync(
            IReadOnlyList<string> prompts,
            int turnTimeoutMinutes,
            string source)
        {
            if (prompts == null || prompts.Count == 0)
            {
                throw new ArgumentException("测试语句不能为空。", nameof(prompts));
            }
            var state = HeadlessRunState;
            var report = new StringBuilder();
            var totalTime = Stopwatch.StartNew();
            state.RunId = Guid.NewGuid().ToString("N");
            state.Running = true;
            state.TotalPrompts = prompts.Count;
            state.CompletedPrompts = 0;
            state.CurrentPromptIndex = -1;
            state.PassedCount = 0;
            state.FailedCount = 0;
            state.LastPromptFailure = string.Empty;
            state.LastError = string.Empty;
            state.StartedAt = DateTime.Now;
            state.UpdatedAt = DateTime.Now;
            AiAnalysisLogger.Write(new JObject
            {
                ["event"] = "ai_headless_test.run_started",
                ["runId"] = state.RunId,
                ["source"] = source ?? string.Empty,
                ["promptCount"] = prompts.Count,
                ["turnTimeoutMinutes"] = turnTimeoutMinutes,
                ["autoApprove"] = true
            });
            report.AppendLine("# AI Headless 回归测试")
                .AppendLine()
                .AppendLine("- 时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                .AppendLine("- 来源：" + (string.IsNullOrWhiteSpace(source) ? "command_line" : source))
                .AppendLine("- 语句数：" + prompts.Count)
                .AppendLine("- 单句超时：" + turnTimeoutMinutes + " 分钟")
                .AppendLine();
            try
            {
                for (int index = 0; index < prompts.Count; index++)
                {
                    string prompt = prompts[index];
                    state.CurrentPromptIndex = index;
                    state.UpdatedAt = DateTime.Now;
                    AiAnalysisLogger.Write(new JObject
                    {
                        ["event"] = "ai_headless_test.prompt_started",
                        ["runId"] = state.RunId,
                        ["promptIndex"] = index,
                        ["prompt"] = prompt
                    });
                    var watch = Stopwatch.StartNew();
                    Task<bool> sendTask = SendHeadlessPromptAsync(prompt);
                    bool timedOut = false;
                    Task timeoutTask = Task.Delay(TimeSpan.FromMinutes(turnTimeoutMinutes));
                    if (await Task.WhenAny(sendTask, timeoutTask).ConfigureAwait(true) == timeoutTask)
                    {
                        timedOut = true;
                        CancelHeadlessTask(HeadlessActiveRuntime, "headless_timeout");
                        try
                        {
                            await sendTask.ConfigureAwait(true);
                        }
                        catch
                        {
                        }
                    }
                    bool ok = sendTask.Status == TaskStatus.RanToCompletion && sendTask.Result && !timedOut;
                    string assistantText = HeadlessLastAssistantText ?? string.Empty;
                    string failureReason = ok ? string.Empty
                        : timedOut ? "单句超时（" + turnTimeoutMinutes + " 分钟），已请求停止。"
                        : HeadlessLastFailure;
                    watch.Stop();
                    state.CompletedPrompts = index + 1;
                    state.LastPromptFailure = failureReason;
                    state.UpdatedAt = DateTime.Now;
                    if (ok) state.PassedCount++; else state.FailedCount++;
                    AiAnalysisLogger.Write(new JObject
                    {
                        ["event"] = "ai_headless_test.prompt_completed",
                        ["runId"] = state.RunId,
                        ["promptIndex"] = index,
                        ["passed"] = ok,
                        ["timedOut"] = timedOut,
                        ["durationSeconds"] = Math.Round(watch.Elapsed.TotalSeconds, 1),
                        ["failureReason"] = failureReason,
                        ["assistantTextLength"] = assistantText.Length,
                        ["assistantText"] = assistantText
                    });
                    report.AppendLine("## 句 " + (index + 1) + " · " + (ok ? "通过" : timedOut ? "超时" : "失败")
                        + " · " + Math.Round(watch.Elapsed.TotalSeconds, 1) + "s")
                        .AppendLine()
                        .AppendLine("> " + prompt.Replace("\n", " "))
                        .AppendLine();
                    if (!ok && !string.IsNullOrWhiteSpace(failureReason))
                    {
                        report.AppendLine("**失败原因**：" + failureReason).AppendLine();
                    }
                    report.AppendLine(string.IsNullOrWhiteSpace(assistantText) ? "（无输出）" : assistantText)
                        .AppendLine();
                }
            }
            catch (Exception ex)
            {
                state.LastError = ex.Message;
                state.FailedCount++;
                report.AppendLine("## 执行异常").AppendLine().AppendLine(ex.ToString());
            }
            finally
            {
                totalTime.Stop();
                state.Running = false;
                state.CurrentPromptIndex = -1;
                state.UpdatedAt = DateTime.Now;
                AiAnalysisLogger.Write(new JObject
                {
                    ["event"] = "ai_headless_test.run_completed",
                    ["runId"] = state.RunId,
                    ["passedCount"] = state.PassedCount,
                    ["failedCount"] = state.FailedCount,
                    ["totalDurationSeconds"] = Math.Round(totalTime.Elapsed.TotalSeconds, 1)
                });
                report.Insert(0, string.Empty).Insert(0, "- 结果：通过 " + state.PassedCount + "，失败 " + state.FailedCount
                    + "，总耗时 " + Math.Round(totalTime.Elapsed.TotalSeconds / 60, 1) + " 分钟");
                TryWriteHeadlessReport(report.ToString(), state);
            }
        }

        private static void TryWriteHeadlessReport(string content, HeadlessAiTestRunState state)
        {
            try
            {
                string directory = Path.Combine(@"D:\AutomationLogs", "AIExecution", "HeadlessTests");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory,
                    "run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".md");
                File.WriteAllText(path, content, Encoding.UTF8);
                state.ReportPath = path;
            }
            catch (Exception ex)
            {
                // 报告写不进磁盘不影响测试事实；原因进运行状态与分析日志。
                state.LastError = "报告写入失败：" + ex.Message;
                AiAnalysisLogger.Write(new JObject
                {
                    ["event"] = "ai_headless_test.report_write_failed",
                    ["error"] = ex.Message
                });
            }
        }
    }
}
