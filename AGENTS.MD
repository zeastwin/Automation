## 语言约定
- 所有代码注释、技术文档、提交说明一律使用**简体中文**。
- 代码中的标识符、API 名称、JSON 字段保持英文；用户可见字符串按 UI 设计需求呈现。
- 面向开发者的说明、分析、总结全部用简体中文输出。

## 编码约定
- 避免过度包装：仅做透传/一行调用的 helper 不要新增，直接在调用处展开。
- 多轮改动后必须清理冗余/无效代码与未使用的配置/引用，禁止保留不起作用的代码残留。
- 需求回滚或取消时，必须同步移除相关实现/入口/配置，禁止保留“未来可能用到”的遗留代码。

## 文档维护准则
- 任何功能改动完成后，必须同步更新本文件中对应模块说明；未更新文档视为任务未完成。
- 变更涉及协议、配置默认值、异常处理策略时，必须在本文件明确写出迁移影响与排查要点。

## 安全准则
- 安全是最高准则：任何异常或不确定状态必须优先停机/报警，禁止继续运行。
- 参数解析必须严格遵循定义范式，禁止对参数进行宽松解析或容错修复。

## 会话启动
- 每次新对话开始后，第一步必须先读取本文件（`AGENTS.md`/`AGENTS.MD`）再执行任何分析或改动。
- 首次回复需明确一句“已读取 AGENTS 约束并按其执行”，用于确认规则已生效。

## 编译验证（固定命令）
- 推荐在 WSL 下直接调用 Visual Studio 2026 的 MSBuild：
  - `"/mnt/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" Automation.sln /t:Build /p:Configuration=Debug /p:OutDir=bin/Debug/ /m`
- 需要全量重编译时使用：
  - `"/mnt/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" Automation.sln /t:Rebuild /p:Configuration=Debug /p:OutDir=bin/Debug/ /m`
- 只编译单项目时使用：
  - `"/mnt/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" Automation.csproj /t:Build /p:Configuration=Debug /p:OutDir=bin/Debug/ /m`
- 若路径变化，先定位 MSBuild 再编译：
  - `find "/mnt/c/Program Files/Microsoft Visual Studio" -path "*MSBuild*Bin/MSBuild.exe" | head -n 5`
- 禁止使用旧版 .NET Framework MSBuild（`/mnt/c/Windows/Microsoft.NET/Framework64/v4.0.30319/MSBuild.exe`），该版本会导致 `LangVersion=latest` 编译失败。
- 若本次改动新增/改名/移动 `.cs` 文件，必须同步更新 `Automation.csproj` 中对应的 `<Compile Include="...">`。
- 编译前必须执行一次“项目文件一致性检查”：确认新增/改名后的 `.cs` 文件都已在 `Automation.csproj` 注册。
- 一致性检查通过后允许执行编译；检查不通过时禁止编译并先修复项目文件。
- 非用户明确要求时，禁止使用 `Debug_codex_*` 等临时输出目录，统一输出到 `bin/Debug/`。

# Automation Agents 速览

项目概述：基于 Windows Forms 的低代码自动化平台，核心是可编辑的“流程/步骤/操作”图谱，协同运动控制卡、IO、TCP/串口通讯与自定义逻辑。入口 `Program.cs` → `FrmMain`，主窗体内嵌各功能子窗体。

- **配置布局**：`Config/Work/*.json` 存流程；`Config/value.json` 存变量；`Config/card.json`、`Config/IOMap.json`、`Config/DataStruct.json`、`Config/DataStation.json` 定义硬件/IO/数据结构；`Config/Account.json` 存账户权限（加密文本，前缀 `ENC:`）；`Config/MarkPoints.json` 记录标记点。
- **全局入口**：`SF` 静态类持有所有窗体、运动控制器 `MotionCtrl`、运行器 `ProcessEngine` 以及路径 `workPath`、`ConfigPath`；同时维护账户系统（`accountStore`/`userSession`/`userContextStore`/`userLoginStore`）、安全锁定状态 `SecurityLocked`，UI/运行期均从此处获取依赖。

