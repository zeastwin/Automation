using System.Reflection;
using ModelContextProtocol.Server;

namespace Automation.McpServer
{
    internal static class McpToolProfile
    {
        private static readonly HashSet<string> DiagnosticTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "search_proc_catalog", "get_proc_overview", "get_op_detail",
            "trace_resource", "search_operation_fields",
            "get_operation_context", "audit_proc_batch", "get_snapshot",
            "list_operation_types", "get_operation_schema", "get_operation_guide",
            "analyze_flow_graph", "diagnose_issue",
            "get_info_log_tail", "validate_proc",
            "list_variables", "search_variables", "get_variable",
            "list_stations", "get_station", "list_points", "get_point",
            "list_data_structs", "get_data_struct", "search_data_structs",
            "list_io", "get_io", "search_io", "get_io_state",
            "search_alarms", "get_alarm"
        };

        private static readonly HashSet<string> EditorOnlyTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "list_intent_templates", "get_intent_template", "build_patch_from_intent",
            "preview_intent", "apply_intent", "preview_patch", "apply_patch", "get_patch_action_schema",
            "create_proc", "delete_procs", "reorder_proc", "copy_proc",
            "start_proc", "stop_proc", "pause_proc", "resume_proc",
            "set_variable", "add_variable", "delete_variable",
            "add_station", "update_station", "delete_station",
            "set_point", "delete_point", "set_data_struct_field",
            "set_alarm", "delete_alarm"
        };

        public static IReadOnlyList<McpServerTool> CreateTools(string profile)
        {
            bool diagnostic;
            if (string.Equals(profile, "Diagnostic", StringComparison.OrdinalIgnoreCase)) diagnostic = true;
            else if (string.Equals(profile, "Editor", StringComparison.OrdinalIgnoreCase)) diagnostic = false;
            else throw new InvalidDataException($"MCP工具模式不支持:{profile}");
            var enabled = new HashSet<string>(DiagnosticTools, StringComparer.Ordinal);
            if (!diagnostic)
            {
                enabled.UnionWith(EditorOnlyTools);
            }
            var tools = new List<McpServerTool>();
            foreach (MethodInfo method in typeof(AutomationMcpTools).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                McpServerToolAttribute? attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                string? toolName = attribute?.Name;
                if (string.IsNullOrEmpty(toolName) || !enabled.Contains(toolName))
                {
                    continue;
                }
                tools.Add(McpServerTool.Create(method, (object)null!));
            }
            if (tools.Count == 0)
            {
                throw new InvalidOperationException($"MCP工具Profile未注册任何工具:{profile}");
            }
            return tools.OrderBy(tool => tool.ProtocolTool.Name, StringComparer.Ordinal).ToList();
        }

        public static IReadOnlyList<McpServerTool> CreateEditorTools()
        {
            return CreateTools("Editor");
        }
    }

    internal sealed class DynamicMcpToolRegistry
    {
        private readonly object syncRoot = new object();
        private readonly IReadOnlyDictionary<string, McpServerTool> allTools;
        private readonly HashSet<string> diagnosticToolNames;
        private string profile = string.Empty;

        public DynamicMcpToolRegistry(string initialProfile)
        {
            allTools = McpToolProfile.CreateEditorTools()
                .ToDictionary(tool => tool.ProtocolTool.Name, StringComparer.Ordinal);
            diagnosticToolNames = McpToolProfile.CreateTools("Diagnostic")
                .Select(tool => tool.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal);
            SetProfile(initialProfile);
        }

        public string Profile
        {
            get { lock (syncRoot) return profile; }
        }

        public IReadOnlyList<McpServerTool> GetTools()
        {
            lock (syncRoot)
            {
                return allTools.Values.Where(tool => string.Equals(profile, "Editor", StringComparison.Ordinal)
                        || diagnosticToolNames.Contains(tool.ProtocolTool.Name))
                    .OrderBy(tool => tool.ProtocolTool.Name, StringComparer.Ordinal).ToList();
            }
        }

        public void SetProfile(string value)
        {
            string normalized;
            if (string.Equals(value, "Diagnostic", StringComparison.Ordinal)) normalized = "Diagnostic";
            else if (string.Equals(value, "Editor", StringComparison.Ordinal)) normalized = "Editor";
            else throw new InvalidDataException($"MCP工具模式不支持:{value}");
            lock (syncRoot) profile = normalized;
        }

        public McpServerTool GetEnabledTool(string name)
        {
            McpServerTool? tool = GetTools().FirstOrDefault(item =>
                string.Equals(item.ProtocolTool.Name, name, StringComparison.Ordinal));
            return tool ?? throw new InvalidOperationException($"当前{Profile}模式未开放工具:{name}");
        }
    }
}
