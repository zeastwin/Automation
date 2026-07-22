using System;
// 模块：运行时 / AI 集成。
// 职责范围：管理 AI 会话、配置、ACP/MCP 进程、受管运行环境和分析记录。
// 状态所有权：会话、活动任务、取消和持久化都在此；FrmAiAssistant 只投影状态并处理用户交互。

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Automation
{
    /// <summary>
    /// AI 会话、任务运行时和持久化的权威状态。窗体只负责展示当前状态和转发用户动作。
    /// </summary>
    internal sealed class AiConversationCoordinator
    {
        private readonly object clientLock = new object();

        public List<AiConversation> Conversations { get; } = new List<AiConversation>();
        public Dictionary<string, AiTaskRuntime> TaskRuntimes { get; } =
            new Dictionary<string, AiTaskRuntime>(StringComparer.Ordinal);
        public AiConversation ActiveConversation { get; private set; }
        public bool TaskHomeVisible { get; private set; } = true;

        public AiTaskRuntime ActiveRuntime => ActiveConversation != null
            && TaskRuntimes.TryGetValue(ActiveConversation.Id, out AiTaskRuntime runtime)
                ? runtime
                : null;

        public bool HasRunningTasks => TaskRuntimes.Values.Any(runtime => runtime.Running);

        public bool TryBeginTask(
            AiTaskRuntime runtime,
            string enteredPrompt,
            IReadOnlyList<GooseFileAttachment> attachments,
            out string prompt,
            out string conversationText,
            out IReadOnlyList<GooseFileAttachment> preparedAttachments,
            out string error)
        {
            prompt = (enteredPrompt ?? string.Empty).Trim();
            conversationText = null;
            preparedAttachments = (attachments ?? Array.Empty<GooseFileAttachment>()).ToList();
            error = null;
            if (string.IsNullOrWhiteSpace(prompt) && preparedAttachments.Count == 0)
            {
                error = "请输入内容或添加附件。";
                return false;
            }
            if (runtime == null)
            {
                error = "AI 任务运行时不存在。";
                return false;
            }
            if (runtime.Running)
            {
                error = "AI 任务正在运行。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(prompt)) prompt = "请分析我上传的文件。";

            conversationText = prompt;
            if (preparedAttachments.Count > 0)
            {
                conversationText += "\n\n📎 附件：" + string.Join(
                    "、",
                    preparedAttachments.Select(item => item.FileName));
            }
            DateTime startedAt = DateTime.Now;
            runtime.Conversation.Messages.Add(new AiConversationMessage
            {
                Role = "user",
                Text = conversationText,
                Time = startedAt
            });
            if (runtime.Conversation.Messages.Count == 1)
            {
                runtime.Conversation.Title = conversationText.Length > 24
                    ? conversationText.Substring(0, 24) + "…"
                    : conversationText;
            }
            runtime.Conversation.UpdatedAt = startedAt;
            runtime.PendingEvents.Clear();
            runtime.Running = true;
            runtime.Status = "进行中";
            runtime.Cancellation?.Dispose();
            runtime.Cancellation = new CancellationTokenSource();
            return true;
        }

        public async Task<AiTaskExecutionResult> ExecuteTaskAsync(
            AiTaskRuntime runtime,
            string prompt,
            IReadOnlyList<GooseFileAttachment> attachments,
            Func<GooseAcpClient> clientFactory)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (clientFactory == null) throw new ArgumentNullException(nameof(clientFactory));
            GooseAcpClient client = null;
            try
            {
                client = clientFactory();
                await client.PromptAsync(
                    prompt,
                    attachments,
                    runtime.Cancellation.Token).ConfigureAwait(false);
                runtime.Status = "已完成";
                return BuildTaskResult(AiTaskExecutionStatus.Completed, runtime, client, null);
            }
            catch (OperationCanceledException)
            {
                runtime.Status = "已停止";
                return BuildTaskResult(AiTaskExecutionStatus.Cancelled, runtime, client, null);
            }
            catch (Exception ex)
            {
                runtime.Status = "失败";
                return BuildTaskResult(AiTaskExecutionStatus.Failed, runtime, client, ex.Message);
            }
            finally
            {
                runtime.Running = false;
                runtime.Cancellation?.Dispose();
                runtime.Cancellation = null;
            }
        }

        public void CompleteTask(
            AiTaskRuntime runtime,
            string assistantText,
            string visualizationJson)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (!string.IsNullOrWhiteSpace(assistantText))
            {
                runtime.Conversation.Messages.Add(new AiConversationMessage
                {
                    Role = "assistant",
                    Text = assistantText,
                    Time = DateTime.Now,
                    VisualizationJson = visualizationJson
                });
            }
            runtime.Conversation.UpdatedAt = DateTime.Now;
            runtime.PendingEvents.Clear();
        }

        public bool TryLoad(out string error)
        {
            error = null;
            try
            {
                Conversations.Clear();
                Conversations.AddRange(AiConversationStorage.Load());
            }
            catch (Exception ex)
            {
                Conversations.Clear();
                error = ex.Message;
            }

            DisposeClients();
            TaskRuntimes.Clear();
            foreach (AiConversation conversation in Conversations)
            {
                TaskRuntimes[conversation.Id] = new AiTaskRuntime
                {
                    Conversation = conversation,
                    Status = "已完成",
                    RestoredContext = BuildRestoredContext(conversation)
                };
            }
            ActiveConversation = null;
            TaskHomeVisible = true;
            return error == null;
        }

        public AiTaskRuntime StartNew()
        {
            DateTime now = DateTime.Now;
            var conversation = new AiConversation
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = "新对话",
                CreatedAt = now,
                UpdatedAt = now
            };
            var runtime = new AiTaskRuntime
            {
                Conversation = conversation,
                Status = "等待开始"
            };
            Conversations.Insert(0, conversation);
            TaskRuntimes[conversation.Id] = runtime;
            ActiveConversation = conversation;
            TaskHomeVisible = false;
            TrimOldConversations();
            return runtime;
        }

        public bool TryDeleteActive(out string error)
        {
            error = null;
            if (ActiveConversation == null)
            {
                error = "当前没有可删除的会话。";
                return false;
            }
            AiTaskRuntime activeRuntime = ActiveRuntime;
            if (activeRuntime?.Running == true)
            {
                error = "当前会话仍在运行。";
                return false;
            }

            string deletedId = ActiveConversation.Id;
            if (TaskRuntimes.TryGetValue(deletedId, out AiTaskRuntime runtime))
            {
                DisposeRuntime(runtime);
                TaskRuntimes.Remove(deletedId);
            }
            Conversations.RemoveAll(item => string.Equals(item.Id, deletedId, StringComparison.Ordinal));
            ActiveConversation = null;
            TaskHomeVisible = true;
            return true;
        }

        public bool TrySwitch(string conversationId, out AiTaskRuntime runtime, out string error)
        {
            runtime = null;
            error = null;
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                error = "会话标识为空。";
                return false;
            }
            AiConversation target = Conversations.FirstOrDefault(item =>
                string.Equals(item.Id, conversationId, StringComparison.Ordinal));
            if (target == null)
            {
                error = "未找到该历史会话。";
                return false;
            }
            ActiveConversation = target;
            if (!TaskRuntimes.TryGetValue(target.Id, out runtime))
            {
                runtime = new AiTaskRuntime
                {
                    Conversation = target,
                    Status = "已完成",
                    RestoredContext = BuildRestoredContext(target)
                };
                TaskRuntimes[target.Id] = runtime;
            }
            TaskHomeVisible = false;
            return true;
        }

        public void ShowHome()
        {
            ActiveConversation = null;
            TaskHomeVisible = true;
        }

        public bool TrySave(out string error)
        {
            error = null;
            try
            {
                Conversations.Sort((left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
                TrimOldConversations();
                AiConversationStorage.Save(Conversations);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public GooseAcpClient GetOrCreateClient(
            AiTaskRuntime runtime,
            Func<GooseAcpClient> factory)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            lock (clientLock)
            {
                if (runtime.Client == null) runtime.Client = factory();
                return runtime.Client;
            }
        }

        public void DisposeClients()
        {
            lock (clientLock)
            {
                foreach (AiTaskRuntime runtime in TaskRuntimes.Values)
                {
                    DisposeRuntime(runtime);
                }
            }
        }

        public void Cancel(AiTaskRuntime runtime)
        {
            if (runtime == null) return;
            runtime.Cancellation?.Cancel();
            lock (clientLock)
            {
                runtime.Client?.Cancel();
            }
        }

        private void TrimOldConversations()
        {
            while (Conversations.Count > AiConversationStorage.MaxConversationCount)
            {
                AiConversation oldest = Conversations
                    .OrderBy(item => item.UpdatedAt)
                    .FirstOrDefault(item => !TaskRuntimes.TryGetValue(item.Id, out AiTaskRuntime runtime)
                        || !runtime.Running);
                if (oldest == null)
                {
                    // 所有超额会话均在运行时暂不剪裁，避免历史数量上限中断正在执行的任务。
                    break;
                }
                Conversations.Remove(oldest);
                if (TaskRuntimes.TryGetValue(oldest.Id, out AiTaskRuntime runtime))
                {
                    DisposeRuntime(runtime);
                    TaskRuntimes.Remove(oldest.Id);
                }
            }
        }

        private static string BuildRestoredContext(AiConversation conversation)
        {
            var builder = new StringBuilder();
            foreach (AiConversationMessage message in conversation?.Messages
                ?? Enumerable.Empty<AiConversationMessage>())
            {
                builder.Append(message.Role == "user" ? "用户：" : "EW-AI：");
                builder.AppendLine(message.Text);
            }
            return builder.ToString();
        }

        private static AiTaskExecutionResult BuildTaskResult(
            AiTaskExecutionStatus status,
            AiTaskRuntime runtime,
            GooseAcpClient client,
            string error)
        {
            return new AiTaskExecutionResult
            {
                Status = status,
                Client = client,
                Error = error,
                Events = runtime.PendingEvents.ToList()
            };
        }

        private static void DisposeRuntime(AiTaskRuntime runtime)
        {
            if (runtime == null) return;
            try
            {
                runtime.Cancellation?.Cancel();
                runtime.Client?.Dispose();
            }
            catch
            {
            }
            runtime.Client = null;
            runtime.Cancellation?.Dispose();
            runtime.Cancellation = null;
            runtime.Running = false;
        }
    }

    internal enum AiTaskExecutionStatus
    {
        Completed,
        Cancelled,
        Failed
    }

    internal sealed class AiTaskExecutionResult
    {
        public AiTaskExecutionStatus Status { get; internal set; }
        public GooseAcpClient Client { get; internal set; }
        public string Error { get; internal set; }
        public IReadOnlyList<GooseAcpEvent> Events { get; internal set; }
    }

    internal sealed class AiTaskRuntime
    {
        public AiConversation Conversation { get; set; }
        public GooseAcpClient Client { get; set; }
        public CancellationTokenSource Cancellation { get; set; }
        public List<GooseAcpEvent> PendingEvents { get; } = new List<GooseAcpEvent>();
        public bool Running { get; set; }
        public string Status { get; set; } = "已完成";
        public string RestoredContext { get; set; }
    }
}