## 核心概念
- **流程模型**：`Proc` → `Step` → `OperationType`。通过 `FrmProc`（树）、`FrmDataGrid`（操作列表）、`FrmPropertyGrid`（属性编辑）修改，序列化为 JSON（启用 `TypeNameHandling.All`）。
- **流程反序列化策略**：读取流程 JSON 时使用 `ObjectCreationHandling.Replace`，避免操作构造函数中的默认集合项在重启后被重复追加。
- **运行引擎**：`ProcessEngine` 以 `ProcAgent` + 命令队列管理单流程执行，`ProcHandle` 仅在引擎内记录索引/状态，UI 通过 `EngineSnapshot` + `SnapshotChanged` 订阅状态。`RunProc`/`RunStep` 支持暂停、单步、跳转、报警；`ExecuteOperation` 将具体操作派发到对应执行函数。
- **文件拆分**：引擎内核在 `Engine/ProcessEngine.Core.cs`，操作执行在 `Engine/ProcessEngine.Operations.cs`，模型在 `Engine/ProcessEngine.Models.cs`。
- **变量**：`FrmValue` 管理 0–999 的 `DicValue`，含类型/名称/备注，持久化到 `Config/value.json`，提供按名/按索引读写。
  - 通过 `FrmMenu` 的“变量”按钮进入变量表时，会强制执行一次表格值刷新（以运行时 `valueStore` 为准）。
  - 变量表编辑校验为“名称必填”；`Type` 为空时自动回填 `double`。
  - `double` 类型在值为空时自动回填 `0` 并持久化；`string` 类型允许空字符串并可直接持久化。
  - 迁移影响与排查：旧版本依赖“值必填”拦截的用法需复核；若类型或值未落盘，优先检查变量名是否为空或重名（重名会被 `TrySetValue` 拒绝）。
- **数据结构**：`FrmDataStruct` 定义结构体集合，操作可对项进行设/取/复制/插入/删除/查找/计数。

## 账户与权限
- **角色**：`SystemAdmin` / `Admin` / `Operator`（`Security/UserAccount.cs`）。
- **默认系统管理员**：用户名 `system`，口令 `software_123`。该账户不可新增/删除/禁用/改名，口令不可修改，权限为全量。
- **账号配置**：`Config/Account.json` 为加密文本（AES-CBC + HMAC-SHA256 + PBKDF2，前缀 `ENC:`），只允许字段 `Accounts`，账户字段为 `Id`/`UserName`/`Role`/`PasswordHash`/`Salt`/`Iterations`/`Disabled`/`Permissions`。解密或校验失败会进入锁定模式。
- **锁定模式**：`SF.SetSecurityLock` 记录日志/提示并停止所有流程；`SF.SecurityLocked` 时仅允许默认系统管理员登录为恢复会话（`UserSession.IsRecovery`），恢复会话只允许 `Account.Manage`。
- **登录流程**：程序启动不弹登录；工具条“用户”按钮打开 `FrmLogin` 完成登录或切换账号，成功后 `SF.SetUserSession` 刷新权限。
- **账户管理**：`FrmAccountManager`（分栏列表 + 权限树 + “全选”），仅系统管理员可新建账号；重置口令通过 `FrmChangePassword`。
- **权限粒度**：以“模块入口 + 关键动作”为主（例如流程运行/编辑/查找、报警配置、IO 监视、打开程序文件夹等），清单见 `Security/PermissionCatalog.cs`。若勾选模块内动作，必须勾选该模块入口权限（保存时校验）。
- **安全动作**：流程“停止/停止全部”不做权限限制，所有角色都可执行。
- **外部接口**：`SF.userContextStore` 提供 `GetUserName/GetUserRole/GetSnapshot`；`SF.userLoginStore` 提供 `TryLogin/TryLogout/IsLoggedIn` 供外部调用。

