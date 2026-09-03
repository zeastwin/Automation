using System;
using System.Collections.Generic;

// 模块：设备 SDK / 账户认证公开契约。
// 职责范围：设备 HMI 通过本文件登录、退出并查询当前账户权限；账户配置不通过 SDK 暴露。

namespace Automation.DeviceSdk
{
    /// <summary>平台内置的三个账户级别；实际权限以账户保存的完整权限集合为准。</summary>
    public enum AccountLevel
    {
        Operator = 0,
        Engineer = 1,
        SystemAdministrator = 2
    }

    /// <summary>稳定权限码。调用方不得自行拼接未登记的权限名称。</summary>
    public static class PlatformPermissionCodes
    {
        public const string ProcessRun = "process.run";
        public const string ProcessEdit = "process.edit";
        public const string VariableRuntimeWrite = "variable.runtime.write";
        public const string VariableConfigure = "variable.configure";
        public const string VariableDebug = "variable.debug";
        public const string MotionOperate = "motion.operate";
        public const string MotionConfigure = "motion.configure";
        public const string IoDebug = "io.debug";
        public const string IoConfigure = "io.configure";
        public const string PlcOperate = "plc.operate";
        public const string PlcConfigure = "plc.configure";
        public const string CommunicationOperate = "communication.operate";
        public const string CommunicationConfigure = "communication.configure";
        public const string HardwareConfigure = "hardware.configure";
        public const string AlarmConfigure = "alarm.configure";
        public const string DataStructureConfigure = "data_structure.configure";
        public const string PlatformEditorOpen = "platform.editor.open";
        public const string PlatformDiagnosticsUse = "platform.diagnostics.use";
        public const string PlatformAiUse = "platform.ai.use";
        public const string SourceReview = "source.review";
        public const string SourceDevelop = "source.develop";
        public const string ApplicationConfigure = "application.configure";
        public const string VersionManage = "version.manage";
        public const string AccountManage = "account.manage";

        private static readonly string[] AllValues =
        {
            ProcessRun,
            ProcessEdit,
            VariableRuntimeWrite,
            VariableConfigure,
            VariableDebug,
            MotionOperate,
            MotionConfigure,
            IoDebug,
            IoConfigure,
            PlcOperate,
            PlcConfigure,
            CommunicationOperate,
            CommunicationConfigure,
            HardwareConfigure,
            AlarmConfigure,
            DataStructureConfigure,
            PlatformEditorOpen,
            PlatformDiagnosticsUse,
            PlatformAiUse,
            SourceReview,
            SourceDevelop,
            ApplicationConfigure,
            VersionManage,
            AccountManage
        };

        /// <summary>返回全部已登记权限码的独立快照。</summary>
        public static IReadOnlyList<string> All => (string[])AllValues.Clone();
    }

    /// <summary>当前登录账户的只读快照；不包含任何密码验证数据。</summary>
    public sealed class AccountSessionSnapshot
    {
        public Guid UserId { get; set; }
        public string UserName { get; set; }
        public AccountLevel Level { get; set; }
        public IReadOnlyList<string> Permissions { get; set; }
        public DateTime LoggedInAt { get; set; }
    }

    /// <summary>登录、退出或当前账户配置变化时发布的会话事实。</summary>
    public sealed class AccountSessionChangedEventArgs : EventArgs
    {
        public AccountSessionChangedEventArgs(AccountSessionSnapshot currentUser)
        {
            CurrentUser = currentUser;
        }

        public AccountSessionSnapshot CurrentUser { get; }
    }

    /// <summary>单个平台实例的账户会话。切换账户前必须先退出当前账户。</summary>
    public interface IAuthenticationSession
    {
        event EventHandler<AccountSessionChangedEventArgs> Changed;

        bool IsAuthenticated { get; }

        AccountSessionSnapshot CurrentUser { get; }

        bool Login(string userName, string password, out string error);

        void Logout();

        bool CheckPermission(string permissionCode, out string error);
    }
}
