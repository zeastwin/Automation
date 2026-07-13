using System.Text.Json;

namespace Automation.McpServer
{
    internal static class PlatformDevelopmentContextCatalog
    {
        public static string Get(string topic)
        {
            string normalized = (topic ?? string.Empty).Trim().ToLowerInvariant();
            object content;
            switch (normalized)
            {
                case "catalog":
                    content = new
                    {
                        topics = new[]
                        {
                            new { topic = "hmi", whenToUse = "修改 Hmi 界面、页面或客户项目源码" },
                            new { topic = "platform-api", whenToUse = "Hmi 需要调用 Automation 平台公开能力" },
                            new { topic = "custom-function", whenToUse = "编写或修改 CustomFunc.cs，或配置调用自定义函数指令" }
                        },
                        rule = "已知目标时直接读取对应 topic；只有目标不明确时才读取 catalog。"
                    };
                    break;
                case "hmi":
                    content = new
                    {
                        scope = "Hmi/ 是客户项目源码范围；平台内核源码仅用于理解公开接口，不作为客户 HMI 任务的默认修改目标。",
                        access = "Hmi 只能通过 AutomationPlatformHost 及实际公开接口访问平台能力，禁止直接引用 SF、平台窗体、Store、ProcessEngine 或原生运动对象。",
                        workflow = new[] { "只读取当前页面及其直接依赖。", "修改前核对实际公开签名。", "实际修改 C# 后再根据构建结果说明是否需要编译部署。" }
                    };
                    break;
                case "platform-api":
                    content = new
                    {
                        entry = "Runtime/AutomationPlatformHost 是 Hmi 访问平台能力的统一入口。",
                        discovery = "先读取 AutomationPlatformHost 和当前任务涉及的公开类型签名；不要凭说明猜测不存在的 API。",
                        boundary = "SF、平台窗体、Store、引擎和原生设备对象属于平台内部适配，不向 Hmi 直接开放。",
                        safety = "设备、通讯或流程状态不确定时停止受影响动作并明确报警；不得绕过运行闸门。"
                    };
                    break;
                case "custom-function":
                    content = new
                    {
                        source = "Hmi/CustomFunc.cs",
                        usage = "调用自定义函数指令的 Name 必须与 CustomFunc 注册的方法名完全一致。",
                        discovery = "修改前读取 CustomFunc.cs、其上下文类型和本次需要调用的公开接口实际签名，不预读无关模块。",
                        deployment = "C# 自定义函数不支持运行时热加载；只有本轮实际修改 CustomFunc.cs 时才提示用户自行编译、重启后生效。纯流程配置不得提示编译或重启。",
                        validation = "以平台编译结果和 Bridge 对自定义函数源码/方法名的校验结果为准。"
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