## 操作库（OperationType）
可用于拼装流程的类别：
- 自定义：`CallCustomFunc` 调用 `CustomFunc` 中注册的委托。
- IO：`IoOperate`（写输出附带前后延时）、`IoCheck`（等待输入/输出），经 `FrmIO.DicIO` 与 `MotionCtrl` 解析。
- 流程协同：`ProcOps`（启动/停止流程）、`WaitProc`（等待流程状态）、`Goto`/`ParamGoto`（分支）、`Delay`。
- 变量/字符串：`GetValue`、`ModifyValue`、`StringFormat`、`Split`、`Replace`。
- 结构体：`SetDataStructItem`、`GetDataStructItem`、`CopyDataStructItem`、`InsertDataStructItem`、`DelDataStructItem`、`FindDataStructItem`、`GetDataStructCount`。
- 通讯：`TcpOps`/`WaitTcp`/`SendTcpMsg`/`ReceoveTcpMsg`，`SerialPortOps`/`WaitSerialPort`/`SendSerialPortMsg`/`ReceoveSerialPortMsg`。
- 运动：`HomeRun`、`StationRunPos`、`StationRunRel`、`SetStationVel`、`StationStop`、`WaitStationStop`（回零、绝对/相对运动、速度覆盖、停止与等待到位）。

## 硬件与 IO
- **卡与轴**：`FrmCard` 编辑运动控制卡（`ControlCard`），轴记录回零模式、脉冲当量、速度/加减速限制等，保存到 `card.json`。
- **工站模型**：`DataStation` 将轴编组为工站，带点位与运动参数，存于 `DataStation.json`，供站类操作引用。
- **IO 映射**：`FrmIO` 将物理 IO 映射为命名 IO（`IOMap.json`），维护输入/输出下拉列表，`FrmIODebug` 支持调试映射。
- **运动控制**：`MotionCtrl` 统一封装卡类型；`InitCardType` 绑定 `ls`（LTDMC）实现；`FrmMain_Load` 完成卡初始化、配置下载、使能伺服、设定当量。

## 通讯界面
- `FrmComunication` 管理 TCP 与串口配置、连接控制、发送与日志显示；接收日志按 `SF.comm.Log` 事件异步写入，并在 UI 侧处理跨线程调用。
  - TCP/串口表仅暴露 `分隔符` 一列，选项固定为：`无`、`\n`、`\r\n`。
  - 映射规则：`无` -> `FrameMode=Raw`；`\n` -> `FrameMode=Delimiter` + `FrameDelimiter=\n`；`\r\n` -> `FrameMode=Delimiter` + `FrameDelimiter=\r\n`。
  - 退出释放策略：窗体 `Dispose` 前必须先停止通讯状态定时器、取消循环发送任务并解除 `SF.comm.Log` 订阅，避免退出阶段回调访问已释放控件。
- `CommunicationHub` 统一管理 `TcpChannel`/`SerialPortChannel`，支持 `Raw` 与 `Delimiter` 帧模式、严格参数校验、统一发送/接收结果模型（`CommReceiveResult`）。
- `ProcessEngine.Operations.Comm` 通过 `CommunicationHub` 执行 `TcpOps/SerialPortOps/Send/Receive/SendReceive`，通讯异常统一转报警，禁止静默失败。
- `FrmState`/`FrmInfo` 展示运行状态与日志；`FrmAlarmConfig` 存报警元数据供操作触发时使用。

## 界面速查
- 导航/菜单：`FrmMenu`。
- 流程编辑：`FrmProc`、`FrmDataGrid`、`FrmPropertyGrid`、`FrmToolBar`（新增/保存/取消/运行/暂停/单步/标记）。
- 变量/IO/卡/工站/数据结构编辑：`FrmValue`、`FrmIO`、`FrmCard`、`FrmStation`、`FrmDataStruct`、`FrmIODebug`。
- 监控/搜索：`FrmState`、`FrmSearch`/`FrmSearch4Value`、`FrmInfo`。
- 账户登录/管理：`FrmLogin`、`FrmAccountManager`、`FrmChangePassword`。

## 扩展指引
- 新增操作：继承 `OperationType` 定义属性/转换器/报警行为，在 `ProcessEngine.ExecuteOperation` 添加分支并实现执行函数。
- 自定义逻辑：向 `CustomFunc.functionMap` 注册委托，名称即出现在 `CallCustomFunc`。
- 硬件调整：使用 `FrmCard`/`FrmIO` UI 修改，`FrmToolBar` 保存后刷新缓存列表/字典。
- 运行问题：操作设置 `ProcHandle.isAlarm`/`isGoto`，按 `AlarmType` 选择跳转或弹框（`Message`）策略。
- 错误处理：任何错误（含超时/未连接/异常）必须设置 `ProcHandle.isAlarm = true` 并填写 `alarmMsg`，禁止静默吞错。
- 新增权限：在 `Security/PermissionCatalog.cs` 增加 `PermissionKeys` 与定义项，必要时补充默认角色权限与分组入口校验。

