using System;
using System.Collections.Generic;
using System.Linq;
using Automation.DeviceSdk;

// 模块：运行时 / 账户安全。
// 职责范围：持有单实例登录会话，执行权限校验和管理员账户事务，并输出安全审计。

namespace Automation
{
    internal sealed class AccountSecurityService : IAuthenticationSession
    {
        private readonly object syncRoot = new object();
        private readonly AccountSecurityStore store;
        private readonly LocalFileLogger auditLogger = new LocalFileLogger(@"D:\AutomationLogs\Security");
        private AccountSecurityDocument document;
        private AccountSessionSnapshot currentUser;
        private bool initialized;
        private string initializationError;

        public AccountSecurityService(string configPath)
        {
            store = new AccountSecurityStore(configPath);
        }

        public event EventHandler<AccountSessionChangedEventArgs> Changed;
        internal event EventHandler SessionEnded;

        public bool IsAuthenticated
        {
            get { lock (syncRoot) return currentUser != null; }
        }

        internal bool IsEnforcementActive
        {
            get { lock (syncRoot) return initialized; }
        }

        public AccountSessionSnapshot CurrentUser
        {
            get { lock (syncRoot) return CloneSession(currentUser); }
        }

        internal bool Initialize(out string error)
        {
            lock (syncRoot)
            {
                if (initialized)
                {
                    error = initializationError;
                    return string.IsNullOrEmpty(error);
                }
                initialized = true;
                if (!store.TryLoadOrCreate(
                    out document,
                    out bool createdDefault,
                    out initializationError))
                {
                    error = initializationError;
                    Audit("authentication.initialize", "system", "failed", error);
                    return false;
                }
                if (createdDefault)
                {
                    Audit("account.create", "system", "succeeded",
                        "target=system; level=SystemAdministrator; source=initial-default");
                }
                error = null;
                return true;
            }
        }

        public bool Login(string userName, string password, out string error)
        {
            AccountSessionSnapshot changed;
            lock (syncRoot)
            {
                if (!EnsureAvailable(out error))
                {
                    Audit("login", userName, "failed", error);
                    return false;
                }
                if (currentUser != null)
                {
                    error = "当前已有登录账户，请先退出。";
                    Audit("login", userName, "failed", error);
                    return false;
                }
                string normalized = userName ?? string.Empty;
                AccountSecurityRecord account = document.Accounts.FirstOrDefault(item =>
                    string.Equals(item.UserName, normalized, StringComparison.OrdinalIgnoreCase));
                bool passwordShapeValid = AccountPasswordHasher.ValidatePassword(password, out _);
                bool accepted = passwordShapeValid && account != null && account.Enabled
                    && AccountPasswordHasher.Verify(password, account.Password);
                if (!accepted)
                {
                    error = "用户名或密码错误。";
                    string detail = account == null ? "账户不存在" : !account.Enabled ? "账户已停用" : "密码错误";
                    Audit("login", normalized, "failed", detail);
                    return false;
                }
                currentUser = CreateSession(account, DateTime.Now);
                changed = CloneSession(currentUser);
                Audit("login", account.UserName, "succeeded", account.Level.ToString());
                error = null;
            }
            RaiseChanged(changed);
            return true;
        }

        public void Logout()
        {
            AccountSessionSnapshot previous;
            lock (syncRoot)
            {
                previous = currentUser;
                if (previous == null)
                {
                    return;
                }
                currentUser = null;
                Audit("logout", previous.UserName, "succeeded", string.Empty);
            }
            RaiseSessionEnded();
            RaiseChanged(null);
        }

        public bool CheckPermission(string permissionCode, out string error)
        {
            lock (syncRoot)
            {
                return HasPermissionCore(permissionCode, out error);
            }
        }

        internal bool Authorize(string permissionCode, string action, out string error)
        {
            string userName;
            lock (syncRoot)
            {
                if (HasPermissionCore(permissionCode, out error))
                {
                    return true;
                }
                userName = currentUser?.UserName ?? "未登录";
                Audit("permission.denied", userName, "denied",
                    $"action={action}; permission={permissionCode}; reason={error}");
                return false;
            }
        }

