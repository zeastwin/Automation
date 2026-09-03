using System;
using System.Collections.Generic;
using System.Linq;
using Automation.DeviceSdk;

// 模块：运行时 / 账户安全。
// 职责范围：定义账户持久化模型、编辑快照和三级账户的新建默认权限。

namespace Automation
{
    internal sealed class AccountPasswordRecord
    {
        public string Algorithm { get; set; }
        public int Iterations { get; set; }
        public string Salt { get; set; }
        public string Hash { get; set; }
    }

    internal sealed class AccountSecurityRecord
    {
        public Guid Id { get; set; }
        public string UserName { get; set; }
        public AccountLevel Level { get; set; }
        public bool Enabled { get; set; }
        public List<string> Permissions { get; set; } = new List<string>();
        public AccountPasswordRecord Password { get; set; }
    }

    internal sealed class AccountSecurityDocument
    {
        public int SchemaVersion { get; set; } = AccountSecurityStore.CurrentSchemaVersion;
        public List<AccountSecurityRecord> Accounts { get; set; } = new List<AccountSecurityRecord>();
    }

    internal sealed class AccountEditorSnapshot
    {
        public Guid Id { get; set; }
        public string UserName { get; set; }
        public AccountLevel Level { get; set; }
        public bool Enabled { get; set; }
        public IReadOnlyList<string> Permissions { get; set; }
        public bool IsBuiltInSystem { get; set; }
    }

    internal static class AccountPermissionDefaults
    {
        public static IReadOnlyList<string> ForLevel(AccountLevel level)
        {
            if (level == AccountLevel.SystemAdministrator)
            {
                return PlatformPermissionCodes.All.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            }

            var values = new HashSet<string>(StringComparer.Ordinal)
            {
                PlatformPermissionCodes.ProcessRun,
                PlatformPermissionCodes.VariableRuntimeWrite
            };
            if (level == AccountLevel.Engineer)
            {
                values.UnionWith(new[]
                {
                    PlatformPermissionCodes.ProcessEdit,
                    PlatformPermissionCodes.VariableConfigure,
                    PlatformPermissionCodes.VariableDebug,
                    PlatformPermissionCodes.MotionOperate,
                    PlatformPermissionCodes.MotionConfigure,
                    PlatformPermissionCodes.IoDebug,
                    PlatformPermissionCodes.IoConfigure,
                    PlatformPermissionCodes.PlcOperate,
                    PlatformPermissionCodes.PlcConfigure,
                    PlatformPermissionCodes.CommunicationOperate,
                    PlatformPermissionCodes.CommunicationConfigure,
                    PlatformPermissionCodes.HardwareConfigure,
                    PlatformPermissionCodes.AlarmConfigure,
                    PlatformPermissionCodes.DataStructureConfigure,
                    PlatformPermissionCodes.PlatformEditorOpen,
                    PlatformPermissionCodes.PlatformDiagnosticsUse,
                    PlatformPermissionCodes.PlatformAiUse,
                    PlatformPermissionCodes.SourceReview
                });
            }
            return values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }
    }
}
