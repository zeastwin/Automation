## 语言约定
- 代码注释、技术文档、提交说明使用**简体中文**；标识符、API、JSON 字段保持英文。

## 编码约定
- 避免过度包装：仅做透传/一行调用的 helper 不要新增，直接在调用处展开。
- 改动后同步清理冗余代码与未使用引用；需求回滚时同步移除相关实现/入口/配置。

## AI 上下文设计
- 不是教 AI 忽略无关信息，而是不要把无关信息交给 AI。
- 系统提示词只保留跨任务始终成立的约束；模块知识、操作指南和候选资源必须由当前任务按需读取。
- 已知精确类型或目标时直接读取对应 Schema/详情；只有目标未知、需要发现时才返回目录，禁止把完整模块清单作为固定前置上下文。
- `Assets/Goose/system.md` 必须以 Goose 官方仓库 [`aaif-goose/goose` 的 `crates/goose/src/prompts/system.md`](https://github.com/aaif-goose/goose/blob/main/crates/goose/src/prompts/system.md) 为完整基底，保留官方模板变量、扩展说明、工具数量提示和响应规则；项目自定义内容使用明确命名的附加区块，禁止用精简重写替代官方主体。
- `Assets/Goose/system.md` 只追加 EW-AI 身份、真实性和工业安全等跨任务恒定约束。平台内部的最小上下文路由规范存放在内嵌资源 `Assets/Goose/automation.md`，启动时部署到 `%APPDATA%/Automation/Goose/automation.md` 并通过 `GOOSE_MOIM_MESSAGE_FILE` 仅注入当前 EW-AI 进程；禁止将平台内部规范放入客户可修改的 `Hmi/`。具体参数类型、数量限制和调用协议写入 MCP 工具指南及 Schema；源码模块知识由当前源码任务按需读取，禁止固定注入所有会话。
- 升级 Goose 或修改 System Prompt 前，必须先对照 Goose 官方仓库当前模板；若官方模板发生变化，应先同步官方基底，再重新应用 EW-AI 自定义区块，并递增 `GooseRuntimeProvisioner.SystemPromptVersion`。
- `Assets/Goose/automation.md` 使用独立的 `GooseRuntimeProvisioner.IntegrationContextVersion` 和 `%APPDATA%/Automation/Goose/.automation-context-version`，不得与 System Prompt 共用版本号；修改该文件时只递增专用上下文版本。
- AI 的流程与变量配置写入以 ChangeSet V2 为默认公开链路：业务语义 `preview_change_set` → 前台确认 → 仅携带 `previewId` 的 `apply_change_set`。Bridge 冻结编译后草稿并负责资源策略、符号跳转、版本校验与事务提交；旧 intent/patch/create_batch 工具不得重新加入 Goose 默认 Editor Profile。
- Goose 受管 Markdown 同时作为 Manifest 资源和 `Assets/Goose/` 输出内容发布；运行时优先读取 Manifest，缺失时读取程序目录副本。两条通道均失败时只禁用 EW-AI 并明确报警，不得阻断 HMI/平台初始化。
- Automation 源码开发知识通过 MCP 的 `get_platform_development_context` 按主题加载；常驻描述只负责让 AI 知道该入口及适用场景，具体 HMI 边界、平台 API 入口、自定义函数规则仅在调用结果中返回。已知目标直接读取 `hmi`、`platform-api` 或 `custom-function`，仅目标不明确时读取 `catalog`。
- Goose 的编译/重启结论必须由本轮实际文件改动和运行机制推导，不能把某模块的部署限制固定注入所有任务；纯流程或运行配置变更不得提示编译、重启，也不得输出“需要重启”和“已热发布生效”之类互相矛盾的结论。
- MCP 工具采用“短而完整的自描述 + 强类型参数 Schema + 按需模块知识 + Bridge 强校验”分层。禁止设置每工具首次必读、进程级已读缓存或 `TOOL_GUIDE_REQUIRED` 错误驱动协议；基础调用契约直接写入工具描述和参数类型，业务知识通过指令 Schema/Guide、Patch Action Schema、开发 Context 按目标读取。
- AI 公开契约统一使用以下语义：`saveRequired` 表示配置保存必填，`runRequired` 表示流程启动必填；`preview_only/plannedProcIndex` 只表示预演对象，提交后的对象身份以 `createdObjects/affectedProcesses` 为准；一次 `apply_change_set` 只证明当前阶段已提交，任务是否完成仍需按用户目标评估。
- Bridge 错误的 `message` 只陈述已验证事实，恢复方式放在结构化 `recovery.nextAction/nextToolArgs/retryableWhen/sideEffects` 中；禁止在错误文本中夹带“AI 不得、请告知用户”等提示词式训话，也禁止返回不存在的工具名。对象编辑在提交后使用稳定 ID，未解析的跳转 `operationKey` 可以跨阶段等待同标签目标，但不得作为已提交对象的读取身份。
- 能用结构化 DTO 表达的 MCP 参数禁止使用无约束 `JsonElement` 或 JSON 字符串；基础对象类型、必填字段、数量、长度和大小校验必须在 MCP 或 Bridge 工作线程完成，不得切换 UI 线程后才发现基础参数错误。

## 安全准则
- 运行时遇到可能影响设备、人员或流程安全的异常或不确定状态时，必须优先停机/报警，禁止继续运行。
- 平台初始化遵循可用性优先原则：只要进程和 HMI 仍可创建，任何配置、设备、通讯或辅助服务异常都不得中断平台初始化；必须以降级状态完成启动，停止受影响流程和危险动作、记录错误并明确报警，同时在运行闸门中禁止受影响功能。
- 流程配置加载或校验异常时，平台初始化必须继续完成并进入 HMI；必须停止全部流程、记录错误并弹出明确报警，设置 `ProcConfigFaulted` 禁止流程自动或手动启动，但不得因此将平台初始化标记为失败。
- 参数解析严格遵循定义范式，禁止宽松解析或容错修复。

# Automation 项目结构

基于 Windows Forms 的低代码自动化平台，核心模型 `Proc -> Step -> OperationType`。

## 目录结构
- `Bridge/` — Automation Bridge，本机 Named Pipe 服务，暴露流程/Schema/意图预演能力。
- `Communication/` — TCP/串口通讯枢纽 `CommunicationHub`。
- `Engine/` — 流程引擎 `ProcessEngine`，按 `ExecuteOperation` 分发指令。
- `IntentTemplates/` — 本地中间意图模板 `intent_templates.json`。
- `McpServer/` — 独立进程，把 Bridge 透传为 MCP Streamable HTTP 服务。
- `MotionControl/` — 运动控制（雷赛 LTDMC/LeiSei3000）。
- `Hmi/` — 面向客户和现场操作的正式 HMI 应用层；`FrmHmiMain` 是默认启动窗体，HMI 只能通过 `AutomationPlatformHost` 访问平台能力。
- `PLC/` — PLC 通讯（S7/ModbusTcp）。
- `Runtime/` — `AutomationPlatformHost`、`AppConfigStorage`、`GooseConfigStorage`、`AutomationMcpServerManager`。
- `Stores/` — 各类配置 Store 与 `LocalFileLogger`。
- `FrmAiAssistant.cs` — AI 助手前台，承载 Goose ACP 聊天、预演确认、联调。
- `GooseAcpClient.cs` — 隐藏启动 `goose acp`，JSON-RPC over stdio 通信；本地日志落 `<exe>/Logs/GooseAcp/`。
- `FrmMain.cs` / `FrmProc.cs` / `FrmDataGrid.cs` / `FrmPropertyGrid.cs` — 可隐藏的平台编辑器与流程编辑链。

## 启动模式
- `Automation.exe` 无参数启动正式 HMI `FrmHmiMain`。
- `Automation.exe --platform` 直接启动平台编辑器，便于开发调试；其他参数一律拒绝。
- HMI 和平台处于同一 UI 线程、同一进程、同一程序集，共用 `<exe>/Config/`。

## 运行时全局对象
- `SF` 是全局容器，持有窗体、`ProcessEngine`、`MotionCtrl`、各 Store、权限会话。
- `SF` 仅允许平台内部适配使用；`Hmi/` 禁止直接引用 `SF`、平台窗体、Store、引擎和原生运动对象。
- `SF.ConfigPath = <exe>/Config/`，`SF.workPath = <exe>/Config/Work/`。
- 权限入口：`SF.EnsurePermission`（动作级）、`SF.HasPermission`（状态级）、`SF.SetSecurityLock`（停机锁定）。
- 流程发布：`SF.PublishProc(procIndex)`。

## 配置文件（Config/ 目录）
- `Work/*.json` — 流程（从 `0.json` 起连续索引）。
- `value.json` — 变量表（固定 0..999，强制保留 `复位状态`、`系统状态`）。
- `DataStruct.json`、`AlarmInfo.json`、`card.json`、`IOMap.json`、`DataStation.json`。
- `SocketInfo.json`、`SerialPortInfo.json`、`PlcConfig.json`。
- `Account.json` — `ENC:` 前缀密文（AES-CBC + HMAC-SHA256 + PBKDF2）。
- `AppConfig.json` — 启动强校验，当前键 `CommMaxMessageQueueSize`（默认 1000）。
- `GooseConfig.json` — AI 助手配置，键：`GooseExecutablePath/WorkingDirectory/McpUri/SessionName/Provider/Model/MaxTurns/MaxOutputTokens`。

## 账户与权限
- 角色：`SystemAdmin`（全量）、`Admin`（全量 - Account.Manage）、`Operator`（Process.Access/Run/Search）。
- 默认系统管理员 `system / software_123`，不可改不可删。
- 账户或权限配置异常触发 `SetSecurityLock`，仅恢复登录可用。

## AI 助手 / MCP / Bridge 链路
- `FrmAiAssistant` 是唯一前台，负责聊天、停止、会话重置、预演确认弹窗。
- `GooseAcpClient` 通过 stdin/stdout 换行分隔 JSON-RPC 调用 `initialize/session/new/session/prompt/session/cancel`。
- `session/new` 的 `mcpServers` 项字段为 `name/type/url/headers`；`type=http` 时 `headers` 必填（空数组也行）。
- ACP 会话把 Automation MCP Server 作为 HTTP MCP Server 注入 Goose；Goose 不直接访问 Bridge 管道。
- Provider/Model 仅作 `GOOSE_PROVIDER/GOOSE_MODEL` 环境变量覆盖，空值沿用 Goose 自身配置；不保存模型密钥。
- AI 助手 Provider 下拉框优先从 Goose config.yaml（`%APPDATA%\Block\goose\config\config.yaml`）读取已注册的 provider 名（如 `custom_deepseek`），避免界面值与 Goose 实际注册名不一致导致 `Unknown provider`；解析由 `GooseConfigStorage.TryLoadGooseProviders` 完成（简单文本解析，无 YAML 依赖）。
- 正常 HMI 启动不启动 Bridge/MCP；首次显示平台编辑器或打开 AI 页面时启动一次，最终退出时统一关闭。
- Bridge 管道名固定 `AutomationBridgePipe`，协议为 `4字节长度前缀 + UTF-8 JSON`。
- 写入推荐路径：模板 -> 中间意图 -> `preview_intent` -> 前台确认 -> `apply_intent`（携带 `previewId`）。
- Goose/ACP 与 MCP 人工审计日志：`D:\AutomationLogs\AIExecution\`（按天滚动、5MB 分卷）。
- Goose/ACP 结构化事件：`D:\AutomationLogs\AIExecution\Structured\`；MCP 结构化事件位于 AIExecution 下对应的结构化子目录。

## 日志
- 所有运行日志统一放在 `D:\AutomationLogs\`，禁止写入程序执行目录。
- 流程引擎日志：`D:\AutomationLogs\ProcessLog\`（`LocalFileLogger`，按天 + 5MB 分卷）。
- Bridge 异常日志：`D:\AutomationLogs\Bridge\`，记录请求路径、请求体摘要和完整异常堆栈。
- 统一结构化旁路：`D:\AutomationLogs\Structured\`，使用单行 JSONL，包含 Bridge、流程指令轨迹和运行时异常；原 TXT/CSV 日志保留兼容。
- 运行信息页：`FrmInfo`，`lvInfoLog` 虚拟模式，环形缓冲 200 条。