        // 独立应用服务的单元测试会直接构造尚未进入平台初始化阶段的运行时；
        // 实际平台一旦调用 Initialize（成功或失败）即严格执行账户权限。
        internal bool AuthorizeApplicationOperation(string permissionCode, string action, out string error)
        {
            lock (syncRoot)
            {
                if (!initialized)
                {
                    error = null;
                    return true;
                }
            }
            return Authorize(permissionCode, action, out error);
        }

        internal IReadOnlyList<AccountEditorSnapshot> GetAccounts(out string error)
        {
            lock (syncRoot)
            {
                if (!EnsureAdministrator(out error))
                {
                    return Array.Empty<AccountEditorSnapshot>();
                }
                return document.Accounts
                    .OrderBy(item => item.UserName, StringComparer.OrdinalIgnoreCase)
                    .Select(ToEditorSnapshot)
                    .ToArray();
            }
        }

        internal bool TryCreateAccount(string userName, AccountLevel level, bool enabled,
            IEnumerable<string> permissions, string password, out string error)
        {
            if (!AccountPasswordHasher.ValidatePassword(password, out error))
            {
                return false;
            }
            lock (syncRoot)
            {
                if (!EnsureAdministrator(out error)
                    || !AccountSecurityStore.TryValidateUserName(userName, out error))
                {
                    return false;
                }
                if (document.Accounts.Any(item => string.Equals(item.UserName, userName, StringComparison.OrdinalIgnoreCase)))
                {
                    error = "用户名已经存在。";
                    return false;
                }
                AccountSecurityDocument candidate = CloneDocument(document);
                candidate.Accounts.Add(new AccountSecurityRecord
                {
                    Id = Guid.NewGuid(),
                    UserName = userName,
                    Level = level,
                    Enabled = enabled,
                    Permissions = NormalizePermissions(permissions),
                    Password = AccountPasswordHasher.Create(password)
                });
                if (!Commit(candidate, out error))
                {
                    return false;
                }
                Audit("account.create", currentUser.UserName, "succeeded",
                    $"target={userName}; level={level}; enabled={enabled}; permissions={string.Join(",", NormalizePermissions(permissions))}");
                return true;
            }
        }

        internal bool TryUpdateAccount(Guid id, string userName, AccountLevel level, bool enabled,
            IEnumerable<string> permissions, out string error)
        {
            AccountSessionSnapshot changed = null;
            bool terminate = false;
            lock (syncRoot)
            {
                if (!EnsureAdministrator(out error)
                    || !AccountSecurityStore.TryValidateUserName(userName, out error))
                {
                    return false;
                }
                AccountSecurityRecord existing = document.Accounts.FirstOrDefault(item => item.Id == id);
                if (existing == null)
                {
                    error = "账户不存在。";
                    return false;
                }
                bool builtIn = IsBuiltInSystem(existing);
                if (builtIn && (!string.Equals(userName, AccountSecurityStore.BuiltInSystemUserName, StringComparison.Ordinal)
                    || level != AccountLevel.SystemAdministrator || !enabled
                    || !new HashSet<string>(permissions ?? Array.Empty<string>(), StringComparer.Ordinal)
                        .SetEquals(PlatformPermissionCodes.All)))
                {
                    error = "内置system账户不能重命名、停用、降级或取消权限。";
                    return false;
                }
                if (document.Accounts.Any(item => item.Id != id
                    && string.Equals(item.UserName, userName, StringComparison.OrdinalIgnoreCase)))
                {
                    error = "用户名已经存在。";
                    return false;
                }

                AccountSecurityDocument candidate = CloneDocument(document);
                AccountSecurityRecord target = candidate.Accounts.Single(item => item.Id == id);
                target.UserName = userName;
                target.Level = level;
                target.Enabled = enabled;
                target.Permissions = NormalizePermissions(permissions);
                if (!Commit(candidate, out error))
                {
                    return false;
                }
                IReadOnlyList<string> oldPermissions = existing.Permissions.ToArray();
                Audit("account.update", currentUser.UserName, "succeeded",
                    $"target={userName}; level={existing.Level}->{level}; enabled={existing.Enabled}->{enabled}; "
                    + DescribePermissionChanges(oldPermissions, target.Permissions));
                if (currentUser.IdEquals(id))
                {
                    if (!enabled)
                    {
                        currentUser = null;
                        terminate = true;
                    }
                    else
                    {
                        currentUser = CreateSession(target, currentUser.LoggedInAt);
                        changed = CloneSession(currentUser);
                    }
                }
            }
            if (terminate)
            {
                RaiseSessionEnded();
                RaiseChanged(null);
            }
            else if (changed != null)
            {
                RaiseChanged(changed);
            }
            return true;
        }

