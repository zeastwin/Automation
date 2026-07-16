using System;
using System.IO;
using System.Text.Json;

namespace Automation.McpServer
{
    internal static class PlatformDevelopmentContextCatalog
    {
        private const string SourceDirectoryEnvironmentVariable = "AUTOMATION_HMI_SOURCE_DIRECTORY";
        private const string ProjectPathEnvironmentVariable = "AUTOMATION_HMI_PROJECT_PATH";
        private const string PlatformSourceRootEnvironmentVariable = "AUTOMATION_PLATFORM_SOURCE_ROOT";
        private const string ValidationScriptEnvironmentVariable = "AUTOMATION_HMI_VALIDATION_SCRIPT";
        private const string CustomFunctionSourceEnvironmentVariable = "AUTOMATION_HMI_CUSTOM_FUNCTION_SOURCE";

        public static string Get(string topic)
        {
            string normalized = (topic ?? string.Empty).Trim().ToLowerInvariant();
            string sourceDirectory = Environment.GetEnvironmentVariable(SourceDirectoryEnvironmentVariable) ?? string.Empty;
            string projectPath = Environment.GetEnvironmentVariable(ProjectPathEnvironmentVariable) ?? string.Empty;
            string platformSourceRoot = Environment.GetEnvironmentVariable(PlatformSourceRootEnvironmentVariable) ?? string.Empty;
            string validationScript = Environment.GetEnvironmentVariable(ValidationScriptEnvironmentVariable) ?? string.Empty;
            string customFunctionSource = Environment.GetEnvironmentVariable(CustomFunctionSourceEnvironmentVariable) ?? string.Empty;
            string validationCommand = string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(validationScript)
                ? string.Empty
                : "powershell -NoProfile -ExecutionPolicy Bypass -File \""
                    + validationScript.Replace("\"", "`\"")
                    + "\" -ProjectPath \""
                    + projectPath.Replace("\"", "`\"")
                    + "\"";
            object validation = new
            {
                available = !string.IsNullOrWhiteSpace(validationCommand),
                command = validationCommand,
                project = projectPath,
                compileOnly = true,
                executesCandidateCode = false,
                overwritesDebug = false,
                successEvidence = "仅 ok=true 的 hmi.compile_validation 结果证明本轮设备工程 HMI 源码编译通过。"
            };

            object content;
            switch (normalized)
            {
                case "catalog":
                    content = new
                    {
                        topics = new[]
                        {
                            new { topic = "hmi", whenToUse = "修改当前运行项目的 HMI 界面、页面或客户项目源码" },
                            new { topic = "platform-api", whenToUse = "HMI 需要调用 Automation 平台公开能力" },
                            new { topic = "custom-function", whenToUse = "编写或修改当前 HMI 项目的自定义函数，或配置调用自定义函数指令" }
                        },
                        currentHmiProject = projectPath,
                        currentHmiSourceDirectory = sourceDirectory,
                        rule = "已知目标时直接读取对应 topic；只有目标不明确时才读取 catalog。"
                    };
                    break;
                case "hmi":
                    content = new
                    {
                        sourceDirectory,
                        project = projectPath,
                        scope = "sourceDirectory 是当前运行项目的 HMI 源码目录；Automation 与 MachineApp 使用相同的公开扩展方式。",
                        access = "设备 HMI 只能通过 Automation.DeviceSdk 的公开契约访问平台能力，禁止直接引用 SF、平台窗体、Store、ProcessEngine 或原生运动对象。",
                        workflow = new[]
                        {
                            "只读取 sourceDirectory 中当前页面及其直接依赖。",
                            "修改前核对实际公开签名。",
                            "HMI 界面或设备自定义函数实际修改完成后执行 validation.command。",
                            "验证产生的候选程序集和中间文件会在成功或失败后自动清理。",
                            "验证通过只证明当前 HMI 项目的候选代码可编译，不代表已经部署或验证运行行为。"
                        },
                        validation
                    };
                    break;
                case "platform-api":
                    string contractPath = string.IsNullOrWhiteSpace(platformSourceRoot)
                        ? string.Empty
                        : Path.Combine(platformSourceRoot, "Automation.DeviceSdk", "PlatformContracts.cs");
                    string implementationPath = string.IsNullOrWhiteSpace(platformSourceRoot)
                        ? string.Empty
                        : Path.Combine(platformSourceRoot, "Runtime", "AutomationPlatformHost.cs");
                    content = new
                    {
                        publicContract = contractPath,
                        implementation = implementationPath,
                        entry = "Automation.DeviceSdk 的 IAutomationPlatform 是设备 HMI 访问平台能力的公开入口。",
                        discovery = "先读取 publicContract 和当前任务涉及的公开类型签名；仅在需要核对实现行为时读取 implementation，不要凭说明猜测不存在的 API。",
                        boundary = "SF、平台窗体、Store、引擎和原生设备对象属于平台内部适配，不向设备 HMI 直接开放。",
                        safety = "设备、通讯或流程状态不确定时停止受影响动作并明确报警；不得绕过运行闸门。"
                    };
                    break;
                case "custom-function":
                    content = new
                    {
                        source = customFunctionSource,
                        project = projectPath,
                        entry = "自定义函数源码统一为当前 HMI 项目下的 Hmi/CustomFunctions.cs；平台内部 Engine/CustomFunc.cs 仅负责注册和执行，不能作为业务源码修改。",
                        usage = "通过 IAutomationPlatform.RegisterCustomFunction 注册的方法名必须与流程中调用自定义函数指令的 Name 完全一致。",
                        discovery = "修改前读取 source、Automation.DeviceSdk 的公开契约和本次需要调用的实际签名，不预读无关模块。",
                        deployment = "修改设备自定义函数后执行 validation.command。验证不会替换当前程序；只有本轮实际修改该 C# 源码时才提示重新生成并重启当前 HMI 程序（Automation 或 MachineApp）后生效，纯流程配置不得提示编译或重启。",
                        validation
                    };
                    break;
                default:
                    return JsonSerializer.Serialize(new
                    {
                        ok = false,
                        type = "mcp.error",
                        errorCode = "DEVELOPMENT_CONTEXT_TOPIC_INVALID",
                        message = "topic 仅支持 catalog/hmi/platform-api/custom-function。"
                    });
            }

            return JsonSerializer.Serialize(new
            {
                ok = true,
                type = "platform.development_context",
                topic = normalized,
                content
            });
        }
    }
}
