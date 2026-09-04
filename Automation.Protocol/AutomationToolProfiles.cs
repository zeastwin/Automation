using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation.Protocol
{
    /// <summary>
    /// Automation MCP 工具档位的唯一名称契约。
    /// Diagnostic/Editor 是用户权限外壳；TaskCoordinator 是动态申请入口；其余名称是工作阶段能力包。
    /// </summary>
    public static class AutomationToolProfiles
    {
        public const string Diagnostic = "Diagnostic";
        public const string Editor = "Editor";
        public const string RuntimeDiagnostic = "RuntimeDiagnostic";
        public const string MachineAgent = "MachineAgent";

        public const string TaskCoordinator = "TaskCoordinator";
        public const string ProcessDesign = "ProcessDesign";
        public const string ProcessReview = "ProcessReview";
        public const string ProcessCreate = "ProcessCreate";
        public const string ProcessEdit = "ProcessEdit";
        public const string ResourceEdit = "ResourceEdit";
        public const string RuntimeControl = "RuntimeControl";
        public const string SourceReview = "SourceReview";
        public const string SourceDevelopment = "SourceDevelopment";
        public const string PlatformConfiguration = "PlatformConfiguration";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Diagnostic, Editor, RuntimeDiagnostic, MachineAgent,
            TaskCoordinator,
            ProcessDesign, ProcessReview, ProcessCreate, ProcessEdit, ResourceEdit,
            RuntimeControl, SourceReview, SourceDevelopment, PlatformConfiguration
        };

        public static readonly IReadOnlyList<string> TaskProfiles = new[]
        {
            TaskCoordinator,
            ProcessDesign, ProcessReview, ProcessCreate, ProcessEdit, ResourceEdit,
            RuntimeControl, SourceReview, SourceDevelopment, PlatformConfiguration
        };

        public static readonly IReadOnlyList<string> ExecutionProfiles = new[]
        {
            ProcessDesign, ProcessReview, ProcessCreate, ProcessEdit, ResourceEdit,
            RuntimeControl, SourceReview, SourceDevelopment, PlatformConfiguration
        };

        private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> TaskToolCatalog =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [RuntimeDiagnostic] = Tools(
                    "diagnose_issue", "get_snapshot", "get_info_log_tail",
                    "get_operation_context", "get_step_detail", "get_flow_graph",
                    "get_operation_references", "trace_resource",
                    "get_variable_by_name", "get_variable_by_index",
                    "get_io", "search_io", "get_io_state", "get_communication",
                    "list_plc_devices", "get_plc_device", "search_alarms", "get_alarm"),
                // Machine Agent 是固定独立工具面：模型只能读取现场和创建冻结预演，
                // 正式执行工具不进入 MCP，由前台确认后直接进入运行时服务。
                [MachineAgent] = Tools(
                    "diagnose_issue", "get_snapshot", "get_info_log_tail",
                    "get_operation_context", "get_step_detail", "get_flow_graph",
                    "get_operation_references", "trace_resource",
                    "get_variable_by_name", "get_variable_by_index",
                    "get_io", "search_io", "get_io_state", "get_communication",
                    "list_plc_devices", "get_plc_device", "search_alarms", "get_alarm",
                    "get_machine_context", "get_equipment_state_history",
                    "preview_process_entry_execution", "preview_process_stop"),
                [TaskCoordinator] = Tools("get_device_summary", "request_capability"),
                [ProcessDesign] = Tools("get_process_design_guide", "request_capability"),
                [ProcessReview] = Tools(
                    "resolve_proc_target", "inspect_process", "get_op_details", "search_ops",
                    "get_operation_references", "trace_resource", "find_variable_usages",
                    "get_operation_context", "audit_proc_batch", "get_operation_guide",
                    "get_semantic_operation_schema", "get_native_operation_schemas",
                    "get_variable_by_name", "get_io", "get_communication",
                    "list_stations", "get_station", "list_points", "get_point",
                    "submit_review_handoff", "request_capability"),
                [ProcessCreate] = Tools(
                    "get_process_design_guide", "list_authoring_resources",
                    "resolve_operation_capability", "get_semantic_operation_schema",
                    "get_native_operation_schemas", "inspect_process",
                    "preview_change_set", "apply_change_set",
                    "discard_change_set_preview", "validate_proc", "request_capability"),
                [ProcessEdit] = Tools(
                    "resolve_proc_target", "get_process_design_guide",
                    "list_authoring_resources", "resolve_operation_capability",
                    "inspect_process", "get_op_details", "get_operation_references",
                    "get_operation_context", "get_native_operation_field_contract",
                    "get_operation_guide", "get_semantic_operation_schema",
                    "get_native_operation_schemas", "preview_change_set", "apply_change_set",
                    "discard_change_set_preview", "validate_proc", "request_capability"),
                [ResourceEdit] = Tools(
                    "list_authoring_resources", "plan_motion_points",
                    "list_variables", "get_variable_by_name", "get_variable_by_index",
                    "find_variable_usages", "add_variable", "update_variable", "delete_variable",
                    "list_data_structs", "get_data_struct", "search_data_struct_items",
                    "upsert_data_struct", "delete_data_struct", "search_alarms", "get_alarm",
                    "set_alarm", "delete_alarm", "update_io_note", "request_capability"),
                [RuntimeControl] = Tools(
                    "get_snapshot", "wait_for_proc_state", "get_proc_overview", "get_flow_graph",
                    "get_step_detail", "get_operation_context", "get_operation_references",
                    "trace_resource", "get_info_log_tail", "diagnose_issue", "diagnose_proc", "validate_proc",
                    "get_variable_by_name", "get_variable_by_index", "get_io", "search_io",
                    "get_io_state", "get_communication", "get_plc_device", "search_alarms",
                    "get_alarm", "run_proc_test", "start_proc", "stop_proc", "pause_proc",
                    "resume_proc", "request_capability"),
                [SourceReview] = Tools(
                    "get_platform_development_context", "search_platform_source", "request_capability"),
                [SourceDevelopment] = Tools(
                    "get_platform_development_context", "search_platform_source", "request_capability"),
                [PlatformConfiguration] = Tools(
                    "get_migration_configuration", "preview_motion_io_configuration",
                    "preview_io_debug_configuration", "preview_plc_configuration",
                    "preview_communication_configuration", "apply_migration_configuration",
                    "discard_migration_configuration", "validate_platform_configuration",
                    "get_station", "get_point", "get_io", "search_io", "get_communication",
                    "get_plc_device", "request_capability")
            };

        public static string Normalize(string value)
        {
            string match = All.FirstOrDefault(item =>
                string.Equals(item, value?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                throw new ArgumentException(
                    $"Automation MCP 工具档位不支持：{value}。可选：{string.Join("/", All)}。",
                    nameof(value));
            }
            return match;
        }

        public static bool IsTaskProfile(string value)
        {
            return TaskProfiles.Any(item =>
                string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsExecutionProfile(string value)
        {
            return ExecutionProfiles.Any(item =>
                string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 动态任务与固定 Agent Profile 的精确 Automation 工具集合。MCP 过滤与 Goose 实际工具面核验共用，
        /// 避免只比较数量或在客户端复制另一份白名单。
        /// </summary>
        public static IReadOnlyList<string> GetTaskToolNames(string value)
        {
            string profile = Normalize(value);
            if (!TaskToolCatalog.TryGetValue(profile, out IReadOnlyList<string> tools))
                throw new ArgumentException($"{profile} 不是动态任务 Profile。", nameof(value));
            return tools;
        }

        // developer 工具已全量常驻所有能力面（不做 available_tools 过滤；
        // 写/shell 由前台权限闸门拦截），不再按能力包开关，UsesDeveloperTools 分类已删除。

        private static IReadOnlyList<string> Tools(params string[] names) =>
            Array.AsReadOnly(names ?? Array.Empty<string>());
    }
}
