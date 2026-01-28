using System;

namespace Automation
{
    public sealed class UserContextSnapshot
    {
        public bool IsLoggedIn { get; set; }
        public bool IsSecurityLocked { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public UserRole? Role { get; set; }
        public bool IsRecovery { get; set; }
    }

    public interface IUserContextStore
    {
        bool IsLoggedIn { get; }
        bool IsSecurityLocked { get; }
        string GetUserName();
        UserRole? GetUserRole();
        UserContextSnapshot GetSnapshot();
    }

    public sealed class UserContextStore : IUserContextStore
    {
        private readonly Func<UserSession> sessionProvider;
        private readonly Func<bool> lockProvider;

        public UserContextStore(Func<UserSession> sessionProvider, Func<bool> lockProvider)
        {
            this.sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
            this.lockProvider = lockProvider ?? throw new ArgumentNullException(nameof(lockProvider));
        }

        public bool IsLoggedIn => sessionProvider() != null;

        public bool IsSecurityLocked => lockProvider();

        public string GetUserName()
        {
            return sessionProvider()?.Account?.UserName ?? string.Empty;
        }

        public UserRole? GetUserRole()
        {
            UserSession session = sessionProvider();
            if (session == null)
            {
                return null;
            }
            return session.Account?.Role;
        }

        public UserContextSnapshot GetSnapshot()
        {
            UserSession session = sessionProvider();
            bool locked = lockProvider();
            if (session == null || session.Account == null)
            {
                return new UserContextSnapshot
                {
                    IsLoggedIn = false,
                    IsSecurityLocked = locked,
                    UserId = string.Empty,
                    UserName = string.Empty,
                    Role = null,
                    IsRecovery = false
                };
            }

            return new UserContextSnapshot
            {
                IsLoggedIn = true,
                IsSecurityLocked = locked,
                UserId = session.Account.Id ?? string.Empty,
                UserName = session.Account.UserName ?? string.Empty,
                Role = session.Account.Role,
                IsRecovery = session.IsRecovery
            };
        }
    }
}