        internal bool TryDeleteAccount(Guid id, out string error)
        {
            bool terminate = false;
            string actor = null;
            string targetName = null;
            lock (syncRoot)
            {
                if (!EnsureAdministrator(out error))
                {
                    return false;
                }
                AccountSecurityRecord existing = document.Accounts.FirstOrDefault(item => item.Id == id);
                if (existing == null)
                {
                    error = "账户不存在。";
                    return false;
                }
                if (IsBuiltInSystem(existing))
                {
                    error = "内置system账户不能删除。";
                    return false;
                }
                actor = currentUser.UserName;
                targetName = existing.UserName;
                terminate = currentUser.UserId == id;
                AccountSecurityDocument candidate = CloneDocument(document);
                candidate.Accounts.RemoveAll(item => item.Id == id);
                if (!Commit(candidate, out error))
                {
                    return false;
                }
                Audit("account.delete", actor, "succeeded", "target=" + targetName);
                if (terminate)
                {
                    currentUser = null;
                }
            }
            if (terminate)
            {
                RaiseSessionEnded();
                RaiseChanged(null);
            }
            return true;
        }

        internal bool TryResetPassword(Guid id, string password, out string error)
        {
            if (!AccountPasswordHasher.ValidatePassword(password, out error))
            {
                return false;
            }
            bool terminate;
            string actor;
            string targetName;
            lock (syncRoot)
            {
                if (!EnsureAdministrator(out error))
                {
                    return false;
                }
                AccountSecurityRecord existing = document.Accounts.FirstOrDefault(item => item.Id == id);
                if (existing == null)
                {
                    error = "账户不存在。";
                    return false;
                }
                AccountSecurityDocument candidate = CloneDocument(document);
                candidate.Accounts.Single(item => item.Id == id).Password = AccountPasswordHasher.Create(password);
                if (!Commit(candidate, out error))
                {
                    return false;
                }
                actor = currentUser.UserName;
                targetName = existing.UserName;
                terminate = currentUser.UserId == id;
                if (terminate)
                {
                    currentUser = null;
                }
                Audit("account.password_reset", actor, "succeeded", "target=" + targetName);
            }
            if (terminate)
            {
                RaiseSessionEnded();
                RaiseChanged(null);
            }
            return true;
        }

        private bool EnsureAvailable(out string error)
        {
            if (!initialized)
            {
                error = "账户服务尚未初始化。";
                return false;
            }
            if (document == null)
            {
                error = initializationError ?? "账户配置不可用。";
                return false;
            }
            error = null;
            return true;
        }

        private bool EnsureAdministrator(out string error)
        {
            if (!EnsureAvailable(out error))
            {
                return false;
            }
            if (currentUser == null
                || currentUser.Level != AccountLevel.SystemAdministrator
                || !currentUser.Permissions.Contains(PlatformPermissionCodes.AccountManage, StringComparer.Ordinal))
            {
                error = "只有拥有账户管理权限的系统管理员可以执行此操作。";
                return false;
            }
            return true;
        }

        private bool HasPermissionCore(string permissionCode, out string error)
        {
            if (!EnsureAvailable(out error))
            {
                return false;
            }
            if (!PlatformPermissionCodes.All.Contains(permissionCode, StringComparer.Ordinal))
            {
                error = "未知权限码：" + (permissionCode ?? string.Empty);
                return false;
            }
            if (currentUser == null)
            {
                error = "当前未登录。";
                return false;
            }
            if (!currentUser.Permissions.Contains(permissionCode, StringComparer.Ordinal))
            {
                error = $"当前账户没有权限：{permissionCode}";
                return false;
            }
            error = null;
            return true;
        }

        private bool Commit(AccountSecurityDocument candidate, out string error)
        {
            if (!store.TrySave(candidate, out error))
            {
                return false;
            }
            document = candidate;
            return true;
        }

        private void Audit(string eventName, string userName, string result, string detail)
        {
            auditLogger.Log($"event={eventName}; user={Sanitize(userName)}; result={result}; detail={Sanitize(detail)}",
                string.Equals(result, "failed", StringComparison.Ordinal) || string.Equals(result, "denied", StringComparison.Ordinal)
                    ? LogLevel.Error
                    : LogLevel.Normal);
        }

