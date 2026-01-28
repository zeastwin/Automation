using System;
using System.Collections.Generic;

namespace Automation
{
    public enum UserRole
    {
        SystemAdmin = 0,
        Admin = 1,
        Operator = 2
    }

    public sealed class UserAccount
    {
        public string Id { get; set; }
        public string UserName { get; set; }
        public UserRole Role { get; set; }
        public string PasswordHash { get; set; }
        public string Salt { get; set; }
        public int Iterations { get; set; }
        public bool Disabled { get; set; }
        public List<string> Permissions { get; set; } = new List<string>();
    }

    public sealed class UserSession
    {
        private readonly HashSet<string> permissions;

        public UserSession(UserAccount account, bool isRecovery)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
            IsRecovery = isRecovery;
            permissions = new HashSet<string>(account.Permissions ?? new List<string>(), StringComparer.Ordinal);
        }

        public UserAccount Account { get; }
        public bool IsRecovery { get; }

        public bool HasPermission(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }
            if (IsRecovery)
            {
                return string.Equals(key, PermissionKeys.AccountManage, StringComparison.Ordinal);
            }
            if (Account.Role == UserRole.SystemAdmin)
            {
                return true;
            }
            return permissions.Contains(key);
        }
    }
}
