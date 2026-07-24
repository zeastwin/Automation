using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Automation.DeviceSdk;
using MySql.Data.MySqlClient;

// 模块：设备项目适配 / 旧项目数据库能力。
// 职责范围：迁入旧 DB_Base、DB 和 DataBaseView 的 MySQL 浏览、编辑及产品记录语义。
// 排查入口：连接失败时检查数据库服务器/数据库名/数据库用户/数据库密码变量及 MySQL 权限。

namespace Automation.Hmi
{
    internal sealed class LegacyDatabaseProfile
    {
        internal int Index { get; set; }

        internal string Server { get; set; }

        internal uint Port { get; set; }

        internal string Database { get; set; }

        internal string User { get; set; }

        internal string Password { get; set; }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Database)
                ? "数据库" + Index.ToString(CultureInfo.InvariantCulture)
                : Database + "@" + Server;
        }
    }

    internal sealed class LegacyDatabaseService
    {
        internal const string ProductTableName = "DB_ProductData";

        private readonly IValueStore values;

        internal LegacyDatabaseService(IValueStore values)
        {
            this.values = values ?? throw new ArgumentNullException(nameof(values));
        }

        internal IReadOnlyList<LegacyDatabaseProfile> GetProfiles()
        {
            var profiles = new List<LegacyDatabaseProfile>();
            for (int index = 0; index < 2; index++)
            {
                string server = Read("数据库服务器" + index, string.Empty);
                string database = Read("数据库名" + index, string.Empty);
                if (string.IsNullOrWhiteSpace(server) && string.IsNullOrWhiteSpace(database))
                {
                    continue;
                }
                profiles.Add(new LegacyDatabaseProfile
                {
                    Index = index,
                    Server = server,
                    Port = ReadPort("数据库端口" + index, 3306U),
                    Database = database,
                    User = Read("数据库用户" + index, "root"),
                    Password = Read("数据库密码" + index, "aaaa")
                });
            }
            return profiles;
        }

        internal IReadOnlyList<string> GetTables(LegacyDatabaseProfile profile)
        {
            ValidateProfile(profile);
            using (var connection = Open(profile))
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT TABLE_NAME FROM information_schema.TABLES "
                    + "WHERE TABLE_SCHEMA=@schema ORDER BY TABLE_NAME";
                command.Parameters.AddWithValue("@schema", profile.Database);
                using (MySqlDataReader reader = command.ExecuteReader())
                {
                    var tables = new List<string>();
                    while (reader.Read())
                    {
                        tables.Add(reader.GetString(0));
                    }
                    return tables;
                }
            }
        }

        internal IReadOnlyList<string> GetColumns(
            LegacyDatabaseProfile profile,
            string table)
        {
            ValidateIdentifier(table, "表名");
            ValidateProfile(profile);
            using (var connection = Open(profile))
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT COLUMN_NAME FROM information_schema.COLUMNS "
                    + "WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table "
                    + "ORDER BY ORDINAL_POSITION";
                command.Parameters.AddWithValue("@schema", profile.Database);
                command.Parameters.AddWithValue("@table", table);
                using (MySqlDataReader reader = command.ExecuteReader())
                {
                    var columns = new List<string>();
                    while (reader.Read())
                    {
                        columns.Add(reader.GetString(0));
                    }
                    return columns;
                }
            }
        }

        internal DataTable Query(
            LegacyDatabaseProfile profile,
            string table,
            string field,
            string value)
        {
            ValidateIdentifier(table, "表名");
            ValidateProfile(profile);
            IReadOnlyList<string> columns = GetColumns(profile, table);
            if (columns.Count == 0)
            {
                throw new InvalidOperationException("表“" + table + "”不存在或没有字段。");
            }
            if (!string.IsNullOrWhiteSpace(field)
                && !columns.Contains(field, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("查询字段“" + field + "”不存在。");
            }

            using (var connection = Open(profile))
            using (var adapter = new MySqlDataAdapter())
            {
                string sql = "SELECT * FROM " + QuoteIdentifier(table);
                var command = new MySqlCommand { Connection = connection };
                if (!string.IsNullOrWhiteSpace(field) && !string.IsNullOrWhiteSpace(value))
                {
                    sql += " WHERE CAST(" + QuoteIdentifier(field)
                        + " AS CHAR) LIKE @filter";
                    command.Parameters.AddWithValue("@filter", "%" + value + "%");
                }
                command.CommandText = sql + " LIMIT 500";
                adapter.SelectCommand = command;
                adapter.MissingSchemaAction = MissingSchemaAction.AddWithKey;
                var tableData = new DataTable(table);
                adapter.Fill(tableData);
                return tableData;
            }
        }

        internal int ApplyChanges(
            LegacyDatabaseProfile profile,
            string table,
            DataTable data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            ValidateIdentifier(table, "表名");
            ValidateProfile(profile);
            using (var connection = Open(profile))
            using (var adapter = new MySqlDataAdapter(
                "SELECT * FROM " + QuoteIdentifier(table),
                connection))
            using (var builder = new MySqlCommandBuilder(adapter))
            {
                adapter.InsertCommand = builder.GetInsertCommand();
                adapter.UpdateCommand = builder.GetUpdateCommand();
                adapter.DeleteCommand = builder.GetDeleteCommand();
                int affected = adapter.Update(data);
                data.AcceptChanges();
                return affected;
            }
        }

        internal bool TrySaveInput(
            string sn,
            string mesResult,
            DateTime time,
            out string error)
        {
            error = string.Empty;
            try
            {
                LegacyDatabaseProfile profile = GetPrimaryProfile();
                EnsureProductTable(profile);
                using (var connection = Open(profile))
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "INSERT INTO " + QuoteIdentifier(ProductTableName)
                        + " (`SN`,`MesQueryRes`,`S1InputDateTime`) "
                        + "VALUES (@sn,@result,@time)";
                    command.Parameters.AddWithValue("@sn", sn ?? string.Empty);
                    command.Parameters.AddWithValue("@result", mesResult ?? string.Empty);
                    command.Parameters.AddWithValue(
                        "@time",
                        time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    command.ExecuteNonQuery();
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal bool TrySaveOutput(
            string sn,
            string mesResult,
            string materialStatus,
            string outputData,
            DateTime time,
            out string error)
        {
            error = string.Empty;
            try
            {
                LegacyDatabaseProfile profile = GetPrimaryProfile();
                EnsureProductTable(profile);
                using (var connection = Open(profile))
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "UPDATE " + QuoteIdentifier(ProductTableName)
                        + " SET `MesAddRes`=@mes,`S1StatusOutput`=@status,"
                        + "`S1DataOutput`=@data,`S1OutputDateTime`=@time "
                        + "WHERE `SN`=@sn ORDER BY `Id` DESC LIMIT 1";
                    command.Parameters.AddWithValue("@sn", sn ?? string.Empty);
                    command.Parameters.AddWithValue("@mes", mesResult ?? string.Empty);
                    command.Parameters.AddWithValue("@status", materialStatus ?? string.Empty);
                    command.Parameters.AddWithValue("@data", outputData ?? string.Empty);
                    command.Parameters.AddWithValue(
                        "@time",
                        time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    int affected = command.ExecuteNonQuery();
                    if (affected == 0)
                    {
                        command.Parameters.Clear();
                        command.CommandText =
                            "INSERT INTO " + QuoteIdentifier(ProductTableName)
                            + " (`SN`,`MesAddRes`,`S1StatusOutput`,`S1DataOutput`,"
                            + "`S1OutputDateTime`) VALUES (@sn,@mes,@status,@data,@time)";
                        command.Parameters.AddWithValue("@sn", sn ?? string.Empty);
                        command.Parameters.AddWithValue("@mes", mesResult ?? string.Empty);
                        command.Parameters.AddWithValue("@status", materialStatus ?? string.Empty);
                        command.Parameters.AddWithValue("@data", outputData ?? string.Empty);
                        command.Parameters.AddWithValue(
                            "@time",
                            time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                        command.ExecuteNonQuery();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private LegacyDatabaseProfile GetPrimaryProfile()
        {
            LegacyDatabaseProfile profile = GetProfiles().FirstOrDefault();
            if (profile == null)
            {
                throw new InvalidOperationException(
                    "未配置数据库服务器和数据库名，已保留 CSV 本地记录。");
            }
            return profile;
        }

        private void EnsureProductTable(LegacyDatabaseProfile profile)
        {
            using (var connection = Open(profile))
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "CREATE TABLE IF NOT EXISTS " + QuoteIdentifier(ProductTableName) + " ("
                    + "`Id` INT NOT NULL AUTO_INCREMENT,"
                    + "`SN` VARCHAR(255) NOT NULL,"
                    + "`MesQueryRes` TEXT NULL,"
                    + "`MesAddRes` TEXT NULL,"
                    + "`S1StatusOutput` TEXT NULL,"
                    + "`S1DataOutput` VARCHAR(4096) NULL,"
                    + "`S1InputDateTime` VARCHAR(32) NULL,"
                    + "`S1OutputDateTime` VARCHAR(32) NULL,"
                    + "PRIMARY KEY (`Id`), INDEX `IX_SN` (`SN`)) "
                    + "ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";
                command.ExecuteNonQuery();
            }
        }

        private static MySqlConnection Open(LegacyDatabaseProfile profile)
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = profile.Server,
                Port = profile.Port,
                Database = profile.Database,
                UserID = profile.User,
                Password = profile.Password,
                CharacterSet = "utf8mb4",
                ConnectionTimeout = 3,
                DefaultCommandTimeout = 10,
                SslMode = MySqlSslMode.Disabled
            };
            var connection = new MySqlConnection(builder.ConnectionString);
            connection.Open();
            return connection;
        }

        private string Read(string name, string fallback)
        {
            return values.TryGet(name, out ValueSnapshot snapshot, out _)
                && snapshot != null
                && !string.IsNullOrWhiteSpace(snapshot.Value)
                    ? snapshot.Value.Trim()
                    : fallback;
        }

        private uint ReadPort(string name, uint fallback)
        {
            return uint.TryParse(
                Read(name, string.Empty),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint value)
                && value > 0U
                    ? value
                    : fallback;
        }

        private static void ValidateProfile(LegacyDatabaseProfile profile)
        {
            if (profile == null)
            {
                throw new InvalidOperationException("未选择数据库。");
            }
            if (string.IsNullOrWhiteSpace(profile.Server))
            {
                throw new InvalidOperationException("数据库服务器不能为空。");
            }
            if (string.IsNullOrWhiteSpace(profile.Database))
            {
                throw new InvalidOperationException("数据库名不能为空。");
            }
        }

        private static void ValidateIdentifier(string value, string description)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.IndexOf('\0') >= 0
                || value.IndexOf('`') >= 0)
            {
                throw new InvalidOperationException(description + "无效。");
            }
        }

        private static string QuoteIdentifier(string value)
        {
            ValidateIdentifier(value, "数据库标识符");
            return "`" + value + "`";
        }
    }
}
