using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// 模块：运行时 / Machine Agent。
// 职责范围：持久化 Machine Agent 自己的最终对话；不读取或写入原 AI 助手会话文件。

namespace Automation
{
    internal sealed class MachineAgentConversationMessage
    {
        public string Role { get; set; }
        public string Text { get; set; }
        public DateTime TimeUtc { get; set; }
    }

    internal static class MachineAgentConversationStorage
    {
        private const int MaximumMessageCount = 120;

        internal static string StoragePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Automation", "MachineAgent", "conversation.json");

        internal static List<MachineAgentConversationMessage> Load()
        {
            if (!File.Exists(StoragePath)) return new List<MachineAgentConversationMessage>();
            string json = File.ReadAllText(StoragePath, Encoding.UTF8);
            List<MachineAgentConversationMessage> messages =
                JsonConvert.DeserializeObject<List<MachineAgentConversationMessage>>(json)
                ?? throw new InvalidDataException("Machine Agent 会话历史根节点必须是数组。");
            if (messages.Any(item => item == null
                || (item.Role != "user" && item.Role != "assistant")
                || item.Text == null))
                throw new InvalidDataException("Machine Agent 会话历史包含无效消息。");
            return messages.Skip(Math.Max(0, messages.Count - MaximumMessageCount)).ToList();
        }

        internal static void Save(IEnumerable<MachineAgentConversationMessage> source)
        {
            string directory = Path.GetDirectoryName(StoragePath);
            Directory.CreateDirectory(directory);
            string temporaryPath = StoragePath + ".tmp";
            List<MachineAgentConversationMessage> messages =
                (source ?? Enumerable.Empty<MachineAgentConversationMessage>()).ToList();
            string json = JsonConvert.SerializeObject(
                messages.Skip(Math.Max(0, messages.Count - MaximumMessageCount)),
                Formatting.Indented);
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            if (File.Exists(StoragePath)) File.Replace(temporaryPath, StoragePath, null);
            else File.Move(temporaryPath, StoragePath);
        }
    }
}
