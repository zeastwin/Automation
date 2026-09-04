// 模块：运动控制 / EPSON 机器人。
// 职责范围：加载并格式化 3.0 Epson.ini 指令模板；模板文件是命令名称和参数数量的单一事实源。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Automation.MotionControl
{
    internal sealed class EpsonCommandCatalog
    {
        internal const string Home = "HOME";
        internal const string GoPoint = "GPT";
        internal const string GoPosition = "GPOS";
        internal const string MovePoint = "MPT";
        internal const string MovePosition = "MPOS";
        internal const string MoveOffset = "MOFFSET";
        internal const string MoveLine = "MLINE";
        internal const string MoveArc = "MARC";
        internal const string MoveArc3 = "MARC3";
        internal const string AddContinuousLine = "ADD_CP_LINE";
        internal const string AddContinuousArc = "ADD_CP_ARC";
        internal const string StartContinuousMove = "CP_MOVE";
        internal const string SetSpeed = "SET_SPEED";
        internal const string GetPosition = "GET_POS";
        internal const string SavePoint = "SAVE_PT";
        internal const string SetPoint = "SET_PT";
        internal const string CreatePallet = "CREAT_PALLET";
        internal const string GoPalletPosition = "GO_PALLET_POS";
        internal const string CcdCalibration = "CCD_CLIB";
        internal const string GoCcdPoint = "GO_CCD_PT";

        private const string EmbeddedResourceName = "Automation.MotionControl.Epson.ini";
        private static readonly string[] RequiredSections =
        {
            Home, GoPoint, GoPosition, MovePoint, MovePosition, MoveOffset, MoveLine,
            MoveArc, MoveArc3, AddContinuousLine, AddContinuousArc, StartContinuousMove,
            SetSpeed, GetPosition, SavePoint, SetPoint, CreatePallet, GoPalletPosition,
            CcdCalibration, GoCcdPoint
        };

        private readonly IReadOnlyDictionary<string, string> formats;

        private EpsonCommandCatalog(IReadOnlyDictionary<string, string> formats)
        {
            this.formats = formats;
        }

        internal static bool TryLoad(PlatformPaths paths, out EpsonCommandCatalog catalog, out string error)
        {
            catalog = null;
            error = null;
            if (paths == null)
            {
                error = "EPSON 指令模板路径未配置。";
                return false;
            }

            string directory = Path.Combine(paths.ConfigPath, "RbtCmd");
            string filePath = Path.Combine(directory, "Epson.ini");
            try
            {
                if (!File.Exists(filePath))
                {
                    Directory.CreateDirectory(directory);
                    using (Stream source = Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream(EmbeddedResourceName))
                    {
                        if (source == null)
                        {
                            error = "程序包缺少 EPSON 默认指令模板资源。";
                            return false;
                        }
                        using (var target = new FileStream(
                            filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            source.CopyTo(target);
                            target.Flush(true);
                        }
                    }
                }

                return TryParse(File.ReadAllLines(filePath, Encoding.UTF8), out catalog, out error);
            }
            catch (Exception ex)
            {
                error = $"EPSON 指令模板加载失败：{ex.Message}";
                return false;
            }
        }

        internal static bool TryParse(IEnumerable<string> lines, out EpsonCommandCatalog catalog, out string error)
        {
            catalog = null;
            error = null;
            if (lines == null)
            {
                error = "EPSON 指令模板内容为空。";
                return false;
            }

            var rawCommands = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = null;
            foreach (string sourceLine in lines)
            {
                string line = (sourceLine ?? string.Empty).Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal)
                    || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                if (line.StartsWith("[", StringComparison.Ordinal)
                    && line.EndsWith("]", StringComparison.Ordinal)
                    && line.Length > 2)
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0 || string.IsNullOrWhiteSpace(section))
                {
                    continue;
                }
                string key = line.Substring(0, separator).Trim();
                if (string.Equals(key, "cmd", StringComparison.OrdinalIgnoreCase))
                {
                    rawCommands[section] = line.Substring(separator + 1).Trim();
                }
            }

            var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string required in RequiredSections)
            {
                if (!rawCommands.TryGetValue(required, out string raw))
                {
                    error = $"EPSON 指令模板缺少节：{required}";
                    return false;
                }
                if (!TryConvertToFormat(raw, out string format, out string formatError))
                {
                    error = $"EPSON 指令模板 {required} 无效：{formatError}";
                    return false;
                }
                parsed.Add(required, format);
            }

            catalog = new EpsonCommandCatalog(parsed);
            return true;
        }

        internal bool TryBuild(string section, out string command, out string error, params object[] arguments)
        {
            command = null;
            error = null;
            if (!formats.TryGetValue(section ?? string.Empty, out string format))
            {
                error = $"EPSON 指令模板不存在：{section}";
                return false;
            }
            try
            {
                command = string.Format(
                    CultureInfo.InvariantCulture, format, arguments ?? Array.Empty<object>());
                return true;
            }
            catch (FormatException ex)
            {
                error = $"EPSON 指令 {section} 参数数量不匹配：{ex.Message}";
                return false;
            }
        }

        private static bool TryConvertToFormat(string raw, out string format, out string error)
        {
            format = null;
            error = null;
            string value = (raw ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                error = "cmd 为空";
                return false;
            }

            bool keepTerminator = value.EndsWith(",", StringComparison.Ordinal)
                || value.EndsWith(";", StringComparison.Ordinal);
            char terminator = keepTerminator ? value[value.Length - 1] : ',';
            string[] parts = value.Split(new[] { ',', ';' }, StringSplitOptions.None);
            string commandName = parts[0].Trim();
            if (commandName.Length == 0)
            {
                error = "命令名称为空";
                return false;
            }

            int parameterCount = parts.Length - 1;
            if (keepTerminator && parameterCount > 0 && parts[parts.Length - 1].Length == 0)
            {
                parameterCount--;
            }
            var builder = new StringBuilder(commandName);
            for (int i = 0; i < parameterCount; i++)
            {
                builder.Append(",{").Append(i).Append('}');
            }
            if (keepTerminator)
            {
                builder.Append(terminator);
            }
            builder.Append("\r\n");
            format = builder.ToString();
            return true;
        }
    }
}
