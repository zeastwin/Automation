using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Automation
{
    public sealed class CompositeLogger : ILogger
    {
        private readonly ILogger[] loggers;

        public CompositeLogger(params ILogger[] loggers)
        {
            this.loggers = loggers ?? Array.Empty<ILogger>();
        }

        public void Log(string message, LogLevel level)
        {
            foreach (ILogger logger in loggers)
            {
                if (logger == null)
                {
                    continue;
                }
                try
                {
                    logger.Log(message, level);
                }
                catch
                {
                }
            }
        }
    }

    public sealed class LocalFileLogger : ILogger
    {
        private const long MaxFileBytes = 5L * 1024 * 1024;
        private readonly object sync = new object();
        private readonly string baseDirectory;
        private DateTime currentDate = DateTime.MinValue;
        private int currentIndex;
        private string currentFilePath;
        private long currentFileSize;

        public LocalFileLogger(string baseDirectory)
        {
            this.baseDirectory = baseDirectory;
        }

        public void Log(string message, LogLevel level)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return;
            }
            string line = BuildLine(message, level);
            lock (sync)
            {
                try
                {
                    EnsureFile(DateTime.Now, line);
                    if (string.IsNullOrEmpty(currentFilePath))
                    {
                        return;
                    }
                    string content = line + Environment.NewLine;
                    File.AppendAllText(currentFilePath, content, Encoding.UTF8);
                    currentFileSize += Encoding.UTF8.GetByteCount(content);
                }
                catch
                {
                }
            }
        }

        private void EnsureFile(DateTime now, string line)
        {
            string dateFolder = Path.Combine(baseDirectory, now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dateFolder);

            if (currentDate.Date != now.Date || string.IsNullOrEmpty(currentFilePath))
            {
                currentDate = now.Date;
                currentIndex = GetStartIndex(dateFolder);
                currentFilePath = BuildFilePath(dateFolder, currentIndex);
                currentFileSize = GetFileSize(currentFilePath);
            }

            long addBytes = Encoding.UTF8.GetByteCount(line + Environment.NewLine);
            if (currentFileSize + addBytes > MaxFileBytes)
            {
                currentIndex++;
                currentFilePath = BuildFilePath(dateFolder, currentIndex);
                currentFileSize = GetFileSize(currentFilePath);
            }
        }

        private int GetStartIndex(string dateFolder)
        {
            int maxIndex = 0;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dateFolder, "log_*.txt");
            }
            catch
            {
                return 1;
            }
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrEmpty(name) || name.Length <= 4 || !name.StartsWith("log_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string indexText = name.Substring(4);
                if (int.TryParse(indexText, out int index) && index > maxIndex)
                {
                    maxIndex = index;
                }
            }
            if (maxIndex <= 0)
            {
                return 1;
            }
            string candidate = BuildFilePath(dateFolder, maxIndex);
            if (GetFileSize(candidate) >= MaxFileBytes)
            {
                return maxIndex + 1;
            }
            return maxIndex;
        }

        private static string BuildFilePath(string dateFolder, int index)
        {
            return Path.Combine(dateFolder, $"log_{index:D3}.txt");
        }

        private static long GetFileSize(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (info.Exists)
                {
                    return info.Length;
                }
            }
            catch
            {
            }
            return 0;
        }

        private static string BuildLine(string message, LogLevel level)
        {
            string text = message ?? string.Empty;
            string levelText = level == LogLevel.Error ? "错误" : "普通";
            return $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{levelText}] {text}";
        }
    }
}
