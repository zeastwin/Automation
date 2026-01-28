using System;

namespace Automation
{
    public interface IUserLoginStore
    {
        bool IsLoggedIn { get; }
        bool TryLogin(string userName, string password, out string error);
        bool TryLogout(out string error);
    }

    public sealed class UserLoginStore : IUserLoginStore
    {
        private readonly AccountStore accountStore;

        public UserLoginStore(AccountStore accountStore)
        {
            this.accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        }

        public bool IsLoggedIn => SF.userSession != null;

        public bool TryLogin(string userName, string password, out string error)
        {
            error = null;
            if (accountStore == null)
            {
                error = "账户系统未初始化";
                SF.SetSecurityLock(error);
                return false;
            }
            if (!accountStore.IsConfigValid)
            {
                if (!accountStore.IsDefaultCredential(userName, password))
                {
                    error = "锁定模式仅允许默认系统管理员登录";
                    return false;
                }
                UserAccount recovery = accountStore.CreateRecoveryAccount();
                SF.SetUserSession(new UserSession(recovery, true));
                return true;
            }
            if (!accountStore.TryAuthenticate(userName, password, out UserAccount account, out string authError))
            {
                error = authError ?? "登录失败";
                return false;
            }
            SF.SetUserSession(new UserSession(account, false));
            return true;
        }

        public bool TryLogout(out string error)
        {
            error = null;
            if (SF.userSession == null)
            {
                error = "当前未登录";
                return false;
            }
            SF.SetUserSession(null);
            return true;
        }
    }
}
