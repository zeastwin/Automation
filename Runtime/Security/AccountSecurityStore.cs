using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Automation.DeviceSdk;

// 模块：运行时 / 账户安全。
// 职责范围：严格加载、校验和原子保存账户文件；损坏文件不得退化为已知默认密码。

namespace Automation
{
    internal sealed class AccountSecurityStore
    {
        internal const int CurrentSchemaVersion = 1;
        internal const string FileName = "AccountSecurity";
        internal const string BuiltInSystemUserName = "system";
        internal const string BuiltInSystemDefaultPassword = "software_123";

        private readonly string directory;
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        public AccountSecurityStore(string directory)
        {
            this.directory = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
        }

        public bool TryLoadOrCreate(
            out AccountSecurityDocument document,
            out bool createdDefault,
            out string error)
        {
            document = null;
            createdDefault = false;
            error = null;
            string primary = Path.Combine(directory, FileName + ".json");
            string backup = primary + ".bak";
            bool primaryExists = File.Exists(primary);
            bool backupExists = File.Exists(backup);
            if (!primaryExists && !backupExists)
            {
                document = CreateDefaultDocument();
                if (!TrySave(document, out error))
                {
                    document = null;
                    return false;
                }
                createdDefault = true;
                return true;
            }

            if (TryRead(primary, out document, out string primaryError))
            {
                return true;
            }
            if (TryRead(backup, out document, out string backupError))
            {
                if (!TryRestorePrimary(primary, document, out error))
                {
                    document = null;
                    return false;
                }
                return true;
            }
            error = "账户配置及备份均不可用，认证已关闭。"
                + $" primary={primaryError ?? "不存在"}; backup={backupError ?? "不存在"}";
            document = null;
            return false;
        }

        public bool TrySave(AccountSecurityDocument document, out string error)
        {
            error = null;
            if (!TryValidate(document, out error))
            {
                return false;
            }
            try
            {
                if (!AtomicJsonFileStore.Save(directory, FileName, document, SerializerSettings))
                {
                    error = "账户配置写入失败。";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "账户配置写入失败：" + ex.Message;
                return false;
            }
        }

        public static bool TryValidate(AccountSecurityDocument document, out string error)
        {
            error = null;
            if (document == null || document.SchemaVersion != CurrentSchemaVersion || document.Accounts == null)
            {
                error = "账户配置版本或结构无效。";
                return false;
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<Guid>();
            HashSet<string> knownPermissions = new HashSet<string>(PlatformPermissionCodes.All, StringComparer.Ordinal);
            foreach (AccountSecurityRecord account in document.Accounts)
            {
                if (account == null || account.Id == Guid.Empty || !ids.Add(account.Id))
                {
                    error = "账户配置包含空账户或重复账户ID。";
                    return false;
                }
                if (!TryValidateUserName(account.UserName, out error) || !names.Add(account.UserName))
                {
                    error = error ?? $"账户名重复：{account.UserName}";
                    return false;
                }
                if (!Enum.IsDefined(typeof(AccountLevel), account.Level))
                {
                    error = $"账户级别无效：{account.UserName}";
                    return false;
                }
                if (account.Password == null
                    || !string.Equals(account.Password.Algorithm, AccountPasswordHasher.AlgorithmName, StringComparison.Ordinal)
                    || account.Password.Iterations != AccountPasswordHasher.IterationCount
                    || !TryReadBase64(account.Password.Salt, 16)
                    || !TryReadBase64(account.Password.Hash, 32))
                {
                    error = $"账户密码摘要无效：{account.UserName}";
                    return false;
                }
                if (account.Permissions == null
                    || account.Permissions.Any(value => !knownPermissions.Contains(value))
                    || account.Permissions.Distinct(StringComparer.Ordinal).Count() != account.Permissions.Count)
                {
                    error = $"账户权限配置无效：{account.UserName}";
                    return false;
                }
            }

            AccountSecurityRecord system = document.Accounts.SingleOrDefault(account =>
                string.Equals(account.UserName, BuiltInSystemUserName, StringComparison.OrdinalIgnoreCase));
            if (system == null
                || !string.Equals(system.UserName, BuiltInSystemUserName, StringComparison.Ordinal)
                || !system.Enabled
                || system.Level != AccountLevel.SystemAdministrator
                || !new HashSet<string>(system.Permissions, StringComparer.Ordinal)
                    .SetEquals(PlatformPermissionCodes.All))
            {
                error = "内置system账户必须保持启用、系统管理员级别和完整权限。";
                return false;
            }
            return true;
        }

        public static bool TryValidateUserName(string userName, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(userName))
            {
                error = "用户名不能为空。";
                return false;
            }
            if (userName.Length > 64)
            {
                error = "用户名不能超过64个字符。";
                return false;
            }
            if (userName.Any(char.IsControl))
            {
                error = "用户名不能包含控制字符。";
                return false;
            }
            return true;
        }

        private bool TryRestorePrimary(
            string primary,
            AccountSecurityDocument recovered,
            out string error)
        {
            string temp = primary + "." + Guid.NewGuid().ToString("N") + ".tmp";
            error = null;
            try
            {
                Directory.CreateDirectory(directory);
                string json = JsonConvert.SerializeObject(recovered, SerializerSettings);
                AtomicJsonFileStore.WriteDurable(temp, json);
                if (File.Exists(primary))
                {
                    // 不把已经损坏的主文件覆盖到有效备份上。
                    File.Replace(temp, primary, null, true);
                }
                else
                {
                    File.Move(temp, primary);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "账户配置已从备份读取，但恢复主文件失败，认证已关闭：" + ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch
                {
                }
            }
        }

        private AccountSecurityDocument CreateDefaultDocument()
        {
            return new AccountSecurityDocument
            {
                Accounts = new List<AccountSecurityRecord>
                {
                    new AccountSecurityRecord
                    {
                        Id = Guid.NewGuid(),
                        UserName = BuiltInSystemUserName,
                        Level = AccountLevel.SystemAdministrator,
                        Enabled = true,
                        Permissions = PlatformPermissionCodes.All.OrderBy(value => value, StringComparer.Ordinal).ToList(),
                        Password = AccountPasswordHasher.Create(BuiltInSystemDefaultPassword)
                    }
                }
            };
        }

        private static bool TryRead(string path, out AccountSecurityDocument document, out string error)
        {
            document = null;
            error = null;
            if (!File.Exists(path))
            {
                return false;
            }
            try
            {
                document = JsonConvert.DeserializeObject<AccountSecurityDocument>(
                    File.ReadAllText(path, Encoding.UTF8), SerializerSettings);
                if (!TryValidate(document, out error))
                {
                    document = null;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                document = null;
                return false;
            }
        }

        private static bool TryReadBase64(string value, int expectedLength)
        {
            try
            {
                return Convert.FromBase64String(value ?? string.Empty).Length == expectedLength;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