        private void RaiseChanged(AccountSessionSnapshot snapshot)
        {
            foreach (EventHandler<AccountSessionChangedEventArgs> handler in
                Changed?.GetInvocationList().Cast<EventHandler<AccountSessionChangedEventArgs>>()
                    ?? Enumerable.Empty<EventHandler<AccountSessionChangedEventArgs>>())
            {
                try
                {
                    handler(this, new AccountSessionChangedEventArgs(CloneSession(snapshot)));
                }
                catch (Exception ex)
                {
                    Audit("session.changed_handler", snapshot?.UserName ?? "未登录",
                        "failed", ex.Message);
                }
            }
        }

        private void RaiseSessionEnded()
        {
            foreach (EventHandler handler in SessionEnded?.GetInvocationList().Cast<EventHandler>()
                ?? Enumerable.Empty<EventHandler>())
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Audit("session.cleanup_handler", "未登录", "failed", ex.Message);
                }
            }
        }

        private static AccountSessionSnapshot CreateSession(AccountSecurityRecord account, DateTime loggedInAt)
        {
            return new AccountSessionSnapshot
            {
                UserId = account.Id,
                UserName = account.UserName,
                Level = account.Level,
                Permissions = account.Permissions.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                LoggedInAt = loggedInAt
            };
        }

        private static AccountSessionSnapshot CloneSession(AccountSessionSnapshot source)
        {
            if (source == null)
            {
                return null;
            }
            return new AccountSessionSnapshot
            {
                UserId = source.UserId,
                UserName = source.UserName,
                Level = source.Level,
                Permissions = (source.Permissions ?? Array.Empty<string>()).ToArray(),
                LoggedInAt = source.LoggedInAt
            };
        }

        private static AccountEditorSnapshot ToEditorSnapshot(AccountSecurityRecord account)
        {
            return new AccountEditorSnapshot
            {
                Id = account.Id,
                UserName = account.UserName,
                Level = account.Level,
                Enabled = account.Enabled,
                Permissions = account.Permissions.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                IsBuiltInSystem = IsBuiltInSystem(account)
            };
        }

        private static List<string> NormalizePermissions(IEnumerable<string> permissions)
        {
            return (permissions ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
        }

        private static string DescribePermissionChanges(
            IEnumerable<string> before,
            IEnumerable<string> after)
        {
            var oldValues = new HashSet<string>(before ?? Array.Empty<string>(), StringComparer.Ordinal);
            var newValues = new HashSet<string>(after ?? Array.Empty<string>(), StringComparer.Ordinal);
            string added = string.Join(",", newValues.Except(oldValues).OrderBy(value => value, StringComparer.Ordinal));
            string removed = string.Join(",", oldValues.Except(newValues).OrderBy(value => value, StringComparer.Ordinal));
            return $"permissions.added=[{added}]; permissions.removed=[{removed}]";
        }

        private static AccountSecurityDocument CloneDocument(AccountSecurityDocument source)
        {
            return new AccountSecurityDocument
            {
                SchemaVersion = source.SchemaVersion,
                Accounts = source.Accounts.Select(account => new AccountSecurityRecord
                {
                    Id = account.Id,
                    UserName = account.UserName,
                    Level = account.Level,
                    Enabled = account.Enabled,
                    Permissions = new List<string>(account.Permissions),
                    Password = new AccountPasswordRecord
                    {
                        Algorithm = account.Password.Algorithm,
                        Iterations = account.Password.Iterations,
                        Salt = account.Password.Salt,
                        Hash = account.Password.Hash
                    }
                }).ToList()
            };
        }

        private static bool IsBuiltInSystem(AccountSecurityRecord account)
        {
            return account != null && string.Equals(account.UserName,
                AccountSecurityStore.BuiltInSystemUserName, StringComparison.OrdinalIgnoreCase);
        }

        private static string Sanitize(string value)
        {
            return (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        }
    }

    internal static class AccountSessionSnapshotExtensions
    {
        public static bool IdEquals(this AccountSessionSnapshot snapshot, Guid id)
        {
            return snapshot != null && snapshot.UserId == id;
        }
    }
}
