// 模块：核心测试 / 账户安全。
// 职责范围：固化默认账户、密码摘要、完整权限、损坏恢复和会话失效契约。

using System;
using System.IO;
using System.Linq;
using System.Text;
using Automation.Bridge;
using Automation.DeviceSdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class AccountSecurityTests
    {
        [TestMethod]
        public void MissingConfiguration_CreatesProtectedSystemAccountOnce()
        {
            using (var directory = new TemporaryDirectory())
            {
                var accounts = new AccountSecurityService(directory.FullPath);
                Assert.IsTrue(accounts.Initialize(out string error), error);

                string path = Path.Combine(directory.FullPath, "AccountSecurity.json");
                Assert.IsTrue(File.Exists(path));
                string json = File.ReadAllText(path, Encoding.UTF8);
                Assert.IsFalse(json.Contains(AccountSecurityStore.BuiltInSystemDefaultPassword));
                StringAssert.Contains(json, AccountPasswordHasher.AlgorithmName);
                StringAssert.Contains(json, AccountPasswordHasher.IterationCount.ToString());

                Assert.IsFalse(accounts.Login("system", "错误密码", out _));
                Assert.IsTrue(accounts.Login(
                    "SYSTEM",
                    AccountSecurityStore.BuiltInSystemDefaultPassword,
                    out error), error);
                Assert.AreEqual(AccountLevel.SystemAdministrator, accounts.CurrentUser.Level);
                CollectionAssert.AreEquivalent(
                    PlatformPermissionCodes.All.ToArray(),
                    accounts.CurrentUser.Permissions.ToArray());
                Assert.IsFalse(accounts.Login("system", AccountSecurityStore.BuiltInSystemDefaultPassword, out _));

                accounts.Logout();
                var reloaded = new AccountSecurityService(directory.FullPath);
                Assert.IsTrue(reloaded.Initialize(out error), error);
                Assert.IsTrue(reloaded.Login(
                    "system",
                    AccountSecurityStore.BuiltInSystemDefaultPassword,
                    out error), error);
            }
        }

        [TestMethod]
        public void PasswordHasher_UsesRandomSaltAndSupportsUnicodeBounds()
        {
            const string password = "工艺密码🔒";
            AccountPasswordRecord first = AccountPasswordHasher.Create(password);
            AccountPasswordRecord second = AccountPasswordHasher.Create(password);

            Assert.AreNotEqual(first.Salt, second.Salt);
            Assert.AreNotEqual(first.Hash, second.Hash);
            Assert.IsTrue(AccountPasswordHasher.Verify(password, first));
            Assert.IsFalse(AccountPasswordHasher.Verify(password + "x", first));
            Assert.IsFalse(AccountPasswordHasher.ValidatePassword(string.Empty, out _));
            Assert.IsFalse(AccountPasswordHasher.ValidatePassword(new string('密', 129), out _));
        }

        [TestMethod]
        public void AccountCrud_PersistsCompleteCustomPermissionsAndInvalidatesDeletedSession()
        {
            using (var directory = new TemporaryDirectory())
            {
                var accounts = InitializeAndLoginSystem(directory.FullPath);
                string[] customPermissions =
                {
                    PlatformPermissionCodes.ProcessRun,
                    PlatformPermissionCodes.IoDebug
                };
                Assert.IsTrue(accounts.TryCreateAccount(
                    "LineUser",
                    AccountLevel.Operator,
                    true,
                    customPermissions,
                    "现场口令✓",
                    out string error), error);
                AccountEditorSnapshot created = accounts.GetAccounts(out error)
                    .Single(item => item.UserName == "LineUser");
                CollectionAssert.AreEquivalent(customPermissions, created.Permissions.ToArray());

                accounts.Logout();
                Assert.IsTrue(accounts.Login("lineuser", "现场口令✓", out error), error);
                Assert.IsTrue(accounts.CheckPermission(PlatformPermissionCodes.IoDebug, out error), error);
                Assert.IsFalse(accounts.CheckPermission(PlatformPermissionCodes.VariableRuntimeWrite, out _));
                accounts.Logout();

                Assert.IsTrue(accounts.Login(
                    "system",
                    AccountSecurityStore.BuiltInSystemDefaultPassword,
                    out error), error);
                string[] engineerDefaults = AccountPermissionDefaults.ForLevel(AccountLevel.Engineer).ToArray();
                Assert.IsTrue(accounts.TryUpdateAccount(
                    created.Id,
                    created.UserName,
                    AccountLevel.Engineer,
                    true,
                    engineerDefaults,
                    out error), error);

                var reloaded = new AccountSecurityService(directory.FullPath);
                Assert.IsTrue(reloaded.Initialize(out error), error);
                Assert.IsTrue(reloaded.Login(
                    "system",
                    AccountSecurityStore.BuiltInSystemDefaultPassword,
                    out error), error);
                AccountEditorSnapshot persisted = reloaded.GetAccounts(out error)
                    .Single(item => item.UserName == "LineUser");
                Assert.AreEqual(AccountLevel.Engineer, persisted.Level);
                CollectionAssert.AreEquivalent(engineerDefaults, persisted.Permissions.ToArray());

                Assert.IsTrue(reloaded.TryCreateAccount(
                    "BackupAdmin",
                    AccountLevel.SystemAdministrator,
                    true,
                    PlatformPermissionCodes.All,
                    "管理口令",
                    out error), error);
                Guid adminId = reloaded.GetAccounts(out error)
                    .Single(item => item.UserName == "BackupAdmin").Id;
                reloaded.Logout();
                Assert.IsTrue(reloaded.Login("backupadmin", "管理口令", out error), error);
                Assert.IsTrue(reloaded.TryDeleteAccount(adminId, out error), error);
                Assert.IsFalse(reloaded.IsAuthenticated);
            }
        }

        [TestMethod]
        public void BuiltInSystem_IsImmutableAndPasswordResetEndsSession()
        {
            using (var directory = new TemporaryDirectory())
            {
                var accounts = InitializeAndLoginSystem(directory.FullPath);
                AccountEditorSnapshot system = accounts.GetAccounts(out string error).Single();

                Assert.IsFalse(accounts.TryUpdateAccount(
                    system.Id,
                    system.UserName,
                    AccountLevel.Engineer,
                    true,
                    AccountPermissionDefaults.ForLevel(AccountLevel.Engineer),
                    out error));
                Assert.IsFalse(accounts.TryDeleteAccount(system.Id, out error));
                Assert.IsTrue(accounts.TryResetPassword(system.Id, "新密码", out error), error);
                Assert.IsFalse(accounts.IsAuthenticated);
                Assert.IsFalse(accounts.Login(
                    "system",
                    AccountSecurityStore.BuiltInSystemDefaultPassword,
                    out _));
                Assert.IsTrue(accounts.Login("system", "新密码", out error), error);
            }
        }

        [TestMethod]
        public void CorruptPrimaryAndBackup_DisablesAuthenticationWithoutRecreatingDefault()
        {
            using (var directory = new TemporaryDirectory())
            {
                var accounts = InitializeAndLoginSystem(directory.FullPath);
                Assert.IsTrue(accounts.TryCreateAccount(
                    "CreateBackup",
                    AccountLevel.Operator,
                    true,
                    AccountPermissionDefaults.ForLevel(AccountLevel.Operator),
                    "密码",
                    out string error), error);
                accounts.Logout();

                string primary = Path.Combine(directory.FullPath, "AccountSecurity.json");
                string backup = primary + ".bak";
                Assert.IsTrue(File.Exists(backup));
                File.WriteAllText(primary, "{broken-primary", new UTF8Encoding(false));
                File.WriteAllText(backup, "{broken-backup", new UTF8Encoding(false));

                var corrupted = new AccountSecurityService(directory.FullPath);
                Assert.IsFalse(corrupted.Initialize(out error));
                StringAssert.Contains(error, "认证已关闭");
                Assert.IsFalse(corrupted.Login(
                    "system",
                    AccountSecurityStore.BuiltInSystemDefaultPassword,
                    out _));
                Assert.AreEqual("{broken-primary", File.ReadAllText(primary, Encoding.UTF8));
            }
        }

        [TestMethod]
        public void CorruptPrimary_UsesLastValidBackup()
        {
            using (var directory = new TemporaryDirectory())
            {
                var accounts = InitializeAndLoginSystem(directory.FullPath);
                Assert.IsTrue(accounts.TryCreateAccount(
                    "CreatesBackup",
                    AccountLevel.Operator,
                    true,
                    AccountPermissionDefaults.ForLevel(AccountLevel.Operator),
                    "密码",
                    out string error), error);
                accounts.Logout();

                string primary = Path.Combine(directory.FullPath, "AccountSecurity.json");
                Assert.IsTrue(File.Exists(primary + ".bak"));
                File.WriteAllText(primary, "{broken-primary", new UTF8Encoding(false));

                var recovered = new AccountSecurityService(directory.FullPath);
                Assert.IsTrue(recovered.Initialize(out error), error);
                Assert.IsTrue(recovered.Login(
                    "system",
                    AccountSecurityStore.BuiltInSystemDefaultPassword,
                    out error), error);
                Assert.AreEqual(1, recovered.GetAccounts(out error).Count,
                    "备份应是上一次完整提交，不能混入损坏主文件的半成品。");
                Assert.IsTrue(AccountSecurityStore.TryValidate(
                    JsonConvert.DeserializeObject<AccountSecurityDocument>(
                        File.ReadAllText(primary, Encoding.UTF8)),
                    out error), error);
                Assert.IsTrue(AccountSecurityStore.TryValidate(
                    JsonConvert.DeserializeObject<AccountSecurityDocument>(
                        File.ReadAllText(primary + ".bak", Encoding.UTF8)),
                    out error), error);
            }
        }

        [TestMethod]
        public void DefaultPermissions_AreCopiedByLevelWithoutInheritanceState()
        {
            string[] operatorPermissions = AccountPermissionDefaults.ForLevel(AccountLevel.Operator).ToArray();
            CollectionAssert.AreEquivalent(new[]
            {
                PlatformPermissionCodes.ProcessRun,
                PlatformPermissionCodes.VariableRuntimeWrite
            }, operatorPermissions);

            string[] engineerPermissions = AccountPermissionDefaults.ForLevel(AccountLevel.Engineer).ToArray();
            Assert.IsTrue(engineerPermissions.Contains(PlatformPermissionCodes.PlatformEditorOpen));
            Assert.IsTrue(engineerPermissions.Contains(PlatformPermissionCodes.SourceReview));
            Assert.IsFalse(engineerPermissions.Contains(PlatformPermissionCodes.SourceDevelop));
            Assert.IsFalse(engineerPermissions.Contains(PlatformPermissionCodes.ApplicationConfigure));
            Assert.IsFalse(engineerPermissions.Contains(PlatformPermissionCodes.VersionManage));
            Assert.IsFalse(engineerPermissions.Contains(PlatformPermissionCodes.AccountManage));

            CollectionAssert.AreEquivalent(
                PlatformPermissionCodes.All.ToArray(),
                AccountPermissionDefaults.ForLevel(AccountLevel.SystemAdministrator).ToArray());
        }

        [TestMethod]
        public void PermissionDenied_RuntimeVariableWriteHasNoSideEffect()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                    10,
                    "运行参数",
                    "double",
                    "1",
                    string.Empty));
                Assert.IsTrue(runtime.Accounts.Initialize(out string error), error);
                var editor = new VariableEditorService(runtime, () => Array.Empty<Proc>());
                DicValue variable = runtime.Stores.Values.GetValueByIndex(10);

                Assert.IsFalse(editor.TrySetRuntimeValue(variable, "2", out error));
                StringAssert.Contains(error, "未登录");
                Assert.AreEqual("1", runtime.Stores.Values.GetValueByIndex(10).Value);

                Assert.IsTrue(runtime.Accounts.Login(
                    "system",
                    AccountSecurityStore.BuiltInSystemDefaultPassword,
                    out error), error);
                Assert.IsTrue(editor.TrySetRuntimeValue(variable, "2", out error), error);
                Assert.AreEqual("2", runtime.Stores.Values.GetValueByIndex(10).Value);
            }
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void PermissionDenied_BridgeWriteHasNoSideEffect()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                {
                    var runtime = new PlatformRuntime(directory.FullPath);
                    Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                        10,
                        "Bridge运行参数",
                        "double",
                        "1",
                        string.Empty));
                    Assert.IsTrue(runtime.Accounts.Initialize(out string error), error);
                    using (var form = new FrmMain(runtime))
                    {
                        var bridge = new AutomationBridgeService(form);
                        AutomationBridgeResponse response = bridge.Handle(
                            "POST",
                            "/bridge/variable/set",
                            new JObject
                            {
                                ["name"] = "Bridge运行参数",
                                ["value"] = "2"
                            }.ToString(Formatting.None));

                        Assert.AreEqual(403, response.StatusCode);
                        Assert.AreEqual("ACCOUNT_PERMISSION_DENIED",
                            JObject.Parse(response.Body)["errorCode"]?.Value<string>());
                        Assert.AreEqual("1", runtime.Stores.Values.GetValueByIndex(10).Value);
                    }
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        public void PermissionDenied_ConfigurationStoresHaveNoSideEffect()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(runtime.Stores.DataStructures.AddStruct(
                    "已有结构",
                    out string error), error);
                Assert.IsTrue(runtime.Accounts.Initialize(out error), error);

                Assert.IsFalse(runtime.Stores.DataStructures.AddStruct(
                    "越权结构",
                    out error));
                StringAssert.Contains(error, "未登录");
                CollectionAssert.AreEqual(
                    new[] { "已有结构" },
                    runtime.Stores.DataStructures.GetStructNames());

                Assert.IsFalse(runtime.Stores.Alarms.TryUpdateAlarm(
                    0, "越权报警", null, null, null, null, "不应写入", out error));
                StringAssert.Contains(error, "未登录");
                Assert.IsTrue(runtime.Stores.Alarms.TryGetByIndex(0, out AlarmInfo alarm));
                Assert.IsTrue(string.IsNullOrEmpty(alarm.Name));
                Assert.IsTrue(string.IsNullOrEmpty(alarm.Note));
            }
        }

        private static AccountSecurityService InitializeAndLoginSystem(string path)
        {
            var accounts = new AccountSecurityService(path);
            Assert.IsTrue(accounts.Initialize(out string error), error);
            Assert.IsTrue(accounts.Login(
                "system",
                AccountSecurityStore.BuiltInSystemDefaultPassword,
                out error), error);
            return accounts;
        }
    }

    [TestClass]
    public sealed class AuthenticationSdkContractTests
    {
        [TestMethod]
        public void Sdk8_ExposesSessionOnlyAndStablePermissionCodes()
        {
            object apiVersion = typeof(PlatformApiInfo)
                .GetField(nameof(PlatformApiInfo.ApiVersion))
                ?.GetRawConstantValue();
            Assert.AreEqual("8.0", apiVersion);
            Assert.IsNotNull(typeof(IAutomationPlatform).GetProperty(nameof(IAutomationPlatform.Authentication)));
            Assert.AreEqual(
                PlatformPermissionCodes.All.Count,
                PlatformPermissionCodes.All.Distinct(StringComparer.Ordinal).Count());
            Assert.IsNull(typeof(IAuthenticationSession).GetMethod("CreateAccount"));
            Assert.IsNull(typeof(IAuthenticationSession).GetMethod("ResetPassword"));
            Assert.IsNotNull(typeof(IAuthenticationSession).GetMethod(nameof(IAuthenticationSession.Login)));
            Assert.IsNotNull(typeof(IAuthenticationSession).GetMethod(nameof(IAuthenticationSession.Logout)));
            Assert.IsNotNull(typeof(IAuthenticationSession).GetMethod(nameof(IAuthenticationSession.CheckPermission)));
        }
    }
}
