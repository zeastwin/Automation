using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
// 模块：MCP / 流程设计 Guide。
// 职责范围：按主题读取内嵌流程设计知识；字段 Schema 和运行行为仍由各自权威代码生成。
// 排查入口：Guide 缺失检查 EmbeddedResource 名称和哈希，字段不符应查 Protocol/行为 Catalog。

namespace Automation.McpServer
{
    internal static class ProcessDesignGuideCatalog
    {
        private const string ResourceName = "Automation.McpServer.Guides.ProcessDesignGuide.md";

        public static readonly string[] SupportedTopics =
        {
            "core",
            "lifecycle",
            "orchestration",
            "interlock",
            "actuator",
            "motion",
            "vision",
            "pick-place",
            "transfer",
            "identify",
            "transaction",
            "monitoring",
            "quality",
            "recovery",
            "custom-function",
            "composition",
            "review"
        };

        public static string Get(string[] topics, string? detail = null)
        {
            string[] normalized = (topics ?? Array.Empty<string>())
                .Select(value => (value ?? string.Empty).Trim().ToLowerInvariant())
                .ToArray();
            string normalizedDetail = string.IsNullOrWhiteSpace(detail)
                ? "compact"
                : detail.Trim().ToLowerInvariant();
            if (!string.Equals(normalizedDetail, "compact", StringComparison.Ordinal)
                && !string.Equals(normalizedDetail, "full", StringComparison.Ordinal))
            {
                return Error(
                    "PROCESS_DESIGN_DETAIL_INVALID",
                    "detail 只能是 compact 或 full。");
            }
            if (normalized.Length == 0 || normalized.Any(string.IsNullOrEmpty))
            {
                return Error(
                    "PROCESS_DESIGN_TOPIC_REQUIRED",
                    "topics 至少包含一个流程设计主题。");
            }

            string[] duplicates = normalized
                .GroupBy(value => value, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicates.Length > 0)
            {
                return Error(
                    "PROCESS_DESIGN_TOPIC_DUPLICATED",
                    "topics 包含重复主题：" + string.Join("、", duplicates) + "。");
            }

            string[] invalid = normalized
                .Where(value => !SupportedTopics.Contains(value, StringComparer.Ordinal))
                .ToArray();
            if (invalid.Length > 0)
            {
                return Error(
                    "PROCESS_DESIGN_TOPIC_INVALID",
                    "topics 包含不支持的主题：" + string.Join("、", invalid) + "。");
            }

            string source;
            using (Stream? stream = typeof(ProcessDesignGuideCatalog).Assembly
                .GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    return Error(
                        "PROCESS_DESIGN_GUIDE_UNAVAILABLE",
                        "流程设计指南内嵌资源不存在。");
                }
                using var reader = new StreamReader(stream, Encoding.UTF8, true);
                source = reader.ReadToEnd();
            }

            string[] selectedTopics = normalized.Contains("core", StringComparer.Ordinal)
                ? normalized
                : new[] { "core" }.Concat(normalized).ToArray();
            var sections = new List<object>();
            foreach (string topic in selectedTopics)
            {
                bool compact = string.Equals(normalizedDetail, "compact", StringComparison.Ordinal);
                string sourceTopic = compact && string.Equals(topic, "core", StringComparison.Ordinal)
                    ? "core-compact"
                    : topic;
                if (!TryExtract(source, sourceTopic, out string markdown))
                {
                    return Error(
                        "PROCESS_DESIGN_SECTION_INVALID",
                        "流程设计指南缺少完整主题区块：" + topic + "。");
                }
                sections.Add(new
                {
                    topic,
                    format = compact ? "compact" : "full",
                    markdown = compact && !string.Equals(topic, "core", StringComparison.Ordinal)
                        ? BuildCompactTopicMarkdown(markdown)
                        : markdown
                });
            }

            ProcessKnowledgeSelection knowledge;
            try
            {
                knowledge = ProcessKnowledgeCatalog.Get(normalized);
            }
            catch (InvalidDataException ex)
            {
                return Error(
                    "PROCESS_KNOWLEDGE_INVALID",
                    ex.Message);
            }

