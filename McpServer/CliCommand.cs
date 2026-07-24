using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Automation.McpServer
{
    /// <summary>
    /// MCP CLI 模式：把当前 Profile 的工具集当作命令行工具使用。
    /// 发现与 Schema 读取按需输出（list/schema），调用（call）与 HTTP 模式共用
    /// 同一批 [McpServerTool] 声明、同一份 Profile 集合和同一条 Bridge 管道链路，
    /// 不经过 HTTP 层，也不启动托盘或消息循环。
    /// </summary>
    internal static class CliCommand
    {
        private const int ExitOk = 0;
        private const int ExitLocalFailure = 1;
        private const int ExitUsage = 2;

        public static async Task<int> RunAsync(string[] args)
        {
            // 保证 JSON 输出始终是 UTF-8，不受系统代码页影响。
            Console.OutputEncoding = new UTF8Encoding(false);

            if (args.Length == 0 || args.Any(value => string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase)))
            {
                PrintUsage();
                return args.Length == 0 ? ExitUsage : ExitOk;
            }

            string command = args[0];
            string? profileOverride = ReadOptionValue(args, "--profile");
            string? jsonArgument = ReadOptionValue(args, "--json");
            string? jsonFile = ReadOptionValue(args, "--json-file");
            if (jsonFile != null)
            {
                // 大体积参数（如 ChangeSet）走文件传递，规避命令行长度上限与 PowerShell 引用问题。
                try
                {
                    jsonArgument = File.ReadAllText(jsonFile, Encoding.UTF8);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"--json-file 读取失败:{ex.Message}");
                    return ExitUsage;
                }
            }
            bool fullSchema = args.Any(value => string.Equals(value, "--full", StringComparison.OrdinalIgnoreCase));
            var positional = args.Skip(1)
                .Where((value, index) => !IsOptionWithValue(args, index + 1))
                .Where(value => !value.StartsWith("--", StringComparison.Ordinal))
                .ToList();

            string? profile = ResolveProfile(profileOverride);
            if (profile == null)
            {
                Console.Error.WriteLine(
                    $"工具Profile不支持:{profileOverride ?? Environment.GetEnvironmentVariable("AUTOMATION_MCP_PROFILE")}，可选Editor/Diagnostic/RuntimeDiagnostic。");
                return ExitUsage;
            }

            bool fullPermission = args.Any(value => string.Equals(value, "--full-permission", StringComparison.OrdinalIgnoreCase))
                || IsTruthyEnvironment("AUTOMATION_MCP_FULL_PERMISSION");
            if (fullPermission && !string.Equals(profile, "Editor", StringComparison.Ordinal))
            {
                // 与 DynamicMcpToolRegistry.SetConfiguration 同一约束。
                Console.Error.WriteLine("完全权限只能在Editor模式下开启。");
                return ExitUsage;
            }

            AutomationMcpOptions options = AutomationMcpOptions.Load(
                new ConfigurationBuilder().Build(),
                AppContext.BaseDirectory);
            options.ToolProfile = profile;
            ToolCallLogger.Configure(options.LogRoot);
            AutomationMcpRuntime.Initialize(options);

            IReadOnlyList<McpServerTool> tools = McpToolProfile.CreateTools(profile, fullPermission);

            switch (command.ToLowerInvariant())
            {
                case "list":
                    PrintToolList(tools, fullSchema);
                    return ExitOk;
                case "schema":
                    return PrintToolSchema(tools, positional.Count > 0 ? positional[0] : null);
                case "call":
                    return await InvokeToolAsync(
                        tools,
                        positional.Count > 0 ? positional[0] : null,
                        jsonArgument).ConfigureAwait(false);
                default:
                    Console.Error.WriteLine($"未知cli命令:{command}");
                    PrintUsage();
                    return ExitUsage;
            }
        }

        private static string? ResolveProfile(string? profileOverride)
        {
            string candidate = !string.IsNullOrWhiteSpace(profileOverride)
                ? profileOverride.Trim()
                : Environment.GetEnvironmentVariable("AUTOMATION_MCP_PROFILE")?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(candidate))
            {
                return "Editor";
            }
            foreach (string supported in new[] { "Editor", "Diagnostic", "RuntimeDiagnostic" })
            {
                if (string.Equals(candidate, supported, StringComparison.OrdinalIgnoreCase))
                {
                    return supported;
                }
            }
            return null;
        }

        private static void PrintToolList(IReadOnlyList<McpServerTool> tools, bool fullSchema)
        {
            var items = new JsonArray();
            foreach (McpServerTool tool in tools)
            {
                var item = new JsonObject
                {
                    ["name"] = tool.ProtocolTool.Name,
                    ["description"] = tool.ProtocolTool.Description
                };
                if (fullSchema)
                {
                    item["inputSchema"] = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText());
                }
                items.Add(item);
            }
            Console.WriteLine(items.ToJsonString(IndentedOptions));
        }

        private static int PrintToolSchema(IReadOnlyList<McpServerTool> tools, string? toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                Console.Error.WriteLine("schema 命令需要工具名：cli schema <name>");
                return ExitUsage;
            }
            McpServerTool? tool = FindTool(tools, toolName);
            if (tool == null)
            {
                return PrintUnknownTool(tools, toolName);
            }
            var result = new JsonObject
            {
                ["name"] = tool.ProtocolTool.Name,
                ["description"] = tool.ProtocolTool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())
            };
            Console.WriteLine(result.ToJsonString(IndentedOptions));
            return ExitOk;
        }

        private static async Task<int> InvokeToolAsync(
            IReadOnlyList<McpServerTool> tools,
            string? toolName,
            string? jsonArgument)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                Console.Error.WriteLine("call 命令需要工具名：cli call <name> --json '<argsJson>'");
                return ExitUsage;
            }
            McpServerTool? tool = FindTool(tools, toolName);
            if (tool == null)
            {
                return PrintUnknownTool(tools, toolName);
            }

            JsonObject arguments;
            try
            {
                arguments = string.IsNullOrWhiteSpace(jsonArgument)
                    ? new JsonObject()
                    : JsonNode.Parse(jsonArgument) as JsonObject
                        ?? throw new JsonException("call 参数必须是 JSON 对象。");
            }
            catch (JsonException ex)
            {
                // 回显实际收到的参数前缀，便于发现 shell 引用层把 \" 原样传入等问题。
                string received = jsonArgument.Length <= 60
                    ? jsonArgument
                    : jsonArgument.Substring(0, 60) + "...";
                Console.Error.WriteLine($"--json 参数解析失败:{ex.Message}；实际收到:{received}");
                return ExitUsage;
            }

            MethodInfo? method = FindToolMethod(tool.ProtocolTool.Name);
            if (method == null)
            {
                Console.Error.WriteLine($"工具缺少方法实现:{tool.ProtocolTool.Name}");
                return ExitLocalFailure;
            }

            object?[] boundArguments;
            try
            {
                boundArguments = BindArguments(method, arguments);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine(BuildErrorJson(
                    "INVALID_ARGUMENT", ex.Message, (ex as InvalidToolArgumentException)?.SemanticKind));
                return ExitUsage;
            }

            try
            {
                object? invocationResult = method.Invoke(null, boundArguments);
                string text = invocationResult switch
                {
                    Task<string> task => await task.ConfigureAwait(false),
                    string value => value,
                    _ => throw new InvalidOperationException(
                        $"工具 {tool.ProtocolTool.Name} 返回类型不受支持:{method.ReturnType.Name}")
                };
                Console.WriteLine(text);
                return ExitOk;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                Console.WriteLine(BuildErrorJson("TOOL_INVOCATION_FAILED", ex.InnerException.Message));
                return ExitLocalFailure;
            }
            catch (Exception ex)
            {
                Console.WriteLine(BuildErrorJson("TOOL_INVOCATION_FAILED", ex.Message));
                return ExitLocalFailure;
            }
        }

        /// <summary>
        /// 与 HTTP 模式同源绑定：参数键按名称（不区分大小写）匹配，
        /// 反序列化使用 MCP SDK 默认选项，缺失的可选参数使用方法签名默认值。
        /// </summary>
        private static object?[] BindArguments(MethodInfo method, JsonObject arguments)
        {
            ParameterInfo[] parameters = method.GetParameters();
            var bound = new object?[parameters.Length];
            var missing = new List<string>();
            for (int index = 0; index < parameters.Length; index++)
            {
                ParameterInfo parameter = parameters[index];
                JsonNode? value = null;
                foreach (KeyValuePair<string, JsonNode?> property in arguments)
                {
                    if (string.Equals(property.Key, parameter.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        break;
                    }
                }

                if (value == null)
                {
                    if (parameter.HasDefaultValue)
                    {
                        bound[index] = parameter.DefaultValue;
                        continue;
                    }
                    // 与 SDK 生成的 inputSchema 一致：无默认值参数即 required，缺失直接报用法错误。
                    missing.Add(parameter.Name ?? $"arg{index}");
                    continue;
                }

                try
                {
                    bound[index] = value.Deserialize(parameter.ParameterType, StrictArgumentOptions);
                }
                catch (JsonException ex)
                {
                    throw new InvalidToolArgumentException(
                        $"参数 {parameter.Name} 反序列化失败:{DescribeJsonTypeMismatch(ex, value)}",
                        TryResolveSemanticKind(ex.Path, value), ex);
                }
            }

            if (missing.Count > 0)
            {
                throw new ArgumentException($"缺少必填参数:{string.Join(", ", missing)}");
            }
            return bound;
        }

        /// <summary>携带出错语义 kind 的参数错误，供错误输出附加精确契约读取入口。</summary>
        private sealed class InvalidToolArgumentException : ArgumentException
        {
            public string? SemanticKind { get; }

            public InvalidToolArgumentException(string message, string? semanticKind, Exception innerException)
                : base(message, innerException)
            {
                SemanticKind = semanticKind;
            }
        }

        /// <summary>
        /// 出错字段位于 actions[N].operation 内时取回该指令的语义 kind，
        /// 让错误恢复能直接指向这一种 kind 的精确契约，而不是整个 ChangeSet Schema。
        /// </summary>
        private static string? TryResolveSemanticKind(string? path, JsonNode argumentRoot)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            Match operation = Regex.Match(path, @"^\$\.actions\[\d+\]\.operation(?=\.|$)");
            if (!operation.Success)
            {
                return null;
            }
            JsonNode? kindNode = ResolveJsonPath(argumentRoot, operation.Value + ".kind");
            return kindNode is JsonValue value && value.TryGetValue<string>(out string? kind)
                ? kind
                : null;
        }

        /// <summary>
        /// 把 System.Text.Json 的 .NET 类型名错误翻译为 JSON 层事实：
        /// 字段路径、期望的 JSON 类型和实际收到的值，避免模型从
        /// "System.Nullable`1[System.Double]" 这类实现细节猜测契约。
        /// </summary>
        private static string DescribeJsonTypeMismatch(JsonException ex, JsonNode argumentRoot)
        {
            string? expected = MapExpectedJsonType(ex.Message);
            if (expected == null || string.IsNullOrEmpty(ex.Path))
            {
                return ex.Message;
            }
            JsonNode? node = ResolveJsonPath(argumentRoot, ex.Path);
            string received = node switch
            {
                null => "null",
                JsonValue value when value.TryGetValue<string>(out string? text)
                    => $"string \"{(text.Length <= 40 ? text : text.Substring(0, 40) + "...")}\"",
                JsonValue => node.ToJsonString(),
                JsonArray => "array",
                JsonObject => "object",
                _ => "null"
            };
            return $"字段 {ex.Path} 期望 {expected}，收到 {received}。";
        }

        private static string? MapExpectedJsonType(string message)
        {
            const string marker = "could not be converted to ";
            int start = message.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }
            string typeName = message.Substring(start + marker.Length);
            int end = typeName.IndexOf(". Path:", StringComparison.Ordinal);
            typeName = (end >= 0 ? typeName.Substring(0, end) : typeName).TrimEnd('.');
            // 展开 Nullable<T> 并去掉命名空间前缀。
            Match nullable = Regex.Match(typeName, @"^System\.Nullable`1\[(.+)\]$");
            if (nullable.Success)
            {
                typeName = nullable.Groups[1].Value;
            }
            int lastDot = typeName.LastIndexOf('.');
            if (lastDot >= 0)
            {
                typeName = typeName.Substring(lastDot + 1);
            }
            return typeName switch
            {
                "Double" or "Single" or "Decimal" => "number",
                "Int16" or "Int32" or "Int64" or "UInt16" or "UInt32" or "UInt64" or "Byte" => "integer",
                "String" => "string",
                "Boolean" => "boolean",
                _ => null
            };
        }

        /// <summary>按 System.Text.Json 的 $.a[0].b 路径格式取回出错字段的原始节点。</summary>
        private static JsonNode? ResolveJsonPath(JsonNode root, string path)
        {
            if (path == "$")
            {
                return root;
            }
            JsonNode? current = root;
            foreach (string segment in path.Substring(2).Split('.'))
            {
                string name = segment;
                int bracket = segment.IndexOf('[');
                if (bracket >= 0)
                {
                    name = segment.Substring(0, bracket);
                }
                if (name.Length > 0)
                {
                    current = current is JsonObject obj ? obj[name] : null;
                }
                if (bracket >= 0 && current != null)
                {
                    foreach (Match index in Regex.Matches(segment.Substring(bracket), @"\[(\d+)\]"))
                    {
                        int position = int.Parse(index.Groups[1].Value);
                        current = current is JsonArray array && position < array.Count ? array[position] : null;
                    }
                }
                if (current == null)
                {
                    return null;
                }
            }
            return current;
        }

        private static McpServerTool? FindTool(IReadOnlyList<McpServerTool> tools, string toolName)
        {
            foreach (McpServerTool tool in tools)
            {
                if (string.Equals(tool.ProtocolTool.Name, toolName, StringComparison.Ordinal))
                {
                    return tool;
                }
            }
            return null;
        }

        private static MethodInfo? FindToolMethod(string toolName)
        {
            foreach (MethodInfo method in typeof(AutomationMcpTools).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                McpServerToolAttribute? attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                if (string.Equals(attribute?.Name, toolName, StringComparison.Ordinal))
                {
                    return method;
                }
            }
            return null;
        }

        private static int PrintUnknownTool(IReadOnlyList<McpServerTool> tools, string toolName)
        {
            Console.Error.WriteLine($"当前Profile未开放工具:{toolName}");
            Console.Error.WriteLine("可用工具:" + string.Join(", ", tools.Select(tool => tool.ProtocolTool.Name)));
            return ExitUsage;
        }

        private static string BuildErrorJson(string errorCode, string message, string? semanticKind = null)
        {
            var error = new JsonObject
            {
                ["ok"] = false,
                ["type"] = "cli.error",
                ["errorCode"] = errorCode,
                ["message"] = message
            };
            if (!string.IsNullOrEmpty(semanticKind))
            {
                // 语义指令字段错误直接给出该 kind 的精确契约读取入口（Editor/Diagnostic 均开放），
                // 避免模型退化为在整个 ChangeSet Schema 中检索。
                error["recovery"] = new JsonObject
                {
                    ["reason"] = "fix_invalid_argument",
                    ["retryableWhen"] = "argument_matches_tool_input_schema",
                    ["sideEffects"] = "none",
                    ["semanticKind"] = semanticKind,
                    ["contractTool"] = "get_semantic_operation_schema"
                };
            }
            return error.ToJsonString(IndentedOptions);
        }

        private static bool IsTruthyEnvironment(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return string.Equals(value, "1", StringComparison.Ordinal)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string? ReadOptionValue(string[] args, string option)        {
            for (int index = 1; index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }
            return null;
        }

        private static bool IsOptionWithValue(string[] args, int index)
        {
            if (index <= 0 || index >= args.Length)
            {
                return false;
            }
            string previous = args[index - 1];
            return string.Equals(previous, "--profile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(previous, "--json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(previous, "--json-file", StringComparison.OrdinalIgnoreCase);
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine(
                "Automation MCP CLI — 以命令行方式使用当前Profile的MCP工具集。" + Environment.NewLine
                + "用法:" + Environment.NewLine
                + "  cli list [--full] [--profile <Editor|Diagnostic|RuntimeDiagnostic>] [--full-permission]" + Environment.NewLine
                + "      列出工具名与描述；--full 附带每个工具的 inputSchema；--full-permission 仅 Editor 可用，追加迁移/平台配置工具。" + Environment.NewLine
                + "  cli schema <name>" + Environment.NewLine
                + "      输出单个工具的描述与 inputSchema。" + Environment.NewLine
                + "  cli call <name> [--json '<argsJson>' | --json-file <path>]" + Environment.NewLine
                + "      调用工具并输出其 JSON 返回；--json 缺省为 {}；大体积参数用 --json-file 从 UTF-8 文件读取。" + Environment.NewLine
                + "Profile 解析顺序：--profile 参数 > AUTOMATION_MCP_PROFILE 环境变量 > Editor。" + Environment.NewLine
                + "完全权限解析顺序：--full-permission 参数 > AUTOMATION_MCP_FULL_PERMISSION 环境变量（1/true）> 关闭。" + Environment.NewLine
                + "退出码：0=调用已执行（业务错误在输出JSON的ok:false内）；1=本地故障；2=用法错误。");
        }

        private static readonly JsonSerializerOptions IndentedOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // 反序列化与 SDK 同源，但未知字段立即报错：避免模型把 steps/ops 嵌套进
        // process.create 等位置时被静默忽略，出现"预演通过但对象为空"的假成功。
        private static readonly JsonSerializerOptions StrictArgumentOptions =
            new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            };
    }
}
