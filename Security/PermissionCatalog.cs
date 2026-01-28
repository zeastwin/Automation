using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    public static class PermissionKeys
    {
        public const string ProcessAccess = "Process.Access";
        public const string ProcessEdit = "Process.Edit";
        public const string ProcessRun = "Process.Run";
        public const string ProcessSearch = "Process.Search";

        public const string CardConfigAccess = "CardConfig.Access";
        public const string IOMonitorUse = "IOMonitor.Use";

        public const string StationAccess = "Station.Access";
        public const string IODebugAccess = "IODebug.Access";
        public const string CommunicationAccess = "Communication.Access";
        public const string ValueAccess = "Value.Access";
        public const string ValueDebugAccess = "ValueDebug.Access";
        public const string AiAccess = "AI.Access";
        public const string PlcAccess = "PLC.Access";
        public const string AlarmConfigAccess = "AlarmConfig.Access";
        public const string AppConfigAccess = "AppConfig.Access";
        public const string AccountManage = "Account.Manage";
        public const string OpenConfigFolder = "Tool.OpenConfigFolder";
    }

    public sealed class PermissionDefinition
    {
        public PermissionDefinition(string key, string groupId, string groupTitle, string title, int order)
        {
            Key = key;
            GroupId = groupId;
            GroupTitle = groupTitle;
            Title = title;
            Order = order;
        }

        public string Key { get; }
        public string GroupId { get; }
        public string GroupTitle { get; }
        public string Title { get; }
        public int Order { get; }
    }

    public static class PermissionCatalog
    {
        private static readonly List<PermissionDefinition> definitions = new List<PermissionDefinition>
        {
            new PermissionDefinition(PermissionKeys.ProcessAccess, "process", "流程模块", "进入流程", 0),
            new PermissionDefinition(PermissionKeys.ProcessEdit, "process", "流程模块", "流程编辑", 1),
            new PermissionDefinition(PermissionKeys.ProcessRun, "process", "流程模块", "流程运行", 2),
            new PermissionDefinition(PermissionKeys.ProcessSearch, "process", "流程模块", "流程查找/定位", 3),

            new PermissionDefinition(PermissionKeys.CardConfigAccess, "card", "控制卡与IO", "进入控制卡/IO配置", 0),
            new PermissionDefinition(PermissionKeys.IOMonitorUse, "card", "控制卡与IO", "IO监视", 1),

            new PermissionDefinition(PermissionKeys.StationAccess, "station", "工站模块", "进入工站", 0),
            new PermissionDefinition(PermissionKeys.IODebugAccess, "iodbg", "IO调试", "进入IO调试", 0),
            new PermissionDefinition(PermissionKeys.CommunicationAccess, "comm", "通讯模块", "进入通讯", 0),
            new PermissionDefinition(PermissionKeys.ValueAccess, "value", "变量模块", "进入变量", 0),
            new PermissionDefinition(PermissionKeys.ValueDebugAccess, "valuedbg", "变量调试", "进入变量调试", 0),
            new PermissionDefinition(PermissionKeys.AiAccess, "ai", "AI助手", "进入AI助手", 0),
            new PermissionDefinition(PermissionKeys.PlcAccess, "plc", "PLC模块", "进入PLC", 0),
            new PermissionDefinition(PermissionKeys.AlarmConfigAccess, "alarm", "报警配置", "进入报警配置", 0),
            new PermissionDefinition(PermissionKeys.AppConfigAccess, "app", "程序设置", "进入程序设置", 0),
            new PermissionDefinition(PermissionKeys.OpenConfigFolder, "tool", "工具", "打开程序文件夹", 0),
            new PermissionDefinition(PermissionKeys.AccountManage, "account", "账户管理", "账户管理", 0)
        };

        private static readonly Dictionary<string, string> groupAccessKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "process", PermissionKeys.ProcessAccess },
            { "card", PermissionKeys.CardConfigAccess },
            { "station", PermissionKeys.StationAccess },
            { "iodbg", PermissionKeys.IODebugAccess },
            { "comm", PermissionKeys.CommunicationAccess },
            { "value", PermissionKeys.ValueAccess },
            { "valuedbg", PermissionKeys.ValueDebugAccess },
            { "ai", PermissionKeys.AiAccess },
            { "plc", PermissionKeys.PlcAccess },
            { "alarm", PermissionKeys.AlarmConfigAccess },
            { "app", PermissionKeys.AppConfigAccess },
            { "tool", PermissionKeys.OpenConfigFolder },
            { "account", PermissionKeys.AccountManage }
        };

        private static readonly HashSet<string> allKeys = new HashSet<string>(definitions.Select(item => item.Key), StringComparer.Ordinal);

        public static IReadOnlyList<PermissionDefinition> All => definitions;

        public static IReadOnlyDictionary<string, string> GroupAccessKeys => groupAccessKeys;

        public static bool IsKnownKey(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && allKeys.Contains(key);
        }

        public static IReadOnlyList<string> GetDefaultPermissions(UserRole role)
        {
            if (role == UserRole.SystemAdmin)
            {
                return allKeys.ToList();
            }
            if (role == UserRole.Admin)
            {
                return definitions
                    .Select(item => item.Key)
                    .Where(key => !string.Equals(key, PermissionKeys.AccountManage, StringComparison.Ordinal))
                    .ToList();
            }
            return new List<string>
            {
                PermissionKeys.ProcessAccess,
                PermissionKeys.ProcessRun,
                PermissionKeys.ProcessSearch
            };
        }
    }
}
