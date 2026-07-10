## 语言约定
- 代码注释、技术文档、提交说明使用**简体中文**；标识符、API、JSON 字段保持英文。

## 编码约定
- 避免过度包装：仅做透传/一行调用的 helper 不要新增，直接在调用处展开。
- 改动后同步清理冗余代码与未使用引用；需求回滚时同步移除相关实现/入口/配置。

## 安全准则
- 任何异常或不确定状态必须优先停机/报警，禁止继续运行。
- 参数解析严格遵循定义范式，禁止宽松解析或容错修复。

## 会话启动
- 每次新对话开始后，第一步必须先读取本文件再执行任何分析或改动。
- 首次回复需明确一句“已读取 AGENTS 约束并按其执行”。

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
- `SocketInfo.json`、`SerialPortInfo.json`、`PlcDevice.json`、`PlcMap.json`。
- `Account.json` — `ENC:` 前缀密文（AES-CBC + HMAC-SHA256 + PBKDF2）。
- `AppConfig.json` — 启动强校验，当前键 `CommMaxMessageQueueSize`（默认 1000）。
- `GooseConfig.json` — AI 助手配置，键：`GooseExecutablePath/WorkingDirectory/McpUri/SessionName/Provider/Model/MaxTurns`。

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
- GooseAcpClient 本地日志：`<exe>/Logs/GooseAcp/yyyy-MM-dd/log_NNN.txt`（按天滚动、5MB 分卷）。
- MCP Server 调用日志：`<exe>/Logs/McpServer/`。

## 日志
- 流程引擎日志：`D:\AutomationLogs\ProcessLog\`（`LocalFileLogger`，按天 + 5MB 分卷）。
- 运行信息页：`FrmInfo`，`lvInfoLog` 虚拟模式，环形缓冲 200 条。
