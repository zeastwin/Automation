## 语言与编码

- 代码注释、技术文档、提交说明使用简体中文；标识符、API、JSON 字段保持英文。
- 只为真实职责、状态或不变量建立抽象；不新增一行透传 helper、单一实现的装饰接口或只为层次感存在的项目。
- 改动后同步删除旧入口、旧字段、旧分支、旧说明、冗余实现和未使用引用，不为尚不存在的旧项目保留兼容层。
- Automation 处于平台开发阶段；配置、Schema、运行时、Bridge/MCP、编辑器和测试始终使用同一条当前契约。

## 按任务读取上下文

常驻规则只保留跨任务边界。开始修改前只读取当前任务相关文档，不一次加载全部 Markdown：

| 修改范围 | 必读入口 |
| --- | --- |
| 启动、宿主、关闭 | `Docs/Architecture/01-启动与生命周期.md` |
| 配置、Store、事务 | `Docs/Architecture/02-配置与持久化.md` |
| 流程模型、编辑、readiness、Engine | `Docs/Architecture/03-流程编辑与运行.md`、`Engine/README.md` |
| WinForms 编辑器 | `Docs/Architecture/04-平台编辑器.md`、`Editor/README.md` |
| Goose、AI 前台、MCP、Bridge、ChangeSet | `Docs/Architecture/05-AI与Bridge.md` |
| 运动、设备、安全 | `Docs/Architecture/06-运动与安全.md`、`MotionControl/README.md` |
| 是否需要架构改造 | `Docs/Architecture/09-架构重整指导原则.md`、`Docs/Architecture/07-技术债清单.md` |

目录职责不明确时先读该目录 README；已经知道精确文件和边界时直接读取源码，不重复加载概览文档。

## 跨任务架构边界

- `AutomationPlatformHost` 拥有单个平台实例的 `PlatformRuntime`；Store、Engine、设备、安全和编辑会话由实例组合并显式传递，不恢复静态全局容器或静态窗体引用。
- HMI 只通过 `IAutomationPlatform` 公开 SDK 使用平台能力；不得直接引用 `PlatformRuntime`、平台窗体、Store、Engine 或原生运动对象。
- 配置内存、`Work/` 目录事务和运行控制分别由 `ProcessDefinitionRepository`、`ProcessWorkDirectoryTransaction`、`IProcessRuntimeControl` 负责，不合并为近义 Store。
- 窗体保留布局、确认、事件转发和渲染。跨页面状态、配置事务、设备协议、运行闸门和可复用业务规则进入现有 Workspace、Store 或应用服务。
- 手工编辑与 AI ChangeSet 复用 `ProcessVariableConfigurationService`；指令结构编辑使用 `OperationEditingService`；IO 调试配置使用 `IoDebugConfigurationEditorService`。
- 配置路径由 `PlatformRuntime.Paths.ConfigPath/WorkPath` 提供。编辑器与设备宿主的运行时释放统一进入 `PlatformShutdownCoordinator`，不得复制设备、通讯和 Engine 释放链。
- `Automation.Runtime.Contracts` 是无 UI 契约程序集，不引用 WinForms、Store、`PlatformRuntime`、Newtonsoft 或实现项目。没有实测复用或依赖问题时不继续拆程序集。
- 稳定且只由一个页面使用的 UI 逻辑可以留在页面；行数、目录对称和“还能再抽接口”不构成架构改造理由。

## 运行、配置与安全

- 流程尚未结束时再次启动是异常请求：返回明确错误并保留当前实例，不能强制结束后重启来掩盖根因。设备或人员安全已受影响时按安全准则停止并报警。
- 配置保存与流程运行分层：保存检查结构和 `saveRequired`；`ProcessReadinessService` 与启动闸门检查 `runRequired`。缺少运行资源可以保存为 `incomplete`，但不能启动。
- 配置文件或字段缺失时按当前契约生成默认值并持久化，平台继续初始化；已有值格式错误、越界或违反约束时严格失败，不静默猜测或修复。
- 只要进程和 HMI 仍可创建，配置、设备、通讯或辅助服务异常不得中断平台初始化；应降级完成启动，停止受影响流程和危险动作，记录错误、报警并设置运行闸门。
- 流程配置加载或校验异常时继续进入 HMI，停止全部流程并设置 `ProcConfigFaulted`，禁止自动和手动启动。
- 运行时遇到可能影响设备、人员或流程安全的异常或不确定状态时，优先停机和报警。
- `EnablePerformanceAnalysis` 只控制性能采样与异常报告；`EnableRuntimeDiagnostics` 只控制诊断页面、专属 MCP、黑匣子和事故窗口。关闭辅助功能不改变流程状态、安全停止、基础报警或失败日志。
- 参数解析遵循定义范式，禁止宽松解析或容错修复。

## AI、MCP 与 Bridge 核心约束

- 流程结构和同阶段变量声明只通过 ChangeSet V2：`preview_change_set` → 前台确认 → `apply_change_set(previewId)`。Bridge 冻结编译结果并校验资源、稳定身份、状态哈希和事务提交；旧 intent/patch/create_batch 或流程管理旁路不得恢复。
- `McpServer/McpToolProfile.cs` 是工具集合权威来源；参数使用 `Automation.Protocol` 强类型 DTO 和生成 Schema。能结构化表达的参数不使用无约束 `JsonElement` 或 JSON 字符串。
- `saveRequired` 表示配置保存必填，`runRequired` 表示启动必填；`preview_only/plannedProcIndex` 只属于预演。提交后使用 `createdObjects/affectedProcesses` 返回的稳定 `procId/stepId/opId`，局部 `key` 不跨已提交阶段。
- Bridge `message` 只陈述已验证事实；失败原因和重试条件放入 `recovery.reason/retryableWhen/sideEffects`，合法迁移放入 `allowedTransitions`。前台确认只改变预演状态，不把内部控制消息写入聊天正文。
- Schema 负责字段结构，行为 Catalog 负责运行语义，Readiness 负责启动阻塞，模型负责业务判断；不使用提示词压制代码契约缺陷。
- 常驻模型上下文只放跨任务事实；具体类型、资源、Guide 和开发知识按目标读取。已知目标精确读取，未知目标才列目录或搜索，不设没有协议或实测依据的任意数量限制。
- 编辑器选择只用于定位，不代表修改授权；只附加实际选中的最深层对象。
- 修改 Goose Prompt、工具、Schema、ChangeSet、语义/原生指令、资源类型、AI 前台或日志前，必须完整阅读 `Docs/Architecture/05-AI与Bridge.md` 对应开发约束并同步整条事实链。

## 验证与日志

- 自动回归统一进入 `Automation.Core.Tests`；最小命令：`dotnet test Automation.sln -c Debug -p:Platform=x64`。高风险生命周期、持久化、设备或 AI 契约改动同时验证 Release。
- `Automation.Core.Tests/Architecture/ArchitectureBoundaryRegression.ps1` 保护旧全局入口、UI 反向依赖、旧 Bridge 路由和退役类型；不得为了通过构建而弱化门禁。
- 所有运行日志写入 `D:\AutomationLogs\`，不写程序执行目录。流程日志在 `ProcessLog/`，Bridge 异常在 `Bridge/`，结构化旁路在 `Structured/`，AI 链路在 `AIExecution/`。
- 配置、状态机或安全改动必须验证失败无副作用、必要回滚和重复调用结果；仅编译通过不算完成。
