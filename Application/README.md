# 应用入口

本目录只放置进程入口和应用级启动声明。`Program.cs` 负责建立 WinForms 进程入口并调用 `AutomationPlatformBootstrap`，平台组合、配置初始化和 HMI/编辑器选择继续由 Runtime 负责。
