## 语言约定
- 所有代码注释、技术文档、提交说明一律使用**简体中文**。
- 代码中的标识符、API 名称、JSON 字段保持英文；用户可见字符串按 UI 设计需求呈现。
- 面向开发者的说明、分析、总结全部用简体中文输出。

## 编码约定
- 避免过度包装：仅做透传/一行调用的 helper 不要新增，直接在调用处展开。
- 多轮改动后必须清理冗余/无效代码与未使用的配置/引用，禁止保留不起作用的代码残留。
- 需求回滚或取消时，必须同步移除相关实现/入口/配置，禁止保留“未来可能用到”的遗留代码。

## 安全准则
- 安全是最高准则：任何异常或不确定状态必须优先停机/报警，禁止继续运行。
- 参数解析必须严格遵循定义范式，禁止对参数进行宽松解析或容错修复。

## 会话启动
- 每次新对话开始后，第一步必须先读取本文件（`AGENTS.md`/`AGENTS.MD`）再执行任何分析或改动。
- 首次回复需明确一句“已读取 AGENTS 约束并按其执行”，用于确认规则已生效。

## 编译验证（固定命令）
- 推荐在 WSL 下直接调用 Visual Studio 2026 的 MSBuild：
  - `"/mnt/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" Automation.sln /t:Build /p:Configuration=Debug /m`
- 需要全量重编译时使用：
  - `"/mnt/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" Automation.sln /t:Rebuild /p:Configuration=Debug /m`
- 只编译单项目时使用：
  - `"/mnt/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" Automation.csproj /t:Build /p:Configuration=Debug /m`
- 若路径变化，先定位 MSBuild 再编译：
  - `find "/mnt/c/Program Files/Microsoft Visual Studio" -path "*MSBuild*Bin/MSBuild.exe" | head -n 5`
- 禁止使用旧版 .NET Framework MSBuild（`/mnt/c/Windows/Microsoft.NET/Framework64/v4.0.30319/MSBuild.exe`），该版本会导致 `LangVersion=latest` 编译失败。
- 若本次改动新增/改名/移动 `.cs` 文件，必须同步更新 `Automation.csproj` 中对应的 `<Compile Include="...">`。
- 编译前必须执行一次“项目文件一致性检查”：确认新增/改名后的 `.cs` 文件都已在 `Automation.csproj` 注册。
- 一致性检查通过后允许执行编译；检查不通过时禁止编译并先修复项目文件。

# Automation Agents 速览

项目概述：基于 Windows Forms 的低代码自动化平台，核心是可编辑的“流程/步骤/操作”图谱，协同运动控制卡、IO、TCP/串口通讯与自定义逻辑。入口 `Program.cs` → `FrmMain`，主窗体内嵌各功能子窗体。

- **配置布局**：`Config/Work/*.json` 存流程；`Config/value.json` 存变量；`Config/card.json`、`Config/IOMap.json`、`Config/DataStruct.json`、`Config/DataStation.json` 定义硬件/IO/数据结构；`Config/Account.json` 存账户权限（加密文本，前缀 `ENC:`）；`Config/MarkPoints.json` 记录标记点。
- **全局入口**：`SF` 静态类持有所有窗体、运动控制器 `MotionCtrl`、运行器 `ProcessEngine` 以及路径 `workPath`、`ConfigPath`；同时维护账户系统（`accountStore`/`userSession`/`userContextStore`/`userLoginStore`）、安全锁定状态 `SecurityLocked`，UI/运行期均从此处获取依赖。

## 核心概念
- **流程模型**：`Proc` → `Step` → `OperationType`。通过 `FrmProc`（树）、`FrmDataGrid`（操作列表）、`FrmPropertyGrid`（属性编辑）修改，序列化为 JSON（启用 `TypeNameHandling.All`）。
- **运行引擎**：`ProcessEngine` 以 `ProcAgent` + 命令队列管理单流程执行，`ProcHandle` 仅在引擎内记录索引/状态，UI 通过 `EngineSnapshot` + `SnapshotChanged` 订阅状态。`RunProc`/`RunStep` 支持暂停、单步、跳转、报警；`ExecuteOperation` 将具体操作派发到对应执行函数。
- **文件拆分**：引擎内核在 `Engine/ProcessEngine.Core.cs`，操作执行在 `Engine/ProcessEngine.Operations.cs`，模型在 `Engine/ProcessEngine.Models.cs`。
- **变量**：`FrmValue` 管理 0–999 的 `DicValue`，含类型/名称/备注，持久化到 `Config/value.json`，提供按名/按索引读写。
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
- `FrmComunication` 管理 TCP（`SocketInfo`/`Socketer`）与串口（`SerialPortInfo`/`SerialPorter`），支持连/断、异步接收日志、十六进制视图与发送；流程操作按名称引用。
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
- `Config/value.json` 存变量字典，`FrmValue` 以 0–999 索引映射到表格。
- `Config/card.json`、`Config/IOMap.json`、`Config/DataStation.json` 分别存卡、IO 映射、工站数据。
- `Config/DataStruct.json` 存数据结构集合，项内包含 `str`/`num` 字典。
- `Config/AlarmInfo.json`、`Config/SocketInfo.json`、`Config/SerialPortInfo.json`、`Config/IODebugMap.json`、`Config/MarkPoints.json` 存报警/通讯/调试/标记点。
- `Config/Account.json` 存账户权限，必须为加密格式（前缀 `ENC:`），明文/字段不合法会触发锁定模式。

## 依赖与驱动
- `MotionControl/LTDMC.cs` P/Invoke `LTDMC.dll`，`MotionControl/LeiSei3000.cs` 实现 LS 运动与 IO。
- `packages.config` 使用 `Newtonsoft.Json`、`OxyPlot`、`S7.Net`、`System.Drawing.Common` 等依赖。
- 运行时会读取 `Config/card_0.ini` 等 ini 做运动卡参数下载。
