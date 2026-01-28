using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation
{
    public enum AccountLoadResult
    {
        Loaded = 0,
        CreatedDefault = 1,
        Invalid = 2
    }

    public sealed class AccountStore
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int DefaultIterations = 20000;
        private const int MaxNameLength = 32;
        private const int EncryptSaltSize = 16;
        private const int EncryptIvSize = 16;
        private const int EncryptMacSize = 32;
        private const int EncryptIterations = 20000;
        private const string EncryptPrefix = "ENC:";
        private static readonly byte[] AccountMasterKey = Convert.FromBase64String("BJXo9pf2eqjz5ves/l3eLKd27xGq4/ECsw99mTw6teY=");

        private readonly List<UserAccount> accounts = new List<UserAccount>();
        private readonly Dictionary<string, UserAccount> accountByName = new Dictionary<string, UserAccount>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> accountIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<UserAccount> Accounts => accounts;
        public bool IsConfigValid { get; private set; } = true;
        public string ConfigError { get; private set; }
        public string DefaultUserName { get; private set; }
        public string DefaultPassword { get; private set; }

        public bool IsSystemUserName(string userName)
        {
            return !string.IsNullOrWhiteSpace(DefaultUserName)
                && !string.IsNullOrWhiteSpace(userName)
                && string.Equals(userName, DefaultUserName, StringComparison.OrdinalIgnoreCase);
        }

        public AccountLoadResult Load(string configPath, string defaultUserName, string defaultPassword, out string error)
        {
            error = null;
            DefaultUserName = defaultUserName;
            DefaultPassword = defaultPassword;

            if (!ValidateUserName(defaultUserName, out string userError))
            {
                error = $"默认管理员用户名无效:{userError}";
                IsConfigValid = false;
                ConfigError = error;
                return AccountLoadResult.Invalid;
            }
            if (!TryValidatePassword(defaultPassword, out string passError))
            {
                error = $"默认管理员口令无效:{passError}";
                IsConfigValid = false;
                ConfigError = error;
                return AccountLoadResult.Invalid;
            }

            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            string path = Path.Combine(configPath, "Account.json");
            if (!File.Exists(path))
            {
                accounts.Clear();
                accountByName.Clear();
                accountIds.Clear();
                UserAccount defaultAccount = CreateDefaultAdmin(defaultUserName, defaultPassword);
                accounts.Add(defaultAccount);
                accountByName[defaultAccount.UserName] = defaultAccount;
                accountIds.Add(defaultAccount.Id);
                if (!TrySaveInternal(path, out error))
                {
                    IsConfigValid = false;
                    ConfigError = error;
                    return AccountLoadResult.Invalid;
                }
                IsConfigValid = true;
                ConfigError = null;
                return AccountLoadResult.CreatedDefault;
            }

            string json;
            try
            {
                string encryptedText = File.ReadAllText(path, Encoding.UTF8);
                if (!TryDecryptAccountJson(encryptedText, out json, out error))
                {
                    IsConfigValid = false;
                    ConfigError = error;
                    return AccountLoadResult.Invalid;
                }
            }
            catch (Exception ex)
            {
                error = $"读取账户配置失败:{ex.Message}";
                IsConfigValid = false;
                ConfigError = error;
                return AccountLoadResult.Invalid;
            }

            try
            {
                JObject root = JObject.Parse(json);
                if (!TryParseRoot(root, out List<UserAccount> loaded, out error))
                {
                    IsConfigValid = false;
                    ConfigError = error;
                    return AccountLoadResult.Invalid;
                }
                accounts.Clear();
                accountByName.Clear();
                accountIds.Clear();
                foreach (UserAccount account in loaded)
                {
                    accounts.Add(account);
                    accountByName[account.UserName] = account;
                    accountIds.Add(account.Id);
                }
                IsConfigValid = true;
                ConfigError = null;
                return AccountLoadResult.Loaded;
            }
            catch (Exception ex)
            {
                error = $"解析账户配置失败:{ex.Message}";
                IsConfigValid = false;
                ConfigError = error;
                return AccountLoadResult.Invalid;
            }
        }

        public bool TrySave(string configPath, out string error)
        {
            error = null;
            if (!ValidateAccounts(accounts, out error))
            {
                return false;
            }
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            string path = Path.Combine(configPath, "Account.json");
            if (!TrySaveInternal(path, out error))
            {
                return false;
            }
            IsConfigValid = true;
            ConfigError = null;
            return true;
        }

        public bool TryAuthenticate(string userName, string password, out UserAccount account, out string error)
        {
            account = null;
            error = null;
            if (string.IsNullOrWhiteSpace(userName))
            {
                error = "用户名不能为空";
                return false;
            }
            if (!accountByName.TryGetValue(userName, out UserAccount found))
            {
                error = "用户名或口令错误";
                return false;
            }
            if (found.Disabled)
            {
                error = "账户已禁用";
                return false;
            }
            if (!VerifyPassword(found, password))
            {
                error = "用户名或口令错误";
                return false;
            }
            account = found;
            return true;
        }

        public bool IsDefaultCredential(string userName, string password)
        {
            if (string.IsNullOrEmpty(DefaultUserName) || string.IsNullOrEmpty(DefaultPassword))
            {
                return false;
            }
            return string.Equals(userName, DefaultUserName, StringComparison.Ordinal)
                && string.Equals(password, DefaultPassword, StringComparison.Ordinal);
        }

        public UserAccount CreateRecoveryAccount()
        {
            return new UserAccount
            {
                Id = Guid.NewGuid().ToString(),
                UserName = DefaultUserName,
                Role = UserRole.SystemAdmin,
                PasswordHash = string.Empty,
                Salt = string.Empty,
                Iterations = DefaultIterations,
                Disabled = false,
                Permissions = new List<string>(PermissionCatalog.GetDefaultPermissions(UserRole.SystemAdmin))
            };
        }

        public bool TryAddAccount(UserAccount account, out string error)
        {
            error = null;
            if (account == null)
            {
                error = "账户为空";
                return false;
            }
            if (IsSystemUserName(account.UserName))
            {
                error = "系统管理员账号不可新增";
                return false;
            }
            if (!ValidateAccount(account, out error))
            {
                return false;
            }
            if (accountByName.ContainsKey(account.UserName))
            {
                error = "用户名已存在";
                return false;
            }
            if (accountIds.Contains(account.Id))
            {
                error = "账户ID重复";
                return false;
            }
            accounts.Add(account);
            accountByName[account.UserName] = account;
            accountIds.Add(account.Id);
            if (!ValidateAccounts(accounts, out error))
            {
                accounts.Remove(account);
                accountByName.Remove(account.UserName);
                accountIds.Remove(account.Id);
                return false;
            }
            return true;
        }

        public bool TryUpdateAccount(UserAccount account, out string error)
        {
            error = null;
            if (account == null)
            {
                error = "账户为空";
                return false;
            }
            UserAccount existingById = accounts.FirstOrDefault(item => string.Equals(item.Id, account.Id, StringComparison.OrdinalIgnoreCase));
            if (existingById != null && IsSystemUserName(existingById.UserName))
            {
                error = "系统管理员账号不可修改";
                return false;
            }
            if (IsSystemUserName(account.UserName))
            {
                error = "系统管理员账号不可修改";
                return false;
            }
            if (!ValidateAccount(account, out error))
            {
                return false;
            }
            if (!accountIds.Contains(account.Id))
            {
                error = "账户不存在";
                return false;
            }
            if (accountByName.TryGetValue(account.UserName, out UserAccount existing)
                && !string.Equals(existing.Id, account.Id, StringComparison.OrdinalIgnoreCase))
            {
                error = "用户名已存在";
                return false;
            }
            UserAccount backup = null;
            int backupIndex = -1;
            for (int i = 0; i < accounts.Count; i++)
            {
                if (string.Equals(accounts[i].Id, account.Id, StringComparison.OrdinalIgnoreCase))
                {
                    backup = accounts[i];
                    backupIndex = i;
                    accounts[i] = account;
                    break;
                }
            }
            if (!ValidateAccounts(accounts, out error))
            {
                if (backupIndex >= 0 && backup != null)
                {
                    accounts[backupIndex] = backup;
                }
                return false;
            }
            accountByName.Clear();
            foreach (UserAccount item in accounts)
            {
                accountByName[item.UserName] = item;
            }
            return true;
        }

        public bool TryRemoveAccount(string accountId, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                error = "账户ID无效";
                return false;
            }
            UserAccount target = accounts.FirstOrDefault(item => string.Equals(item.Id, accountId, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                error = "账户不存在";
                return false;
            }
            if (IsSystemUserName(target.UserName))
            {
                error = "系统管理员账号不可删除";
                return false;
            }
            int adminCount = accounts.Count(item => item.Role == UserRole.SystemAdmin && !item.Disabled);
            if (target.Role == UserRole.SystemAdmin && !target.Disabled && adminCount <= 1)
            {
                error = "至少保留一个可用系统管理员";
                return false;
            }
            accounts.Remove(target);
            accountByName.Remove(target.UserName);
            accountIds.Remove(target.Id);
            return true;
        }

        public bool TrySetPassword(UserAccount account, string newPassword, out string error)
        {
            error = null;
            if (account == null)
            {
                error = "账户为空";
                return false;
            }
            if (IsSystemUserName(account.UserName))
            {
                error = "系统管理员口令不可修改";
                return false;
            }
            if (!TryValidatePassword(newPassword, out error))
            {
                return false;
            }
            byte[] salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            string hash = HashPassword(newPassword, salt, DefaultIterations);
            account.PasswordHash = hash;
            account.Salt = Convert.ToBase64String(salt);
            account.Iterations = DefaultIterations;
            return true;
        }

        public bool VerifyPassword(UserAccount account, string password)
        {
            if (account == null || string.IsNullOrEmpty(password))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(account.PasswordHash) || string.IsNullOrWhiteSpace(account.Salt))
            {
                return false;
            }
            if (account.Iterations <= 0)
            {
                return false;
            }
            byte[] salt;
            try
            {
                salt = Convert.FromBase64String(account.Salt);
            }
            catch
            {
                return false;
            }
            string computed = HashPassword(password, salt, account.Iterations);
            return FixedTimeEquals(account.PasswordHash, computed);
        }

        private static string HashPassword(string password, byte[] salt, int iterations)
        {
            using (var derive = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                byte[] hash = derive.GetBytes(HashSize);
                return Convert.ToBase64String(hash);
            }
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null)
            {
                return false;
            }
            byte[] leftBytes = Encoding.UTF8.GetBytes(left);
            byte[] rightBytes = Encoding.UTF8.GetBytes(right);
            if (leftBytes.Length != rightBytes.Length)
            {
                return false;
            }
            int diff = 0;
            for (int i = 0; i < leftBytes.Length; i++)
            {
                diff |= leftBytes[i] ^ rightBytes[i];
            }
            return diff == 0;
        }

        private bool TryParseRoot(JObject root, out List<UserAccount> loaded, out string error)
        {
            loaded = new List<UserAccount>();
            error = null;
            if (root == null)
            {
                error = "账户配置为空";
                return false;
            }
            HashSet<string> allowedRoot = new HashSet<string>(StringComparer.Ordinal)
            {
                "Accounts"
            };
            foreach (var prop in root.Properties())
            {
                if (!allowedRoot.Contains(prop.Name))
                {
                    error = $"账户配置包含未定义字段:{prop.Name}";
                    return false;
                }
            }
            if (!root.TryGetValue("Accounts", StringComparison.Ordinal, out JToken accountsToken))
            {
                error = "账户配置缺少Accounts";
                return false;
            }
            if (accountsToken.Type != JTokenType.Array)
            {
                error = "账户配置Accounts类型无效";
                return false;
            }
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JToken token in (JArray)accountsToken)
            {
                if (token.Type != JTokenType.Object)
                {
                    error = "账户项类型无效";
                    return false;
                }
                if (!TryParseAccount((JObject)token, out UserAccount account, out error))
                {
                    return false;
                }
                if (names.Contains(account.UserName))
                {
                    error = $"账户用户名重复:{account.UserName}";
                    return false;
                }
                if (ids.Contains(account.Id))
                {
                    error = $"账户ID重复:{account.Id}";
                    return false;
                }
                names.Add(account.UserName);
                ids.Add(account.Id);
                loaded.Add(account);
            }
            if (!ValidateAccounts(loaded, out error))
            {
                return false;
            }
            return true;
        }

        private bool TryParseAccount(JObject obj, out UserAccount account, out string error)
        {
            account = null;
            error = null;
            HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "Id",
                "UserName",
                "Role",
                "PasswordHash",
                "Salt",
                "Iterations",
                "Disabled",
                "Permissions"
            };
            foreach (var prop in obj.Properties())
            {
                if (!allowed.Contains(prop.Name))
                {
                    error = $"账户包含未定义字段:{prop.Name}";
                    return false;
                }
            }
            if (!TryGetString(obj, "Id", out string id, out error))
            {
                return false;
            }
            if (!Guid.TryParse(id, out _))
            {
                error = "账户ID无效";
                return false;
            }
            if (!TryGetString(obj, "UserName", out string userName, out error))
            {
                return false;
            }
            if (!ValidateUserName(userName, out error))
            {
                return false;
            }
            if (!TryGetString(obj, "Role", out string roleText, out error))
            {
                return false;
            }
            if (!Enum.TryParse(roleText, false, out UserRole role))
            {
                error = "账户角色无效";
                return false;
            }
            if (!TryGetString(obj, "PasswordHash", out string passwordHash, out error))
            {
                return false;
            }
            if (!TryGetString(obj, "Salt", out string saltText, out error))
            {
                return false;
            }
            if (!TryGetInt(obj, "Iterations", out int iterations, out error))
            {
                return false;
            }
            if (iterations < DefaultIterations)
            {
                error = "账户口令迭代次数无效";
                return false;
            }
            if (!TryValidateBase64(passwordHash, HashSize, out error))
            {
                return false;
            }
            if (!TryValidateBase64(saltText, SaltSize, out error))
            {
                return false;
            }
            if (!TryGetBool(obj, "Disabled", out bool disabled, out error))
            {
                return false;
            }
            if (!TryGetStringArray(obj, "Permissions", out List<string> permissions, out error))
            {
                return false;
            }
            if (!ValidatePermissionSet(role, permissions, out error))
            {
                return false;
            }
            account = new UserAccount
            {
                Id = id,
                UserName = userName,
                Role = role,
                PasswordHash = passwordHash,
                Salt = saltText,
                Iterations = iterations,
                Disabled = disabled,
                Permissions = permissions
            };
            return true;
        }

        private bool ValidateAccounts(List<UserAccount> source, out string error)
        {
            error = null;
            if (source == null || source.Count == 0)
            {
                error = "账户列表为空";
                return false;
            }
            UserAccount systemAccount = source.FirstOrDefault(item => IsSystemUserName(item.UserName));
            if (systemAccount == null)
            {
                error = "缺少系统管理员账户";
                return false;
            }
            if (systemAccount.Role != UserRole.SystemAdmin)
            {
                error = "系统管理员角色无效";
                return false;
            }
            if (systemAccount.Disabled)
            {
                error = "系统管理员账户不可禁用";
                return false;
            }
            if (!ValidatePermissionSet(UserRole.SystemAdmin, systemAccount.Permissions, out error))
            {
                return false;
            }
            int adminCount = source.Count(item => item.Role == UserRole.SystemAdmin && !item.Disabled);
            if (adminCount <= 0)
            {
                error = "至少需要一个可用系统管理员";
                return false;
            }
            foreach (UserAccount account in source)
            {
                if (!ValidateAccount(account, out error))
                {
                    return false;
                }
            }
            return true;
        }

        private bool ValidateAccount(UserAccount account, out string error)
        {
            error = null;
            if (account == null)
            {
                error = "账户为空";
                return false;
            }
            if (string.IsNullOrWhiteSpace(account.Id) || !Guid.TryParse(account.Id, out _))
            {
                error = "账户ID无效";
                return false;
            }
            if (!ValidateUserName(account.UserName, out error))
            {
                return false;
            }
            if (!ValidatePermissionSet(account.Role, account.Permissions, out error))
            {
                return false;
            }
            if (account.Iterations < DefaultIterations)
            {
                error = "账户口令迭代次数无效";
                return false;
            }
            if (string.IsNullOrWhiteSpace(account.PasswordHash) || string.IsNullOrWhiteSpace(account.Salt))
            {
                error = "账户口令配置无效";
                return false;
            }
            if (!TryValidateBase64(account.PasswordHash, HashSize, out error))
            {
                return false;
            }
            if (!TryValidateBase64(account.Salt, SaltSize, out error))
            {
                return false;
            }
            return true;
        }

        private static bool ValidateUserName(string userName, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(userName))
            {
                error = "用户名不能为空";
                return false;
            }
            if (!string.Equals(userName, userName.Trim(), StringComparison.Ordinal))
            {
                error = "用户名不能包含首尾空格";
                return false;
            }
            if (userName.Length > MaxNameLength)
            {
                error = "用户名长度超限";
                return false;
            }
            if (HasAnyWhiteSpace(userName))
            {
                error = "用户名不能包含空白字符";
                return false;
            }
            return true;
        }

        public static bool TryValidatePassword(string password, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(password))
            {
                error = "口令不能为空";
                return false;
            }
            if (!string.Equals(password, password.Trim(), StringComparison.Ordinal))
            {
                error = "口令不能包含首尾空格";
                return false;
            }
            if (HasAnyWhiteSpace(password))
            {
                error = "口令不能包含空白字符";
                return false;
            }
            return true;
        }

        private static bool HasAnyWhiteSpace(string value)
        {
            foreach (char c in value)
            {
                if (char.IsWhiteSpace(c))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryDecryptAccountJson(string encryptedText, out string json, out string error)
        {
            json = null;
            error = null;
            if (string.IsNullOrWhiteSpace(encryptedText))
            {
                error = "账户配置内容为空";
                return false;
            }
            if (!encryptedText.StartsWith(EncryptPrefix, StringComparison.Ordinal))
            {
                error = "账户配置格式无效";
                return false;
            }
            string base64 = encryptedText.Substring(EncryptPrefix.Length);
            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(base64);
            }
            catch
            {
                error = "账户配置内容无效";
                return false;
            }
            int minLength = EncryptSaltSize + EncryptIvSize + EncryptMacSize + 1;
            if (payload.Length < minLength)
            {
                error = "账户配置内容长度无效";
                return false;
            }
            byte[] salt = new byte[EncryptSaltSize];
            byte[] iv = new byte[EncryptIvSize];
            Buffer.BlockCopy(payload, 0, salt, 0, salt.Length);
            Buffer.BlockCopy(payload, salt.Length, iv, 0, iv.Length);
            int macOffset = payload.Length - EncryptMacSize;
            int cipherOffset = salt.Length + iv.Length;
            int cipherLength = macOffset - cipherOffset;
            if (cipherLength <= 0)
            {
                error = "账户配置内容无效";
                return false;
            }
            byte[] cipher = new byte[cipherLength];
            Buffer.BlockCopy(payload, cipherOffset, cipher, 0, cipher.Length);
            byte[] mac = new byte[EncryptMacSize];
            Buffer.BlockCopy(payload, macOffset, mac, 0, mac.Length);

            byte[] keyMaterial;
            using (var derive = new Rfc2898DeriveBytes(AccountMasterKey, salt, EncryptIterations))
            {
                keyMaterial = derive.GetBytes(64);
            }
            byte[] encKey = new byte[32];
            byte[] macKey = new byte[32];
            Buffer.BlockCopy(keyMaterial, 0, encKey, 0, encKey.Length);
            Buffer.BlockCopy(keyMaterial, 32, macKey, 0, macKey.Length);

            byte[] macSource = new byte[salt.Length + iv.Length + cipher.Length];
            Buffer.BlockCopy(salt, 0, macSource, 0, salt.Length);
            Buffer.BlockCopy(iv, 0, macSource, salt.Length, iv.Length);
            Buffer.BlockCopy(cipher, 0, macSource, salt.Length + iv.Length, cipher.Length);
            byte[] computed;
            using (var hmac = new HMACSHA256(macKey))
            {
                computed = hmac.ComputeHash(macSource);
            }
            if (!FixedTimeEquals(mac, computed))
            {
                error = "账户配置校验失败";
                return false;
            }

            try
            {
                using (var aes = Aes.Create())
                {
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = encKey;
                    aes.IV = iv;
                    using (var decryptor = aes.CreateDecryptor())
                    {
                        byte[] plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                        json = Encoding.UTF8.GetString(plain);
                    }
                }
                return true;
            }
            catch
            {
                error = "账户配置解密失败";
                return false;
            }
        }

        private static string EncryptAccountJson(string json)
        {
            byte[] salt = new byte[EncryptSaltSize];
            byte[] iv = new byte[EncryptIvSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
                rng.GetBytes(iv);
            }
            byte[] keyMaterial;
            using (var derive = new Rfc2898DeriveBytes(AccountMasterKey, salt, EncryptIterations))
            {
                keyMaterial = derive.GetBytes(64);
            }
            byte[] encKey = new byte[32];
            byte[] macKey = new byte[32];
            Buffer.BlockCopy(keyMaterial, 0, encKey, 0, encKey.Length);
            Buffer.BlockCopy(keyMaterial, 32, macKey, 0, macKey.Length);

            byte[] cipher;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encKey;
                aes.IV = iv;
                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] plain = Encoding.UTF8.GetBytes(json ?? string.Empty);
                    cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                }
            }
            byte[] macSource = new byte[salt.Length + iv.Length + cipher.Length];
            Buffer.BlockCopy(salt, 0, macSource, 0, salt.Length);
            Buffer.BlockCopy(iv, 0, macSource, salt.Length, iv.Length);
            Buffer.BlockCopy(cipher, 0, macSource, salt.Length + iv.Length, cipher.Length);
            byte[] mac;
            using (var hmac = new HMACSHA256(macKey))
            {
                mac = hmac.ComputeHash(macSource);
            }
            byte[] payload = new byte[macSource.Length + mac.Length];
            Buffer.BlockCopy(macSource, 0, payload, 0, macSource.Length);
            Buffer.BlockCopy(mac, 0, payload, macSource.Length, mac.Length);
            return EncryptPrefix + Convert.ToBase64String(payload);
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            int diff = 0;
            for (int i = 0; i < left.Length; i++)
            {
                diff |= left[i] ^ right[i];
            }
            return diff == 0;
        }

        private static bool ValidatePermissionSet(UserRole role, List<string> permissions, out string error)
        {
            error = null;
            if (permissions == null)
            {
                error = "权限列表为空";
                return false;
            }
            HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string permission in permissions)
            {
                if (string.IsNullOrWhiteSpace(permission))
                {
                    error = "权限项为空";
                    return false;
                }
                if (!PermissionCatalog.IsKnownKey(permission))
                {
                    error = $"权限项无效:{permission}";
                    return false;
                }
                if (!unique.Add(permission))
                {
                    error = $"权限项重复:{permission}";
                    return false;
                }
            }
            foreach (var pair in PermissionCatalog.GroupAccessKeys)
            {
                string groupId = pair.Key;
                string accessKey = pair.Value;
                bool hasAny = PermissionCatalog.All.Any(item => item.GroupId == groupId && unique.Contains(item.Key));
                if (hasAny && !unique.Contains(accessKey))
                {
                    error = $"缺少模块进入权限:{accessKey}";
                    return false;
                }
            }
            if (role == UserRole.SystemAdmin)
            {
                foreach (string key in PermissionCatalog.GetDefaultPermissions(UserRole.SystemAdmin))
                {
                    if (!unique.Contains(key))
                    {
                        error = "系统管理员权限不完整";
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool TryGetString(JObject obj, string name, out string value, out string error)
        {
            value = null;
            error = null;
            if (!obj.TryGetValue(name, StringComparison.Ordinal, out JToken token))
            {
                error = $"账户缺少字段:{name}";
                return false;
            }
            if (token.Type != JTokenType.String)
            {
                error = $"账户字段类型无效:{name}";
                return false;
            }
            value = token.Value<string>();
            if (value == null)
            {
                error = $"账户字段为空:{name}";
                return false;
            }
            return true;
        }

        private static bool TryGetInt(JObject obj, string name, out int value, out string error)
        {
            value = 0;
            error = null;
            if (!obj.TryGetValue(name, StringComparison.Ordinal, out JToken token))
            {
                error = $"账户缺少字段:{name}";
                return false;
            }
            if (token.Type != JTokenType.Integer)
            {
                error = $"账户字段类型无效:{name}";
                return false;
            }
            value = token.Value<int>();
            return true;
        }

        private static bool TryGetBool(JObject obj, string name, out bool value, out string error)
        {
            value = false;
            error = null;
            if (!obj.TryGetValue(name, StringComparison.Ordinal, out JToken token))
            {
                error = $"账户缺少字段:{name}";
                return false;
            }
            if (token.Type != JTokenType.Boolean)
            {
                error = $"账户字段类型无效:{name}";
                return false;
            }
            value = token.Value<bool>();
            return true;
        }

        private static bool TryGetStringArray(JObject obj, string name, out List<string> values, out string error)
        {
            values = new List<string>();
            error = null;
            if (!obj.TryGetValue(name, StringComparison.Ordinal, out JToken token))
            {
                error = $"账户缺少字段:{name}";
                return false;
            }
            if (token.Type != JTokenType.Array)
            {
                error = $"账户字段类型无效:{name}";
                return false;
            }
            foreach (JToken item in (JArray)token)
            {
                if (item.Type != JTokenType.String)
                {
                    error = $"账户字段元素类型无效:{name}";
                    return false;
                }
                string text = item.Value<string>();
                if (text == null)
                {
                    error = $"账户字段元素为空:{name}";
                    return false;
                }
                values.Add(text);
            }
            return true;
        }

        private static bool TryValidateBase64(string value, int expectedSize, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "口令配置为空";
                return false;
            }
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(value);
            }
            catch
            {
                error = "口令配置格式无效";
                return false;
            }
            if (bytes.Length != expectedSize)
            {
                error = "口令配置长度无效";
                return false;
            }
            return true;
        }

        private bool TrySaveInternal(string path, out string error)
        {
            error = null;
            JObject root = new JObject();
            JArray list = new JArray();
            foreach (UserAccount account in accounts.OrderBy(item => item.UserName, StringComparer.OrdinalIgnoreCase))
            {
                JObject obj = new JObject
                {
                    ["Id"] = account.Id,
                    ["UserName"] = account.UserName,
                    ["Role"] = account.Role.ToString(),
                    ["PasswordHash"] = account.PasswordHash,
                    ["Salt"] = account.Salt,
                    ["Iterations"] = account.Iterations,
                    ["Disabled"] = account.Disabled,
                    ["Permissions"] = new JArray(account.Permissions ?? new List<string>())
                };
                list.Add(obj);
            }
            root["Accounts"] = list;
            try
            {
                string json = root.ToString(Formatting.Indented);
                string encrypted = EncryptAccountJson(json);
                File.WriteAllText(path, encrypted, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                error = $"保存账户配置失败:{ex.Message}";
                return false;
            }
        }

        private UserAccount CreateDefaultAdmin(string userName, string password)
        {
            byte[] salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            string hash = HashPassword(password, salt, DefaultIterations);
            return new UserAccount
            {
                Id = Guid.NewGuid().ToString(),
                UserName = userName,
                Role = UserRole.SystemAdmin,
                PasswordHash = hash,
                Salt = Convert.ToBase64String(salt),
                Iterations = DefaultIterations,
                Disabled = false,
                Permissions = new List<string>(PermissionCatalog.GetDefaultPermissions(UserRole.SystemAdmin))
            };
        }
    }
}
