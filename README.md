# Automation

Automation 是基于 Windows Forms 与 .NET Framework 4.8 的低代码自动化平台，核心模型为 `Proc -> Step -> OperationType`。当前仓库同时包含平台编辑器、流程运行时、设备访问、Automation Bridge、MCP 服务和 EW-AI 前台。

## 从这里开始

- [架构导航](Docs/Architecture/README.md)：先理解程序如何启动、配置如何进入运行时、流程如何执行。
- [架构重整指导原则](Docs/Architecture/09-架构重整指导原则.md)：说明边界、事实源、测试、不过度拆分和停止决策。
- [技术债清单](Docs/Architecture/07-技术债清单.md)：记录当前停止结论、非阻塞观察项和重新启动架构工作的条件。

这些文档用于帮助人阅读和定位，不复制代码中的完整契约。字段、Schema、状态和运行行为仍以文档中标出的权威源码为准。

## 最小验证

```powershell
dotnet test Automation.sln -c Debug -p:Platform=x64
```

全部自动回归统一由 `Automation.Core.Tests` 进入 VS Test Explorer 和 `dotnet test`。架构边界仍在主程序集编译前执行；需要独立进程或真实 WinForms 消息循环的旧回归由测试项目统一调度，不再从仓库根部逐个运行脚本。
