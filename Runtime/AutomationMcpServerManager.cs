using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Automation
{
    public sealed class AutomationMcpServerManager : IDisposable
    {
        private const string McpProcessName = "Automation.McpServer";
        private readonly object processLock = new object();
        private Process managedProcess;
        private string lastMessage = "MCP Server 尚未启动。";
        private string managedExecutablePath = string.Empty;
        private string managedRuntimeDirectory = string.Empty;
        private bool disposed;

        public string GetLifecycleReport()
        {
            lock (processLock)
            {
                if (managedProcess == null)
                {
                    return lastMessage;
                }

                if (managedProcess.HasExited)
                {
                    return $"{lastMessage}\r\nAutomation 启动的 MCP Server 已退出，ExitCode={managedProcess.ExitCode}。";
                }

                return $"{lastMessage}\r\nAutomation 启动的 MCP Server 正在运行，PID={managedProcess.Id}，文件={managedExecutablePath}。";
            }
        }

        public async Task<string> EnsureStartedAsync(string baseUri, string toolProfile)
        {
            string normalizedBaseUri = NormalizeBaseUri(baseUri);
            if (!string.Equals(toolProfile, "Diagnostic", StringComparison.Ordinal)
                && !string.Equals(toolProfile, "Editor", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"MCP工具模式不支持:{toolProfile}");
            }
            if (string.IsNullOrWhiteSpace(normalizedBaseUri))
            {
                throw new InvalidOperationException("MCP 地址为空。");
            }

            if (!IsLoopbackHttpUri(normalizedBaseUri))
            {
                throw new InvalidOperationException($"MCP 地址不是本机 HTTP 地址，禁止自动启动：{normalizedBaseUri}");
            }

            string exePath = ResolveMcpServerExecutablePath();
            if (string.IsNullOrWhiteSpace(exePath))
            {
                throw new InvalidOperationException(
                    "未找到完整的 Automation.McpServer 运行包，必须同时包含 exe、dll、deps.json 和 runtimeconfig.json。");
            }

            lock (processLock)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(AutomationMcpServerManager), "MCP Server 生命周期管理器已释放。");
                }

                // 强制清理所有同名进程（包括上次 Automation 异常退出残留的子进程），避免复用已失效的旧实例导致 Bridge 调用失败。
                int killedCount = KillAllMcpServerProcesses();
                if (KillProcess(managedProcess))
                {
                    killedCount++;
                }
                try
                {
                    managedProcess?.Dispose();
                }
                catch
                {
                }
                managedProcess = null;
                CleanupManagedRuntimeDirectory();

                string runtimeExePath = PrepareRuntimeCopy(exePath);

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = runtimeExePath,
                    WorkingDirectory = Path.GetDirectoryName(runtimeExePath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.Arguments = "--AutomationMcp:ToolProfile=" + toolProfile;
                managedProcess = Process.Start(startInfo);
                if (managedProcess == null)
                {
                    throw new InvalidOperationException("MCP Server 进程启动失败。");
                }
                managedExecutablePath = runtimeExePath;
                lastMessage = killedCount > 0
                    ? $"已清理 {killedCount} 个残留 MCP Server 进程，并启动独立运行副本：{runtimeExePath}"
                    : $"已由 Automation 启动 MCP Server 独立运行副本：{runtimeExePath}";
            }

            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(250).ConfigureAwait(false);
                if (await ReadHttpAsync(normalizedBaseUri + "/info", 1000).ConfigureAwait(false) != null)
                {
                    lock (processLock)
                    {
                        lastMessage = $"MCP Server 已就绪：{normalizedBaseUri}，工具模式:{toolProfile}";
                        return lastMessage;
                    }
                }
            }

            lock (processLock)
            {
                string processState = managedProcess == null
                    ? "进程未创建。"
                    : managedProcess.HasExited
                        ? $"进程已退出，ExitCode={managedProcess.ExitCode}。"
                        : $"进程仍在运行，PID={managedProcess.Id}。";
                lastMessage = $"MCP Server 启动后未就绪：{normalizedBaseUri}/info。启动文件：{managedExecutablePath}。{processState}";
                throw new InvalidOperationException(lastMessage);
            }
        }

        public static string NormalizeBaseUri(string baseUri)
        {
            return (baseUri ?? string.Empty).Trim().TrimEnd('/');
        }

        public static async Task<string> SetToolProfileAsync(
            string baseUri,
            string toolProfile,
            bool fullPermissionEnabled = false)
        {
            string normalizedBaseUri = NormalizeBaseUri(baseUri);
            if (!string.Equals(toolProfile, "Diagnostic", StringComparison.Ordinal)
                && !string.Equals(toolProfile, "Editor", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"MCP工具模式不支持:{toolProfile}");
            }
            if (fullPermissionEnabled && !string.Equals(toolProfile, "Editor", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("完全权限只能在编辑模式下开启。");
            }
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(normalizedBaseUri + "/tool-profile");
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.Timeout = 5000;
            request.ReadWriteTimeout = 5000;
            byte[] body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
            {
                profile = toolProfile,
                fullPermissionEnabled
            }));
            request.ContentLength = body.Length;
            using (Stream stream = await request.GetRequestStreamAsync().ConfigureAwait(false))
            {
                await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            }
            using (WebResponse response = await request.GetResponseAsync().ConfigureAwait(false))
            using (Stream stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
            {
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        public static async Task<string> ReadHttpAsync(string url, int timeoutMs)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                Task<WebResponse> responseTask = request.GetResponseAsync();
                if (await Task.WhenAny(responseTask, Task.Delay(timeoutMs)).ConfigureAwait(false) != responseTask)
                {
                    request.Abort();
                    try
                    {
                        using (WebResponse abortedResponse = await responseTask.ConfigureAwait(false))
                        {
                        }
                    }
                    catch (WebException ex) when (ex.Status == WebExceptionStatus.RequestCanceled)
                    {
                    }
                    return null;
                }

                using (WebResponse response = await responseTask.ConfigureAwait(false))
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                {
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool IsLoopbackHttpUri(string uriText)
        {
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && (uri.IsLoopback
                    || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Host, "[::1]", StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveMcpServerExecutablePath()
        {
            List<string> candidates = new List<string>();
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            AddCandidate(candidates, Path.Combine(baseDirectory, "Automation.McpServer.exe"));
            AddCandidate(candidates, Path.Combine(baseDirectory, "McpServer", "Automation.McpServer.exe"));
            AddCandidate(candidates, Path.Combine(baseDirectory, "Tools", "McpServer", "Automation.McpServer.exe"));

            string projectRoot = ResolveProjectRoot(baseDirectory);
            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                AddCandidate(candidates, Path.Combine(projectRoot, "McpServer", "bin", "x64", "Debug", "net8.0-windows", "Automation.McpServer.exe"));
                AddCandidate(candidates, Path.Combine(projectRoot, "McpServer", "bin", "x64", "Release", "net8.0-windows", "Automation.McpServer.exe"));
                AddCandidate(candidates, Path.Combine(projectRoot, "McpServer", "bin", "Debug", "net8.0-windows", "Automation.McpServer.exe"));
                AddCandidate(candidates, Path.Combine(projectRoot, "McpServer", "bin", "Release", "net8.0-windows", "Automation.McpServer.exe"));
            }

            foreach (string candidate in candidates)
            {
                if (IsCompleteMcpRuntime(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static bool IsCompleteMcpRuntime(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return false;
            }

            string directory = Path.GetDirectoryName(executablePath);
            string assemblyName = Path.GetFileNameWithoutExtension(executablePath);
            return !string.IsNullOrWhiteSpace(directory)
                && File.Exists(Path.Combine(directory, assemblyName + ".dll"))
                && File.Exists(Path.Combine(directory, assemblyName + ".deps.json"))
                && File.Exists(Path.Combine(directory, assemblyName + ".runtimeconfig.json"));
        }

        private string PrepareRuntimeCopy(string sourceExecutablePath)
        {
            string sourceDirectory = Path.GetDirectoryName(sourceExecutablePath)
                ?? throw new InvalidOperationException("MCP Server 源运行目录无效。");
            string runtimeRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Automation", "McpRuntime");
            Directory.CreateDirectory(runtimeRoot);
            CleanupStaleRuntimeDirectories(runtimeRoot);

            string runtimeDirectory = Path.Combine(runtimeRoot,
                Process.GetCurrentProcess().Id + "_" + Guid.NewGuid().ToString("N"));
            CopyRuntimeDirectory(sourceDirectory, runtimeDirectory);
            string runtimeExecutablePath = Path.Combine(runtimeDirectory, Path.GetFileName(sourceExecutablePath));
            if (!IsCompleteMcpRuntime(runtimeExecutablePath))
            {
                TryDeleteDirectory(runtimeDirectory);
                throw new InvalidOperationException("MCP Server 独立运行副本不完整，未启动进程。");
            }

            managedRuntimeDirectory = runtimeDirectory;
            return runtimeExecutablePath;
        }

        private static void CopyRuntimeDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (string file in Directory.GetFiles(sourceDirectory))
            {
                File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), true);
            }
            foreach (string directory in Directory.GetDirectories(sourceDirectory))
            {
                string name = Path.GetFileName(directory);
                if (string.Equals(name, "Logs", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Config", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "publish", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                CopyRuntimeDirectory(directory, Path.Combine(destinationDirectory, name));
            }
        }

        private static void CleanupStaleRuntimeDirectories(string runtimeRoot)
        {
            foreach (string directory in Directory.GetDirectories(runtimeRoot))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-1))
                    {
                        Directory.Delete(directory, true);
                    }
                }
                catch
                {
                    // 仍被异常残留进程使用的目录留待下次启动清理。
                }
            }
        }

        private void CleanupManagedRuntimeDirectory()
        {
            string runtimeDirectory = managedRuntimeDirectory;
            managedRuntimeDirectory = string.Empty;
            TryDeleteDirectory(runtimeDirectory);
        }

        private static void TryDeleteDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
                // 进程句柄刚释放时可能短暂占用，旧目录由下次启动的过期清理回收。
            }
        }

        private static string ResolveProjectRoot(string baseDirectory)
        {
            DirectoryInfo directory = new DirectoryInfo(baseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Automation.csproj")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            return null;
        }

        private static void AddCandidate(List<string> candidates, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            foreach (string candidate in candidates)
            {
                if (string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            candidates.Add(path);
        }

        public void Dispose()
        {
            lock (processLock)
            {
                disposed = true;
                int killedCount = KillAllMcpServerProcesses();
                if (KillProcess(managedProcess))
                {
                    killedCount++;
                }
                try
                {
                    managedProcess?.Dispose();
                }
                catch
                {
                }
                managedProcess = null;
                managedExecutablePath = string.Empty;
                CleanupManagedRuntimeDirectory();
                lastMessage = $"Automation 关闭时已回收 {killedCount} 个 MCP Server 进程。";
            }
        }

        private static int KillAllMcpServerProcesses()
        {
            int killedCount = 0;
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(McpProcessName);
            }
            catch
            {
                return killedCount;
            }

            foreach (Process process in processes)
            {
                using (process)
                {
                    if (KillProcess(process))
                    {
                        killedCount++;
                    }
                }
            }
            return killedCount;
        }

        private static bool KillProcess(Process process)
        {
            if (process == null)
            {
                return false;
            }

            try
            {
                if (process.HasExited)
                {
                    return false;
                }

                process.Kill();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