            string sourceSha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
            object[] functionalBlocks = normalized
                .Select(BuildFunctionalBlock)
                .OfType<object>()
                .ToArray();
            string[] requiredResourceTypes = normalized
                .SelectMany(GetRequiredResourceTypes)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            IEnumerable<object> knowledgeBlocks = string.Equals(
                normalizedDetail, "full", StringComparison.Ordinal)
                ? knowledge.Blocks.Select(block => (object)new
                {
                    patternId = block.PatternId,
                    title = block.Title,
                    summary = block.Summary,
                    topics = block.Topics,
                    capabilities = block.Capabilities,
                    deviceTypes = block.DeviceTypes,
                    processTypes = block.ProcessTypes,
                    riskTags = block.RiskTags,
                    markdown = block.Markdown,
                    contentSha256 = block.ContentSha256
                })
                : knowledge.Blocks.Select(block => (object)new
                {
                    patternId = block.PatternId,
                    title = block.Title,
                    summary = block.Summary,
                    observableGoal = ExtractKnowledgeSection(block.Markdown, "可观察目标"),
                    applicableBoundary = ExtractKnowledgeSection(block.Markdown, "适用边界"),
                    prerequisiteFacts = ExtractKnowledgeSection(block.Markdown, "当前事实与适配"),
                    recommendedStages = ExtractKnowledgeSection(block.Markdown, "参考阶段"),
                    completionEvidence = ExtractKnowledgeSection(block.Markdown, "完成证据"),
                    failureAndRecovery = ExtractKnowledgeSection(block.Markdown, "失败、超时与恢复"),
                    riskTags = block.RiskTags,
                    contentSha256 = block.ContentSha256
                });
            return JsonSerializer.Serialize(new
            {
                ok = true,
                type = "process.design_guide",
                detail = normalizedDetail,
                source = "Automation AI 流程设计知识",
                sourceSha256,
                requestedTopics = normalized,
                includedCore = true,
                sections,
                usableKnowledgeCatalogSha256 = knowledge.CatalogSha256,
                knowledgeBlocks,
                goalCoverage = new
                {
                    proofBoundary = "Readiness只证明当前结构可保存或可启动，不证明用户业务目标已经完成。",
                    completionRule = "最终答复前根据当前目标核对已提交结构与功能槽，分别标明已实现、占位和缺少事实；功能槽不规定固定调用顺序或一次提交完成。",
                    evidenceGapPolicy = new
                    {
                        missingFact = "相关资源存在但精确目标、角色、极性或终态证据缺失，只能证明当前仍有证据缺口，不能据此判定该功能不需要。",
                        alternativeMechanism = "只有当前事实或用户意图足以支持时才改用另一机构；替代会实质改变目标含义时，询问用户或用config.placeholder保留原目标。",
                        completionClaim = "不得用可编译、ready或runnable代替功能槽完成证据，也不得把未实现槽位静默从目标中删除。"
                    },
                    resourceRequests = requiredResourceTypes.Select(type => new { type }),
                    functionalBlocks
                },
                authority = new
                {
                    fields = "当前语义或原生Schema",
                    runtimeBehavior = "当前行为契约和Guide",
                    resources = "当前资源工具返回",
                    readiness = "当前readiness和运行闸门",
                    referencePolicy = "只返回已完成甄别的可用规范；旧项目证据和审核中间结果不进入运行时上下文"
                }
            });
        }

