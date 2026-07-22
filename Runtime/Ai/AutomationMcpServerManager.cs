using Newtonsoft.Json;
// 模块：运行时 / AI 集成。
// 职责范围：管理 AI 会话、配置、ACP/MCP 进程、受管运行环境和分析记录。

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Automation
{
    public sealed class AutomationMcpServerManager : IDisposable
    {
        private const string McpProcessName = "Automation.McpServer";
        private const string EditorInstanceName = "editor";
        private const string RuntimeDiagnosticInstanceName = "runtime_diagnostic";
        private readonly object processLock = new object();
        private readonly Dictionary<string, ManagedMcpInstance> instances =
            new Dictionary<string, ManagedMcpInstance>(StringComparer.Ordinal);
        private string lastMessage = "MCP Server 尚未启动。";
        private bool staleProcessesCleaned;
        private bool disposed;

        public Task<string> EnsureStartedAsync(string baseUri, string toolProfile)
        {
            if (!string.Equals(toolProfile, "Diagnostic", StringComparison.Ordinal)
                && !string.Equals(toolProfile, "Editor", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"MCP工具模式不支持:{toolProfile}");
            }
            return EnsureInstanceStartedAsync(
                EditorInstanceName,
                NormalizeBaseUri(baseUri),
                toolProfile,
                enableTrayIcon: true,
                allowToolProfileChanges: true);
        }

        /// <summary>
        /// 启动固定 RuntimeDiagnostic Profile 的独立 MCP 实例，并返回本次进程的专属地址。
        /// 该地址不写入 GooseConfig，也不改变编辑助手 MCP 的工具集合。
        /// </summary>
        public async Task<string> EnsureRuntimeDiagnosticStartedAsync()
        {
            if (!AppConfigStorage.TryGetCached(out AppConfig config, out string configError))
            {
                throw new InvalidOperationException("智能诊断配置不可用：" + configError);
            }
            if (!config.EnableRuntimeDiagnostics)
            {
                throw new InvalidOperationException("智能诊断中心已在程序设置中停用。");
            }
            ManagedMcpInstance active = null;
            lock (processLock)
            {
                ThrowIfDisposedLocked();
                if (instances.TryGetValue(RuntimeDiagnosticInstanceName, out ManagedMcpInstance current)
                    && IsRunning(current.Process))
                {
                    active = current;
                }
            }

            if (active != null)
            {
                string info = await ReadHttpAsync(active.BaseUri + "/info", 1000).ConfigureAwait(false);
                if (HasExpectedProfile(info, "RuntimeDiagnostic")) return active.BaseUri;
            }

            string baseUri = AllocateLoopbackUri();
            await EnsureInstanceStartedAsync(
                RuntimeDiagnosticInstanceName,
                baseUri,
                "RuntimeDiagnostic",
                enableTrayIcon: false,
                allowToolProfileChanges: false).ConfigureAwait(false);
            return baseUri;
        }

        public void StopRuntimeDiagnostic()
        {
            lock (processLock)
            {
                if (disposed) return;
                int stoppedCount = StopInstanceLocked(RuntimeDiagnosticInstanceName);
                lastMessage = stoppedCount > 0
                    ? "智能诊断已停用，RuntimeDiagnostic MCP 实例已停止。"
                    : "智能诊断已停用，RuntimeDiagnostic MCP 实例未运行。";
            }
        }

        private async Task<string> EnsureInstanceStartedAsync(
            string instanceName,
            string baseUri,
            string toolProfile,
            bool enableTrayIcon,
            bool allowToolProfileChanges)
        {
            string normalizedBaseUri = NormalizeBaseUri(baseUri);
            if (string.IsNullOrWhiteSpace(normalizedBaseUri))
            {
                throw new InvalidOperationException("MCP 地址为空。");
            }
            if (!IsLoopbackHttpUri(normalizedBaseUri))
            {
                throw new InvalidOperationException($"MCP 地址不是本机 HTTP 地址，禁止自动启动：{normalizedBaseUri}");
            }

            string sourceExecutablePath = ResolveMcpServerExecutablePath();
            if (string.IsNullOrWhiteSpace(sourceExecutablePath))
            {
                throw new InvalidOperationException(
                    "未找到完整的 Automation.McpServer 运行包，必须同时包含 exe、dll、deps.json 和 runtimeconfig.json。");
            }

            ManagedMcpInstance started;
            int killedCount = 0;
            lock (processLock)
            {
                ThrowIfDisposedLocked();
                if (!staleProcessesCleaned)
                {
                    killedCount = KillAllMcpServerProcesses();
                    staleProcessesCleaned = true;
                }
                else
                {
                    killedCount += KillUntrackedMcpServerProcessesLocked();
                }
                killedCount += StopInstanceLocked(instanceName);

                string runtimeExecutablePath = PrepareRuntimeCopy(
                    sourceExecutablePath, instanceName, out string runtimeDirectory);
                var startInfo = new ProcessStartInfo
                {
                    FileName = runtimeExecutablePath,
                    WorkingDirectory = Path.GetDirectoryName(runtimeExecutablePath)
                        ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Arguments = "--AutomationMcp:ToolProfile=" + toolProfile
                        + " --AutomationMcp:ListenUrl=" + normalizedBaseUri
                        + " --AutomationMcp:EnableTrayIcon=" + enableTrayIcon.ToString().ToLowerInvariant()
                        + " --AutomationMcp:AllowToolProfileChanges="
                        + allowToolProfileChanges.ToString().ToLowerInvariant()
                };
                ApplyHmiDevelopmentEnvironment(startInfo);
                Process process = Process.Start(startInfo);
                if (process == null)
                {
                    TryDeleteDirectory(runtimeDirectory);
                    throw new InvalidOperationException($"MCP Server 进程启动失败:{instanceName}");
                }
                started = new ManagedMcpInstance
                {
                    Name = instanceName,
                    Profile = toolProfile,
                    BaseUri = normalizedBaseUri,
                    Process = process,
                    ExecutablePath = runtimeExecutablePath,
                    RuntimeDirectory = runtimeDirectory
                };
                instances[instanceName] = started;
                lastMessage = killedCount > 0
                    ? $"已清理{killedCount}个旧 MCP 进程并启动{instanceName}实例。"
                    : $"已启动{instanceName} MCP 实例。";
            }

            for (int attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(250).ConfigureAwait(false);
                string info = await ReadHttpAsync(normalizedBaseUri + "/info", 1000).ConfigureAwait(false);
                if (HasExpectedProfile(info, toolProfile))
                {
                    lock (processLock)
                    {
                        lastMessage = $"MCP Server 已就绪：{normalizedBaseUri}，实例:{instanceName}，工具模式:{toolProfile}";
                        return lastMessage;
                    }
                }
            }

            lock (processLock)
            {
                string processState = IsRunning(started.Process)
                    ? $"进程仍在运行，PID={started.Process.Id}。"
                    : "进程已退出。";
                lastMessage = $"MCP Server 启动后未就绪：{normalizedBaseUri}/info。"
                    + $"启动文件：{started.ExecutablePath}。{processState}";
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
                throw new InvalidOperationException($"可动态切换的MCP工具模式不支持:{toolProfile}");
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
                using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                {
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool HasExpectedProfile(string info, string expectedProfile)
        {
            if (string.IsNullOrWhiteSpace(info)) return false;
            try
            {
                return string.Equals(
                    JObject.Parse(info)["toolProfile"]?.Value<string>(),
                    expectedProfile,
                    StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static string AllocateLoopbackUri()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                return $"http://127.0.0.1:{port}";
            }
            finally
            {
                listener.Stop();
            }
        }

        private static bool IsLoopbackHttpUri(string uriText)
        {
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out Uri uri)) return false;
            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && (uri.IsLoopback
                    || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Host, "[::1]", StringComparison.OrdinalIgnoreCase));
        }

        private static void ApplyHmiDevelopmentEnvironment(ProcessStartInfo startInfo)
        {
            if (!HmiDevelopmentSourceLocator.TryResolve(
                AppDomain.CurrentDomain.BaseDirectory,
                out HmiDevelopmentSource hmiSource,
                out _))
            {
                return;
            }
            startInfo.EnvironmentVariables[HmiDevelopmentSourceLocator.SourceDirectoryEnvironmentVariable] =
                hmiSource.SourceDirectory;
            startInfo.EnvironmentVariables[HmiDevelopmentSourceLocator.ProjectKindEnvironmentVariable] =
                hmiSource.ProjectKind;
            if (!string.IsNullOrWhiteSpace(hmiSource.ProjectRoot))
            {
                startInfo.EnvironmentVariables[HmiDevelopmentSourceLocator.ProjectRootEnvironmentVariable] =
                    hmiSource.ProjectRoot;
            }
            if (!string.IsNullOrWhiteSpace(hmiSource.SkillRootDirectory))
            {
                startInfo.EnvironmentVariables[HmiDevelopmentSourceLocator.SkillRootEnvironmentVariable] =
                    hmiSource.SkillRootDirectory;
            }
            if (!string.IsNullOrWhiteSpace(hmiSource.ProjectPath))
            {
                startInfo.EnvironmentVariables[HmiDevelopmentSourceLocator.ProjectPathEnvironmentVariable] =
                    hmiSource.ProjectPath;
            }
            if (!string.IsNullOrWhiteSpace(hmiSource.PlatformSourceRoot))
            {
                startInfo.EnvironmentVariables[HmiDevelopmentSourceLocator.PlatformSourceRootEnvironmentVariable] =
                    hmiSource.PlatformSourceRoot;
            }
            if (!string.IsNullOrWhiteSpace(hmiSource.ValidationScriptPath))
            {
                startInfo.EnvironmentVariables[HmiDevelopmentSourceLocator.ValidationScriptEnvironmentVariable] =
                    hmiSource.ValidationScriptPath;
            }
            if (!string.IsNullOrWhiteSpace(hmiSource.CustomFunctionSourcePath))
            {
                startInfo.EnvironmentVariables[HmiDevelopmentSourceLocator.CustomFunctionSourceEnvironmentVariable] =
                    hmiSource.CustomFunctionSourcePath;
            }
        }

        private static string ResolveMcpServerExecutablePath()
        {
            var candidates = new List<string>();
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
                if (IsCompleteMcpRuntime(candidate)) return candidate;
            }
            return null;
        }

        private static bool IsCompleteMcpRuntime(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return false;
            string directory = Path.GetDirectoryName(executablePath);
            string assemblyName = Path.GetFileNameWithoutExtension(executablePath);
            return !string.IsNullOrWhiteSpace(directory)
                && File.Exists(Path.Combine(directory, assemblyName + ".dll"))
                && File.Exists(Path.Combine(directory, assemblyName + ".deps.json"))
                && File.Exists(Path.Combine(directory, assemblyName + ".runtimeconfig.json"));
        }

        private static string PrepareRuntimeCopy(
            string sourceExecutablePath,
            string instanceName,
            out string runtimeDirectory)
        {
            string sourceDirectory = Path.GetDirectoryName(sourceExecutablePath)
                ?? throw new InvalidOperationException("MCP Server 源运行目录无效。");
            string runtimeRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Automation", "McpRuntime");
            Directory.CreateDirectory(runtimeRoot);
            CleanupStaleRuntimeDirectories(runtimeRoot);
            runtimeDirectory = Path.Combine(runtimeRoot,
                Process.GetCurrentProcess().Id + "_" + instanceName + "_" + Guid.NewGuid().ToString("N"));
            CopyRuntimeDirectory(sourceDirectory, runtimeDirectory);
            string runtimeExecutablePath = Path.Combine(runtimeDirectory, Path.GetFileName(sourceExecutablePath));
            if (!IsCompleteMcpRuntime(runtimeExecutablePath))
            {
                TryDeleteDirectory(runtimeDirectory);
                throw new InvalidOperationException("MCP Server 独立运行副本不完整，未启动进程。");
            }
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

        private int StopInstanceLocked(string instanceName)
        {
            if (!instances.TryGetValue(instanceName, out ManagedMcpInstance instance)) return 0;
            int killedCount = KillProcess(instance.Process) ? 1 : 0;
            try
            {
                instance.Process?.Dispose();
            }
            catch
            {
            }
            TryDeleteDirectory(instance.RuntimeDirectory);
            instances.Remove(instanceName);
            return killedCount;
        }

        private void ThrowIfDisposedLocked()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AutomationMcpServerManager), "MCP Server 生命周期管理器已释放。");
            }
        }

        private static void TryDeleteDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
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
                if (File.Exists(Path.Combine(directory.FullName, "Automation.csproj"))) return directory.FullName;
                directory = directory.Parent;
            }
            return null;
        }

        private static void AddCandidate(ICollection<string> candidates, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || candidates.Contains(path)) return;
            candidates.Add(path);
        }

        public void Dispose()
        {
            lock (processLock)
            {
                if (disposed) return;
                disposed = true;
                int killedCount = 0;
                foreach (string name in new List<string>(instances.Keys))
                {
                    killedCount += StopInstanceLocked(name);
                }
                killedCount += KillAllMcpServerProcesses();
                lastMessage = $"Automation 关闭时已回收{killedCount}个 MCP Server 进程。";
            }
        }

        private int KillUntrackedMcpServerProcessesLocked()
        {
            var trackedProcessIds = new HashSet<int>();
            foreach (ManagedMcpInstance instance in instances.Values)
            {
                if (!IsRunning(instance.Process)) continue;
                try
                {
                    trackedProcessIds.Add(instance.Process.Id);
                }
                catch
                {
                }
            }

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
                    int processId;
                    try
                    {
                        processId = process.Id;
                    }
                    catch
                    {
                        continue;
                    }
                    if (!trackedProcessIds.Contains(processId) && KillProcess(process)) killedCount++;
                }
            }
            return killedCount;
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
                    if (KillProcess(process)) killedCount++;
                }
            }
            return killedCount;
        }

        private static bool IsRunning(Process process)
        {
            if (process == null) return false;
            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static bool KillProcess(Process process)
        {
            if (!IsRunning(process)) return false;
            try
            {
                process.Kill();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class ManagedMcpInstance
        {
            public string Name { get; set; }
            public string Profile { get; set; }
            public string BaseUri { get; set; }
            public Process Process { get; set; }
            public string ExecutablePath { get; set; }
            public string RuntimeDirectory { get; set; }
        }
    }
}
