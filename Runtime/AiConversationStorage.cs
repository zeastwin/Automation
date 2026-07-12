using Newtonsoft.Json;
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
    }

    public sealed class AiConversation
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<AiConversationMessage> Messages { get; set; } = new List<AiConversationMessage>();
    }

    public static class AiConversationStorage
    {
        public const int MaxConversationCount = 10;

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