        private static object? BuildFunctionalBlock(string topic)
        {
            switch (topic)
            {
                case "actuator":
                    return new
                    {
                        topic,
                        requiredResourceTypes = new[] { "io_input", "io_output" },
                        slots = new object[]
                        {
                            Slot("command", "执行器目标命令", "required", "真实输出及已确认的电气目标"),
                            Slot("feedback", "目标反馈", "when_available", "反馈等待或明确的开环设备契约"),
                            Slot("failure_exit", "失败出口", "when_requested_or_required", "超时或矛盾反馈后的可见控制流")
                        }
                    };
                case "motion":
                    return new
                    {
                        topic,
                        requiredResourceTypes = new[] { "motion" },
                        slots = new object[]
                        {
                            Slot("motion_target", "运动目标", "required", "当前工站与实际轴；优先复用已示教点位，没有现成目标时规划有业务含义的点位名并登记为待示教"),
                            Slot("motion_action", "真实运动动作", "required", "原生运动指令及其精确契约"),
                            Slot("motion_completion", "运动完成证据", "required", "同步完成或显式等待/到位证据")
                        }
                    };
                case "pick-place":
                    return new
                    {
                        topic,
                        requiredResourceTypes = new[] { "motion", "io_input", "io_output" },
                        slots = new object[]
                        {
                            Slot("pickup_motion", "进入取料位置", "required", "取料工站与真实运动动作；缺少现成点位时规划取料点名，坐标留给人工示教"),
                            Slot("acquire", "取得工件", "required", "夹持或真空命令及可用反馈"),
                            Slot("place_motion", "进入放料位置", "required", "放料工站与真实运动动作；缺少现成点位时规划放料点名，坐标留给人工示教"),
                            Slot("release", "释放工件", "required", "释放命令及可用反馈"),
                            Slot("safe_transition", "安全过渡与机构复位时序", "required_when_actuator_and_motion_interleave", "机构动作与轴运动相邻时必须确立先后：复位后移动（防撞击）或夹持随行移载；缺工艺事实时询问用户或按标准时序实现并标注假设")
                        }
                    };
                case "transfer":
                    return new
                    {
                        topic,
                        requiredResourceTypes = new[] { "motion", "io_input", "io_output" },
                        slots = new object[]
                        {
                            Slot("mechanism_selection", "确认搬运机构", "required", "当前motion与相关IO目录及用户目标；缺少点位或角色事实不是排除相关机构的证据"),
                            Slot("transfer_action", "搬运动作", "required", "现场实际输送、升降、气动或运动资源"),
                            Slot("arrival_evidence", "到达证明", "required", "边界/到位反馈或明确开环契约；单一输入未激活不证明相反机械终态"),
                            Slot("handoff", "交接提交", "when_crossing_ownership", "接收确认与占用状态转移")
                        }
                    };
                default:
                    return null;
            }
        }

        private static string[] GetRequiredResourceTypes(string topic)
        {
            switch (topic)
            {
                case "actuator":
                    return new[] { "io_input", "io_output" };
                case "motion":
                    return new[] { "motion" };
                case "pick-place":
                case "transfer":
                    return new[] { "motion", "io_input", "io_output" };
                default:
                    return Array.Empty<string>();
            }
        }

        private static string BuildCompactTopicMarkdown(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
            string heading = markdown.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;
            var sections = new List<string>();
            foreach (string name in new[]
            {
                "适用范围", "职责", "可组合子块", "设计要点", "关键边界", "参考骨架", "推荐结构"
            })
            {
                string value = ExtractMarkdownSubsection(markdown, name);
                if (!string.IsNullOrWhiteSpace(value)) sections.Add("### " + name + "\n\n" + value);
                if (sections.Count >= 3) break;
            }
            string result = string.Join("\n\n", new[] { heading }.Concat(sections));
            const int maxLength = 2400;
            return result.Length <= maxLength ? result : result.Substring(0, maxLength) + "…";
        }

        private static string ExtractMarkdownSubsection(string markdown, string heading)
        {
            string marker = "### " + heading;
            int start = markdown.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return string.Empty;
            start += marker.Length;
            int end = markdown.IndexOf("\n### ", start, StringComparison.Ordinal);
            if (end < 0) end = markdown.Length;
            return markdown.Substring(start, end - start).Trim();
        }

        private static object Slot(string id, string goal, string requirement, string evidence)
        {
            return new { id, goal, requirement, evidence };
        }

        private static string ExtractKnowledgeSection(string markdown, string heading)
        {
            if (string.IsNullOrWhiteSpace(markdown) || string.IsNullOrWhiteSpace(heading))
                return string.Empty;
            string marker = "## " + heading;
            int start = markdown.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return string.Empty;
            start += marker.Length;
            int end = markdown.IndexOf("\n## ", start, StringComparison.Ordinal);
            if (end < 0) end = markdown.Length;
            return markdown.Substring(start, end - start).Trim();
        }

        private static bool TryExtract(string source, string topic, out string markdown)
        {
            string startMarker = "<!-- process-design:" + topic + ":start -->";
            string endMarker = "<!-- process-design:" + topic + ":end -->";
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
            {
                markdown = string.Empty;
                return false;
            }
            start += startMarker.Length;
            int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
            if (end < 0)
            {
                markdown = string.Empty;
                return false;
            }
            markdown = source.Substring(start, end - start).Trim();
            return markdown.Length > 0;
        }

        private static string Error(string errorCode, string message)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                type = "mcp.error",
                errorCode,
                message,
                allowedDetails = new[] { "compact", "full" },
                allowedTopics = SupportedTopics
            });
        }
    }
}
