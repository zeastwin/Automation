using System;
using System.Security.Cryptography;
using System.Text;

// 模块：运行时 / 账户安全。
// 职责范围：集中生成和验证不可逆密码摘要，避免 UI、Store 或日志接触算法细节。

namespace Automation
{
    internal static class AccountPasswordHasher
    {
        internal const string AlgorithmName = "PBKDF2-HMAC-SHA256";
        // 本地工控软件优先保证登录响应；随机盐和固定时序比较继续保留。
        internal const int IterationCount = 20000;
        private const int SaltSize = 16;
        private const int HashSize = 32;

        public static bool ValidatePassword(string password, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(password))
            {
                error = "密码不能为空。";
                return false;
            }
            if (password.Length > 128)
            {
                error = "密码不能超过128个字符。";
                return false;
            }
            return true;
        }

        public static AccountPasswordRecord Create(string password)
        {
            if (!ValidatePassword(password, out string error))
            {
                throw new ArgumentException(error, nameof(password));
            }
            byte[] salt = new byte[SaltSize];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(salt);
            }
            byte[] hash = Derive(password, salt, IterationCount);
            return new AccountPasswordRecord
            {
                Algorithm = AlgorithmName,
                Iterations = IterationCount,
                Salt = Convert.ToBase64String(salt),
                Hash = Convert.ToBase64String(hash)
            };
        }

        public static bool Verify(string password, AccountPasswordRecord record)
        {
            if (record == null
                || !string.Equals(record.Algorithm, AlgorithmName, StringComparison.Ordinal)
                || record.Iterations != IterationCount)
            {
                return false;
            }
            try
            {
                byte[] salt = Convert.FromBase64String(record.Salt ?? string.Empty);
                byte[] expected = Convert.FromBase64String(record.Hash ?? string.Empty);
                byte[] actual = Derive(password ?? string.Empty, salt, record.Iterations);
                return FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(password ?? string.Empty);
            using (var derive = new Rfc2898DeriveBytes(bytes, salt, iterations, HashAlgorithmName.SHA256))
            {
                return derive.GetBytes(HashSize);
            }
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            int different = left == null || right == null ? 1 : left.Length ^ right.Length;
            int count = Math.Min(left?.Length ?? 0, right?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                different |= left[i] ^ right[i];
            }
            return different == 0;
        }
    }
}