## 补充梳理（实现细节）
- **运行时目录**：`SF.ConfigPath`/`SF.workPath` 由 exe 路径拼出，调试时写入 `bin/Debug/Config`（Release 同理），流程按 `0.json`…`N.json` 编号加载。
- **窗体装配**：`FrmMain` 构造时创建所有子窗体并写入 `SF`，`loadFillForm` 以 `TopLevel=false` + `Dock=Fill` 嵌入面板。
- **启动顺序**：`Program.Main` 初始化账户系统并加载 `Account.json`，`FrmMain_Load` 再依次刷新变量/IO/卡/数据结构/通讯/报警，并完成运动卡初始化、配置下载、使能伺服、设定当量。
- **页面切换**：`FrmMenu` 通过 `SF.curPage` 切换流程编辑/卡与 IO/工站等界面，动态重排面板与工具条按钮可见性。
- **流程调度**：`ProcAgent` + `ProcessControl` 同步门闩控制运行/暂停/单步，UI 仅通过 `EngineSnapshot` 读状态并发命令。
- **报警处理**：执行异常标记 `isAlarm`，`Message` 弹窗根据 `AlarmType` 触发停止/忽略/跳转或多按钮选择。
- **权限刷新**：`SF.SetUserSession`/`SF.SetSecurityLock` 会调用 `SF.RefreshPermissionUi`，统一刷新 `FrmMenu`/`FrmToolBar`/`FrmValue`/`FrmDataGrid`/`FrmAccountManager`。

## 配置文件补充
- `Config/Work/*.json` 按索引存单个流程，加载时以文件数量决定流程数量。
- `Config/Work/*.json` 读取时会替换集合实例（`ObjectCreationHandling.Replace`），确保 `Count` 类字段与参数列表不会因默认构造项被额外追加而失配。
- `Config/value.json`、`Config/card.json`、`Config/DataStruct.json`、`Config/AlarmInfo.json`、`Config/PlcDevice.json`、`Config/PlcMap.json` 的读取同样使用 `ObjectCreationHandling.Replace`，避免构造默认集合项被重复叠加。
- 迁移影响与排查：若历史版本已误保存“空白参数项”，升级后不会继续新增，但历史空项仍会保留；需在流程编辑界面删除空项并保存。排查时优先检查 `Config/Work/*.json` 中对应操作的 `Count` 与 `Params/$values`（或同类列表）的数量是否一致。
- `Config/value.json` 存变量字典，`FrmValue` 以 0–999 索引映射到表格。
- `Config/card.json`、`Config/IOMap.json`、`Config/DataStation.json` 分别存卡、IO 映射、工站数据。
- `Config/DataStruct.json` 存数据结构集合，项内包含 `str`/`num` 字典。
- `Config/AlarmInfo.json`、`Config/SocketInfo.json`、`Config/SerialPortInfo.json`、`Config/IODebugMap.json`、`Config/MarkPoints.json` 存报警/通讯/调试/标记点。
- `Config/AppConfig.json` 存程序运行参数，当前包含 `CommMaxMessageQueueSize`（通讯接收队列长度）；默认值为 `1000`。
- 若 `Config/AppConfig.json` 不存在，程序会自动生成默认文件并继续加载；若自动生成失败则按错误处理（启动失败并提示）。
- `Config/SocketInfo.json` 支持 `FrameMode`（`Raw`/`Delimiter`）、`FrameDelimiter`（仅 `Delimiter` 生效）、`EncodingName`、`ConnectTimeoutMs`；当前默认 `FrameMode` 为 `Raw`。
- `Config/Account.json` 存账户权限，必须为加密格式（前缀 `ENC:`），明文/字段不合法会触发锁定模式。

## 依赖与驱动
- `MotionControl/LTDMC.cs` P/Invoke `LTDMC.dll`，`MotionControl/LeiSei3000.cs` 实现 LS 运动与 IO。
- `packages.config` 使用 `Newtonsoft.Json`、`OxyPlot`、`S7.Net`、`System.Drawing.Common` 等依赖。
- 运行时会读取 `Config/card_0.ini` 等 ini 做运动卡参数下载。
