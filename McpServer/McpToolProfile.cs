using System.Reflection;
using ModelContextProtocol.Server;

namespace Automation.McpServer
{
    internal static class McpToolProfile
    {
        private static readonly HashSet<string> CommonTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_platform_development_context",
            "search_proc_catalog", "get_proc_overview", "get_proc_detail", "get_step_detail", "get_op_detail", "get_op_details",
            "get_operation_references", "get_proc_references", "trace_resource",
            "diagnose_issue", "get_snapshot",
            "wait_for_proc_state",
            "list_variables", "search_variables", "get_variable",
            "list_stations", "get_station", "list_points", "get_point",
            "list_data_structs", "get_data_struct", "search_data_structs",
            "list_io", "get_io", "search_io", "get_io_state",
            "search_alarms", "get_alarm", "get_operation_guide"
        };

        private static readonly HashSet<string> DiagnosticOnlyTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "list_procs", "search_ops",
            "search_operation_fields", "find_references", "find_variable_usages",
            "get_operation_context", "audit_proc_batch", "list_operation_types", "get_operation_schema",
            "analyze_flow_graph", "get_info_log_tail", "validate_proc", "diagnose_proc"
        };

        private static readonly HashSet<string> EditorOnlyTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_change_capabilities", "get_operation_contracts", "get_native_operation_contract",
            "preview_change_set", "apply_change_set",
            "run_proc_test",
            "start_proc", "stop_proc", "pause_proc", "resume_proc",
            "set_variable"
        };

        public static IReadOnlyList<McpServerTool> CreateTools(string profile)
        {
            bool diagnostic;
            if (string.Equals(profile, "Diagnostic", StringComparison.OrdinalIgnoreCase)) diagnostic = true;
            else if (string.Equals(profile, "Editor", StringComparison.OrdinalIgnoreCase)) diagnostic = false;
            else throw new InvalidDataException($"MCP工具模式不支持:{profile}");
            var enabled = new HashSet<string>(CommonTools, StringComparer.Ordinal);
            if (diagnostic)
            {
                enabled.UnionWith(DiagnosticOnlyTools);
            }
            else
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
        private readonly HashSet<string> editorToolNames;
        private readonly HashSet<string> diagnosticToolNames;
        private string profile = string.Empty;

        public DynamicMcpToolRegistry(string initialProfile)
        {
            IReadOnlyList<McpServerTool> editorTools = McpToolProfile.CreateEditorTools();
            IReadOnlyList<McpServerTool> diagnosticTools = McpToolProfile.CreateTools("Diagnostic");
            allTools = editorTools.Concat(diagnosticTools)
                .GroupBy(tool => tool.ProtocolTool.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            editorToolNames = editorTools
                .Select(tool => tool.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal);
            diagnosticToolNames = diagnosticTools
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
                HashSet<string> enabledNames = string.Equals(profile, "Editor", StringComparison.Ordinal)
                    ? editorToolNames
                    : diagnosticToolNames;
                return allTools.Values.Where(tool => enabledNames.Contains(tool.ProtocolTool.Name))
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
