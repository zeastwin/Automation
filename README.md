# Automation

Automation 是基于 Windows Forms 与 .NET Framework 4.8 的低代码自动化平台，核心模型为 `Proc -> Step -> OperationType`。当前仓库同时包含平台编辑器、流程运行时、设备访问、Automation Bridge、MCP 服务和 EW-AI 前台。

## 从这里开始

- [架构导航](Docs/Architecture/README.md)：先理解程序如何启动、配置如何进入运行时、流程如何执行。
- [技术债清单](Docs/Architecture/07-技术债清单.md)：区分当前事实、已确认问题和后续重整顺序。
- [重整路线图](Docs/Architecture/08-重整路线图.md)：按可独立验收的阶段持续收敛代码。

这些文档用于帮助人阅读和定位，不复制代码中的完整契约。字段、Schema、状态和运行行为仍以文档中标出的权威源码为准。

## 最小验证

```powershell
msbuild Automation.sln /m /p:Configuration=Debug /p:Platform=x64
pwsh -File Tests/ArchitectureBoundaryRegression.ps1
pwsh -File Tests/EditorWorkspaceConstructionRegression.ps1 -Configuration Debug
pwsh -File Tests/HeadlessPlatformHostRegression.ps1 -Configuration Debug
```

架构边界门禁当前会阻止 `SF.*`、`Application.MessageLoop`、`Application.DoEvents` 回流，并阻止运行时核心与运动控制层重新依赖 WinForms。工作区构造回归会实际创建并释放一次 `FrmMain`，用于发现窗体在 `EditorWorkspace` 挂接前访问 `Workspace` 的初始化顺序错误。
