## 语言约定
- 代码注释、技术文档、提交说明使用**简体中文**；标识符、API、JSON 字段保持英文。

## 编码约定
- 避免过度包装：仅做透传/一行调用的 helper 不要新增，直接在调用处展开。
- 改动后同步清理冗余代码与未使用引用；需求回滚时同步移除相关实现/入口/配置。

## AI 上下文设计
- 不是教 AI 忽略无关信息，而是不要把无关信息交给 AI。
- 面向模型的常驻上下文、工具描述、按需 Guide 和错误恢复优先陈述当前事实、可用动作、字段契约与成功条件，尽量不写“不要、不得、禁止、切勿、AI不得”等训话式负面引导。代码已经强制执行的规则由 Schema、状态机、校验器或结构化 `recovery` 表达，文字只保留必要的事实投影；工业安全边界与真实互斥条件除外。
- 系统提示词只保留跨任务始终成立的约束；模块知识、操作指南和候选资源必须由当前任务按需读取。
- 已知精确类型或目标时直接读取对应 Schema/详情；只有目标未知、需要发现时才返回目录，禁止把完整模块清单作为固定前置上下文。
- `Assets/Goose/system.md` 以 Goose 官方仓库 [`aaif-goose/goose` 的 `crates/goose/src/prompts/system.md`](https://github.com/aaif-goose/goose/blob/main/crates/goose/src/prompts/system.md) 为功能规则基底，保留官方模板变量、扩展说明、工具数量提示和响应规则。官方身份、名称和来源文案可以按 EW-AI 品牌直接替换，不视为破坏功能基底；其他项目自定义内容使用明确命名的附加区块，禁止用精简重写替代官方功能主体。
- `Assets/Goose/system.md` 只在官方功能规则基底上替换 EW-AI 品牌身份，并追加真实性和工业安全等跨任务恒定约束。平台内部的最小上下文路由规范存放在内嵌资源 `Assets/Goose/automation.md`，启动时部署到 `%APPDATA%/Automation/Goose/automation.md` 并通过 `GOOSE_MOIM_MESSAGE_FILE` 仅注入当前 EW-AI 进程；禁止将平台内部规范放入客户可修改的 `Hmi/`。具体参数类型、数量限制和调用协议写入 MCP 工具指南及 Schema；源码模块知识由当前源码任务按需读取，禁止固定注入所有会话。
- 升级 Goose 或修改 System Prompt 前，必须先对照 Goose 官方仓库当前模板；若官方模板发生变化，应先同步官方功能规则基底，再重新应用 EW-AI 品牌身份和自定义区块，并递增 `GooseRuntimeProvisioner.SystemPromptVersion`。
- `Assets/Goose/automation.md` 使用独立的 `GooseRuntimeProvisioner.IntegrationContextVersion` 和 `%APPDATA%/Automation/Goose/.automation-context-version`，不得与 System Prompt 共用版本号；修改该文件时只递增专用上下文版本。
- AI 的流程结构以及需要与流程同阶段事务提交的逐变量声明，以 ChangeSet V2 为默认公开链路：业务语义 `preview_change_set` → 前台确认 → 仅携带 `previewId` 的 `apply_change_set`。Bridge 冻结编译结果并负责资源策略、符号跳转、版本校验与事务提交；旧 intent/patch/create_batch 工具不得重新加入 Goose 默认 Editor Profile。
- Goose 受管 Markdown 同时作为 Manifest 资源和 `Assets/Goose/` 输出内容发布；运行时优先读取 Manifest，缺失时读取程序目录副本。两条通道均失败时只禁用 EW-AI 并明确报警，不得阻断 HMI/平台初始化。
- Automation 源码开发知识通过 MCP 的 `get_platform_development_context` 按主题加载；常驻描述只负责让 AI 知道该入口及适用场景，具体 HMI 边界、平台 API 入口、自定义函数规则仅在调用结果中返回。已知目标直接读取 `hmi`、`platform-api` 或 `custom-function`，仅目标不明确时读取 `catalog`。
- Goose 的编译/重启结论必须由本轮实际文件改动和运行机制推导，不能把某模块的部署限制固定注入所有任务；纯流程或运行配置变更不得提示编译、重启，也不得输出“需要重启”和“已热发布生效”之类互相矛盾的结论。
- MCP 工具采用“短而完整的自描述 + 强类型参数 Schema + 按需模块知识 + Bridge 强校验”分层。禁止设置每工具首次必读、进程级已读缓存或 `TOOL_GUIDE_REQUIRED` 错误驱动协议；基础调用契约直接写入工具描述和参数类型，业务知识通过语义/原生指令 Schema、同源行为 Guide、资源详情和开发 Context 按目标读取。
- AI 公开契约统一使用以下语义：`saveRequired` 表示配置保存必填，`runRequired` 表示流程启动必填；`preview_only/plannedProcIndex` 只表示预演对象，提交后的对象身份以 `createdObjects/affectedProcesses` 为准；一次 `apply_change_set` 只证明当前阶段已提交，任务是否完成仍需按用户目标评估。
- Bridge 错误的 `message` 只陈述已验证事实；失败原因和重试条件放在结构化 `recovery.reason/retryableWhen/sideEffects`，当前合法状态迁移放在 `allowedTransitions`。禁止在错误文本中夹带“AI 不得、请告知用户”等提示词式训话，也禁止返回不存在的工具名。对象编辑在提交后使用稳定 ID，未解析的跳转 `operationKey` 可以跨阶段等待同标签目标，但不得作为已提交对象的读取身份。
- 能用结构化 DTO 表达的 MCP 参数禁止使用无约束 `JsonElement` 或 JSON 字符串；基础对象类型、必填字段、数量、长度和大小校验必须在 MCP 或 Bridge 工作线程完成，不得切换 UI 线程后才发现基础参数错误。

## AI 模块核心设计思想
- 目标是让 AI 理解平台、拿到准确事实并使用趁手工具逐步完成任务；不以“最少工具调用”“一次性完整提交”或“回复看起来完整”为优化目标。合理的分阶段读取、保存、回读和修正属于正常链路。
- 不强迫一次做完。一个 ChangeSet 只需是可独立审查、可保存、可继续完善的阶段；AI 可以先创建空流程、空步骤或少量指令，再根据提交结果继续。不得为追求一次完成重新引入渐进式草稿管理器、会话外草稿缓存或大而全的单次输出协议。
- 不用任意硬限制代替架构设计。流程数、步骤数、指令数、Schema 数量和 JSON 体积只有在协议、内存、UI 或实测模型边界提供依据时才设限；限制必须由服务端强类型 Schema 或体积计算执行，并返回可分页/分批的事实，禁止把诸如“最多3个流程/10个步骤/20条指令/64KB”固定写入提示词。
- 不滥用 Markdown。`system.md`、`automation.md` 只承载各自层级长期稳定的内容；字段名、枚举、模式矩阵、资源候选、数量边界、错误恢复和具体指令技巧不得复制到常驻提示词。能由 Schema、行为契约、资源工具或 Bridge 状态按需提供的事实，就只在对应来源维护。
- 不用提示词弥补代码契约缺陷。AI 经常填错字段、选错工具或误解指令时，先检查工具命名与描述、参数 Schema、行为契约、资源发现、编译器和错误结构；只有跨任务通用的决策方法缺失时，才以少量正向提示补充 `automation.md`。
- 提示词描述稳定事实和通用方法，不为某一条测试语句编写专用规则，不堆叠“不要这样、不要那样”的负面训话，不预先注入当前任务没有使用的模块知识。场景变化后仍成立，才有资格进入常驻 Markdown。
- 向 AI 摆放可验证事实，不替 AI 编造结论。控制流、运行语义和资源状态只有确定性实现能够证明时才输出；不可靠的启发式分析器、猜测性警告或“推荐下一步”会与模型自身推理冲突，宁可删除。模型负责结合用户目标判断，平台负责提供事实、合法状态迁移和安全边界。
- 保存与运行分层。配置保存只检查 `saveRequired`、结构和可确定的不变量；缺少 `runRequired`、晚绑定资源或尚未补齐的业务目标可以保存为 incomplete。启动闸门再由 `ProcessReadinessService` 严格拦截。不得为了让预演“看起来通过”而伪造资源或占位值，也不得把运行条件升级为保存条件。
- 会话连续性是能力而不是污染。未新建、删除或切换对话时，保留已经验证的对象身份、Schema、平台事实和成功调用经验；新请求重新选择当前最短可验证路径，但不清空仍适用的知识，也不盲目沿用与新目标无关的上轮方案。
- 用户在编辑器中的选择只是定位上下文，不是改动授权。只传递实际选中的最深层级：只选流程则不附加步骤/指令，只选步骤则不附加指令；AI 仍以用户本轮文字目标决定是否读取或修改。
- 性能优化优先减少无效上下文和错误重试，而不是禁止 Shell、强迫并行或压缩掉必要事实。已知目标精确读，未知目标才搜索；大对象由服务端先算体积，再用摘要、分页、步骤读取或最多25个明确 `opId` 的有限批量读取。

## AI 事实分层与单一事实源
- 同一事实只允许一个权威来源；其他层只能引用、投影或验证，不得手写另一份近似说明。若确需多种输出形式，必须从同一来源生成并用测试证明等价。
- Goose 官方通用行为的权威来源是 `Assets/Goose/system.md` 的官方功能规则基底；EW-AI 品牌身份可替换官方身份与来源文案，真实性、工业安全等跨任务约束放在明确命名的自定义区块。
- Automation 的常驻路由方法权威来源是 `Assets/Goose/automation.md`；这里只说明如何选择表达层、如何分阶段提交和如何验证，不保存具体指令知识。
- MCP 对外工具集合的权威来源是 `McpServer/McpToolProfile.cs`；工具是否存在、属于 Editor 还是 Diagnostic，以 Profile 为准。`McpServer/Program.cs --verify-profile` 必须检查必需工具和退役工具，Markdown、错误和 Guide 不得引用 Profile 中不存在的工具。
- MCP 参数入口的权威来源是 `Automation.Protocol/` 的强类型 DTO、`AutomationMcpTools` 方法签名及 `McpToolProfile` 生成的 JSON Schema。工具描述只补充“何时使用”和参数无法表达的短语义。
- 原生指令类型集合的权威来源是 `OperationDefinitionRegistry`；原生字段结构、嵌套数组、符号跳转和 JSON 到对象的严格编译以 `StructuredOperationCompiler` 为准。
- 原生指令运行行为、字段联动、模式矩阵和失败条件以 `OperationBehaviorCatalog` 为权威来源；`get_operation_guide` 和原生 Schema 中的 behavior 必须读取同一个 Catalog，不维护第二份手写 Guide。
- 语义指令支持范围、Schema 和编译结果以 `SemanticOperationKinds`、`AiOperationCompilerRegistry` 及对应编译器为权威来源；语义层无法准确表达时才选择 `native.operation`，不得让两个表达层对同一字段给出不同含义。
- 资源配置的权威来源是相应 Store；候选发现和精确读取由对应 Bridge/MCP 资源工具投影。`BuildAiResourceSnapshot` 只为本次编译提供资源事实，资源引用类型必须与 `StructuredOperationCompiler`、引用追踪和运行就绪检查一致。
- 可保存性与结构归一化以 `AiChangeSetCompiler`、`ProcessDefinitionService` 为准；可运行性以 `ProcessReadinessService` 和实际运行闸门为准；指令最终运行语义以 `ProcessEngine.Operations.*` 为准。预演校验与运行时若共享同一不变量，应抽取共同验证器，禁止复制两套逐渐漂移的规则。
- 预演状态、确认状态、合法后续迁移、版本哈希和提交结果以 `AutomationBridgeService` 返回的结构化字段为准。前台只展示和确认，不得把内部状态重新包装成聊天提示词要求 AI 执行。
- 已提交对象身份以稳定 `procId/stepId/opId` 为准；当前 ChangeSet 内的新对象关联才使用局部 `key`。物理索引只用于当前显示和读取，不作为跨阶段身份；跳转由符号目标编译并在结构变化后重算。

## 防止“左右脑互搏”的检查原则
- 修改 AI 链路前先列出本次事实会经过的所有层：常驻上下文、工具 Profile、工具描述/Schema、按需 Guide、Bridge 返回、编译器、就绪校验、运行时、前台渲染和日志。只改其中一层通常视为未完成。
- 出现互相矛盾、模糊或不贴切的信息时，删除非权威副本并修正生成链，不在更上层再加一句提示词压制它。
- Bridge `message` 只写已验证事实；合法后续动作放 `allowedTransitions`，失败恢复放结构化 `recovery`。不得返回自然语言决策指令、模型训话、已退役工具名或未经验证的“应该”。
- Schema 负责字段类型、枚举、结构、条件必填和引用类型；行为 Catalog 负责运行语义；Readiness 负责启动阻塞；模型负责业务判断。禁止同一个验证器越层推测用户意图或业务正确性。
- 前台确认只改变预演记录状态。不得在聊天区显示 `previewId`、自动确认文案或“请立即继续提交”等内部控制消息；AI 从工具返回的 `confirmed/status/allowedTransitions` 获取状态。
- 用户手动停止测试、模型响应变慢、MCP 被用户退出等外部事实必须和工具路由问题区分；日志没有证据时不得归因给提示词或模型能力。
- 任何“优化”都必须能回答：减少了哪类错误、删除了哪份重复事实、权威来源是什么、如何验证没有改变其他场景。仅增加规则数量不算优化。

## AI 功能改动联动检查表
- 新增或修改原生指令时，逐项检查：模型与默认值、`OperationDefinitionRegistry` 注册、`ProcessEngine` 分发与执行、`StructuredOperationCompiler` 递归 Schema/数量字段/符号跳转、`OperationBehaviorCatalog`、所需资源工具与引用类型、`BuildAiResourceSnapshot`、`ProcessDefinitionService`/`ProcessReadinessService`、原生编译测试和实际运行测试。
- 新增或修改语义 `kind` 时，逐项检查：Protocol DTO、`SemanticOperationKinds`、语义 Schema、编译器注册、ChangeSet discriminator、字段与资源策略、就绪校验、Profile 验证和语义编译测试。
- 新增或修改资源类型时，逐项检查：Store/API 单一事实源、列表发现与精确读取是否都必要、Bridge 端点、`AutomationBridgeClient`、强类型 MCP 工具、Profile、`referenceType`、资源快照、引用追踪、保存/运行缺失策略和测试。已知名称应可精确读取，未知名称才列目录。
- 修改 ChangeSet 时，逐项检查：`Automation.Protocol` DTO、MCP JSON Schema、Bridge 基础参数验证是否在 UI 线程前、`AiChangeSetCompiler`、冻结预演、状态哈希、前台确认、仅凭 `previewId` 提交、事务保存、运行值保持、`createdObjects/affectedProcesses` 和 Profile 回归。
- 修改跳转、插入、删除或移动指令时，必须检查稳定 ID、局部 key 解析、跨步骤目标、未解析标签、物理地址重算、被删目标失效证据和提交后回读；禁止依赖旧 index 继续编辑。
- 修改变量能力时，保持“列表可批量读取、对象按名称/索引精确读取、配置按单变量增删改、运行值按单变量设置”的公开语义。ChangeSet 中的变量声明是与流程同阶段提交的单变量策略，不得暴露整张变量表替换工具；配置提交不得把当前运行值重置为初始值。
- 修改提示词或 Goose 部署时，逐项检查：官方 system 功能规则基底 diff、EW-AI 品牌身份差异、自定义区块边界、Manifest 与输出副本、对应独立版本号、运行时部署路径、当前进程环境变量、缺失时只禁用 EW-AI、Profile 中无旧工具词，并记录本轮 prompt/context 哈希供日志定位。
- 修改工具集合或路由时，必须同步 `McpToolProfile` 与 `Program --verify-profile`；评估新增工具是否真的提供新的精确能力，能否由现有强类型工具自然承担。不得为了规避一次模型误用持续增加近义工具。
- 修改前台聊天、确认窗体或选择上下文时，检查 UI 线程安全、关闭/取消竞态、一个活动预演的状态机、流程/步骤/指令分层选择、内部事件不进入聊天正文、Markdown 渲染和结构化日志关联。
- 修改日志时，保证 `turnId/seq` 能串起用户输入、模型段、工具开始/结束、预演、确认、提交和轮次结束；主分析日志保留紧凑事实，完整报文只作底层取证。日志不得反向注入模型上下文，也不得在高速循环中逐指令同步写盘。
- 删除或回滚方案时，同步删除代码、Profile 入口、描述、Schema、Markdown、版本配置、测试和日志字段中的残留；退役方案继续出现在任一层都会重新造成错误路由。

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
- `IntentTemplates/` — 遗留中间意图模板，仅保留兼容；不属于 Goose 默认 Editor 写入链。
- `McpServer/` — 独立进程，把 Bridge 透传为 MCP Streamable HTTP 服务。
- `MotionControl/` — 运动控制（雷赛 LTDMC/LeiSei3000）。
- `Hmi/` — 平台仓库保留的 HMI 调试界面源码；`Automation.exe` 根据 `AppConfig.StartupView` 选择启动该 HMI 或平台编辑器。
- `../DeviceProject/Hmi/` — 与平台并列的独立设备 HMI 工程源码；`FrmHmiMain` 由 `MachineApp.exe` 启动，只能通过 `AutomationPlatformHost` 的公开 SDK 访问平台能力。
- `Engine/CustomFunc.cs` — 平台内部的自定义函数注册与执行容器，AI 不得直接修改；平台内置 HMI 与独立设备 HMI 的业务函数分别写入各自的 `Hmi/CustomFunctions.cs`，并统一通过 `IAutomationPlatform.RegisterCustomFunction` 注册。
- `PLC/` — PLC 通讯（S7/ModbusTcp）。
- `Runtime/` — `AutomationPlatformHost`、`AppConfigStorage`、`GooseConfigStorage`、`AutomationMcpServerManager`。
- `Stores/` — 各类配置 Store 与 `LocalFileLogger`。
- `FrmAiAssistant.cs` — AI 助手前台，承载 Goose ACP 聊天、预演确认、联调。
- `GooseAcpClient.cs` — 隐藏启动 `goose acp`，JSON-RPC over stdio 通信；AI 执行日志统一落 `D:\AutomationLogs\AIExecution\`。
- `FrmMain.cs` / `FrmProc.cs` / `FrmDataGrid.cs` / `FrmPropertyGrid.cs` — 可隐藏的平台编辑器与流程编辑链。

## 启动模式
- `Automation.exe` 无参数时根据 `AppConfig.StartupView` 启动平台编辑器 `FrmMain` 或平台内置 HMI，用于平台开发、配置与调试；其他参数一律拒绝。
- `../DeviceProject/MachineApp.exe` 是独立设备工程入口：同样读取自身 `Config/AppConfig.json` 的 `StartupView`，初始化平台内核后启动设备工程自己的 `FrmHmiMain` 或平台编辑器。
- HMI 和平台处于同一 UI 线程、同一进程、同一程序集，共用 `<exe>/Config/`。

## 运行时全局对象
- `SF` 是全局容器，持有窗体、`ProcessEngine`、`MotionCtrl`、各 Store、权限会话。
- `SF` 仅允许平台内部适配使用；`../DeviceProject/Hmi/` 禁止直接引用 `SF`、平台窗体、Store、引擎和原生运动对象。
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
- 启动层：正常 HMI 启动不启动 Bridge/MCP；首次显示平台编辑器或打开 AI 页面时，由 `AutomationMcpServerManager` 启动 Bridge 与 MCP，进程最终退出时统一关闭。辅助服务失败只禁用 EW-AI，不阻断平台/HMI 初始化。
- 上下文部署层：`GooseRuntimeProvisioner` 从 Manifest 优先部署 `system.md` 与 `automation.md`，分别维护版本；`system.md` 部署到 Goose 受管默认提示词路径，`automation.md` 由 `GooseAcpClient` 通过 `GOOSE_MOIM_MESSAGE_FILE` 仅注入当前 EW-AI 子进程。Provider/Model 和 UTF-8 PowerShell 也只覆盖当前子进程，不从客户 HMI 目录加载平台规范。
- Provider 层：Provider/Model 只作当前子进程环境变量覆盖，空值沿用 Goose 配置且不保存模型密钥。下拉框优先从 `%APPDATA%\Block\goose\config\config.yaml` 读取已注册 provider 名，解析由 `GooseConfigStorage.TryLoadGooseProviders` 完成。
- ACP 层：`FrmAiAssistant` 是唯一聊天前台；`GooseAcpClient` 隐藏启动 `goose acp`，通过 stdin/stdout 换行分隔 JSON-RPC 调用 `initialize/session/new/session/prompt/session/cancel`。`session/new.mcpServers` 使用 `name/type/url/headers`，HTTP 类型即使无 Header 也传空数组。
- 选择上下文层：每次 prompt 由 `GooseAcpClient.BuildSelectionContext` 附加当前编辑器选择。只展开实际有效的 `process -> step -> operation` 层级，包含显示 index 和稳定 ID；选择只用于定位，不等于用户授权修改。
- MCP 注入层：ACP 会话只注入 Automation MCP 的 HTTP 地址；Goose 不直接访问 Bridge Named Pipe。当前可见工具完全由 `McpToolProfile` 决定，Editor 提供知识/读取、有限诊断、ChangeSet 写入和已授权运行工具，Diagnostic 不暴露配置写入。
- 读取与表达层：AI 先复用会话中已验证且仍适用的事实。目标未知时使用目录/搜索，目标已知时直接读取精确对象或对应 Schema；普通业务意图优先选能准确表达的语义 `kind`，已知原生 `operaType` 或语义层表达不足时读取精确原生 Schema 并使用 `native.operation`。Guide 只在运行语义确实不明确时按类型读取。
- MCP/Bridge 传输层：`AutomationMcpTools` 接收强类型参数，经 `AutomationBridgeClient` 发送到本机 Bridge；Bridge 管道名固定 `AutomationBridgePipe`，协议为 `4字节长度前缀 + UTF-8 JSON`。基础类型、数量和大小错误应在 MCP 或 Bridge 工作线程发现，只有访问 WinForms/Store 状态和提交时才进入 UI 线程。
- 配置预演层：流程结构默认通过 `preview_change_set` 提交当前阶段。Bridge 使用 `AiChangeSetCompiler` 在流程、变量和 `BuildAiResourceSnapshot` 快照上编译，解析语义/原生指令、资源策略和符号跳转，计算 readiness，冻结编译结果并记录基础状态哈希；返回对象仍是 `preview_only`。
- 前台确认层：`FrmAiAssistant` 根据预演事件显示一个确认窗体，用户确认只把对应预演记录标记为 confirmed。聊天气泡不显示内部 previewId、自动确认或继续提交指令；AI 通过工具返回的 `status/confirmed/allowedTransitions` 感知状态。
- 提交层：`apply_change_set` 只接收同一预演的 `previewId`。Bridge 再校验确认状态、过期时间和基础状态哈希，然后使用冻结的编译结果执行事务保存与发布；不得重新让模型提交原始 ChangeSet，也不得在 apply 时重新编译出另一份结果。
- 身份与继续编辑层：预演中的 `plannedProcIndex` 和局部 key 不能用于读取已提交对象。提交后以 `createdObjects/affectedProcesses` 返回的 `procId/stepId/opId` 和当前真实索引继续读取、校验或下一阶段编辑。
- 验证层：`apply_change_set` 只证明阶段已提交；`validate_proc` 证明结构，`readinessStatus/runnable` 证明启动条件，`run_proc_test` 或明确授权的运行工具证明实际观测。最终报告必须按用户目标选择证据，不把这些层级合并成“全部完成”。
- 变量层：变量列表可以分页批量读取，单变量可按名称或索引精确读取；配置增删改和运行值设置均为单变量公开工具。ChangeSet 只在流程阶段确实依赖变量声明时承载逐变量策略，禁止整表公开替换和因配置提交重置运行值。
- 运行层：配置保存、运行时值修改和流程运行是不同权限与工具链。用户要求测试时使用有界 `run_proc_test`；只有明确要求持续运行才使用 `start_proc`。启动前统一经过 `ProcessReadinessService`，设备、人身或状态不确定时按安全准则停止/报警。
- 日志层：Goose/ACP、MCP、预演和提交事件统一关联到 `D:\AutomationLogs\AIExecution\`；日常分析以 `D:\AutomationLogs\AIExecution\Analysis\` 为主入口，按 `turnId/seq` 读取 `turn.started`、`model.segment`、`tool.started/tool.finished`、`preview.*`、`turn.completed/failed`。紧凑主日志用于链路判断，完整 ACP/MCP/Bridge 报文仅作底层取证。

## 日志
- 所有运行日志统一放在 `D:\AutomationLogs\`，禁止写入程序执行目录。
- 流程引擎日志：`D:\AutomationLogs\ProcessLog\`（`LocalFileLogger`，按天 + 5MB 分卷）。
- Bridge 异常日志：`D:\AutomationLogs\Bridge\`，记录请求路径、请求体摘要和完整异常堆栈。
- 统一结构化旁路：`D:\AutomationLogs\Structured\`，使用单行 JSONL，包含 Bridge、流程运行摘要和运行时异常；原 TXT/CSV 日志保留兼容。流程运行默认只写 `process.started/process.summary/operation.failed`，禁止在正常高速循环中逐指令同步落盘。
- 运行信息页：`FrmInfo`，`lvInfoLog` 虚拟模式，环形缓冲 200 条。
