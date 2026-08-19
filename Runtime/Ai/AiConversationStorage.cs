using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Automation.Protocol;
// 模块：运行时 / AI 集成。
// 职责范围：管理 AI 会话、配置、ACP/MCP 进程、受管运行环境和分析记录。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Automation
{
    public sealed class AiConversationMessage
    {
        public string Role { get; set; }
        public string Text { get; set; }
        public DateTime Time { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string VisualizationJson { get; set; }
    }

    public sealed class AiConversation
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<AiConversationMessage> Messages { get; set; } = new List<AiConversationMessage>();
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ReviewHandoffDefinition ReviewHandoff { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string TrustedFactsJson { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? TrustedFactsObservedAt { get; set; }
    }

    public static class AiConversationStorage
    {
        public const int MaxConversationCount = 20;

        public static string StoragePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Automation", "AiConversations", "conversations.json");

        public static List<AiConversation> Load()
        {
            if (!File.Exists(StoragePath))
            {
                return new List<AiConversation>();
            }

            string json = File.ReadAllText(StoragePath, Encoding.UTF8);
            var conversations = JsonConvert.DeserializeObject<List<AiConversation>>(json);
            if (conversations == null)
            {
                throw new InvalidDataException("AI 会话历史根节点必须是数组。");
            }
            foreach (AiConversation conversation in conversations)
            {
                if (conversation == null || string.IsNullOrWhiteSpace(conversation.Id)
                    || string.IsNullOrWhiteSpace(conversation.Title) || conversation.Messages == null)
                {
                    throw new InvalidDataException("AI 会话历史包含无效会话。");
                }
                if (conversation.ReviewHandoff != null)
                {
                    string handoffError = AiTaskCapabilityPolicy.ValidateReviewHandoff(
                        conversation.ReviewHandoff,
                        AutomationToolProfiles.ProcessReview);
                    if (handoffError != null)
                        throw new InvalidDataException("AI 会话历史包含无效评审交接：" + handoffError);
                }
                if (!string.IsNullOrWhiteSpace(conversation.TrustedFactsJson))
                {
                    // 上限取预算分档的最大档：历史文件可能来自大上下文模型会话，
                    // 小上下文模型恢复时由 BuildRestoredContext 按当前窗口重新裁剪。
                    if (conversation.TrustedFactsJson.Length > AiContextBudget.MaxTrustedFactsChars)
                        throw new InvalidDataException(
                            "AI 会话可信事实超过" + AiContextBudget.MaxTrustedFactsChars + "字符边界。");
                    try
                    {
                        JObject.Parse(conversation.TrustedFactsJson);
                    }
                    catch (JsonException ex)
                    {
                        throw new InvalidDataException("AI 会话可信事实不是有效JSON对象。", ex);
                    }
                }
                foreach (AiConversationMessage message in conversation.Messages)
                {
                    if (message == null
                        || (message.Role != "user" && message.Role != "assistant")
                        || message.Text == null)
                    {
                        throw new InvalidDataException("AI 会话历史包含无效消息。");
                    }
                }
            }
            return conversations.OrderByDescending(item => item.UpdatedAt)
                .Take(MaxConversationCount).ToList();
        }

        public static void Save(IEnumerable<AiConversation> conversations)
        {
            string directory = Path.GetDirectoryName(StoragePath);
            Directory.CreateDirectory(directory);
            string temporary = StoragePath + ".tmp";
            string json = JsonConvert.SerializeObject(
                conversations.OrderByDescending(item => item.UpdatedAt).Take(MaxConversationCount),
                Formatting.Indented);
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(StoragePath))
            {
                File.Replace(temporary, StoragePath, null);
            }
            else
            {
                File.Move(temporary, StoragePath);
            }
        }
    }
}
