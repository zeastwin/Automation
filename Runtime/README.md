# 平台运行时源码导航

`Runtime/` 负责把配置、设备、流程引擎、编辑器协作和辅助服务组合成一个可启动、可关闭的平台实例。它不实现具体流程指令，也不保存配置文件的权威状态。

| 目录 | 主要入口 | 职责 |
| --- | --- | --- |
| `Hosting/` | `AutomationPlatformHost`、`PlatformRuntime` | 应用启动、实例组合、初始化、路径与宿主生命周期入口 |
| `Lifecycle/` | `PlatformSafetyCoordinator`、`PlatformShutdownCoordinator` | 安全锁、设备协调、系统状态与幂等关闭 |
| `Configuration/` | `AppConfigStorage`、`ConfigurationVersionService` | 应用配置、序列化边界、版本管理与 HMI 开发源码定位 |
| `Editing/` | `EditorSessionCoordinator`、`ProcessVariableConfigurationService` | 编辑会话、历史、剪贴板、联合提交与 UI 适配端口 |
| `Process/` | `ProcessRuntimeControl`、Store facade | 向宿主和编辑器投影流程、变量运行能力 |
| `Diagnostics/` | `RuntimeBlackBoxRecorder`、`ProcessPerformanceAnalyzer` | 断点、性能、审计、异常与黑匣子记录 |
| `Ai/` | `AutomationMcpServerManager`、`GooseRuntimeProvisioner` | AI 会话、配置、ACP 事件、MCP 进程和运行环境 |
| `Motion/` | `ManualMotionService` | 手动运动请求与安全门禁协作 |
| `Infrastructure/` | `HighResolutionWaiter`、`ObjectGraphCloner` | 无业务归属的底层运行辅助能力 |

## 放置规则

- 宿主拥有对象和组合关系放入 `Hosting/`；安全、启停和设备状态变化放入 `Lifecycle/`。
- 配置事实仍由 `Stores/` 持有；Runtime 只负责组合、协调和面向上层的 facade。
- 流程定义、校验和指令执行进入 `Engine/`，不得回流 Runtime。
- 新文件应进入明确子目录，`Runtime/` 根目录只保留本导航文档。
