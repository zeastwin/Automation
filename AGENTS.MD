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
- 变更涉及协议、配置默认值、异常处理策略时，必须在本文件写明影响范围、运行约束与排查入口。
- 本文件定位为“首次接手项目的全局说明书”：读者不依赖历史记录，仅通过本文件即可建立完整系统认知。
- 文档只描述当前状态，不写“改动过程/前后对比/临时方案/兼容过渡”等叙事。
- 全文按“先系统、后模块、再细节”组织；任何章节都从整体职责、边界、输入输出开始描述。
- 涉及顺序、层级、布局、协议、权限、配置等结构化信息时，直接给出当前完整清单。
- 更新文档前先对照代码事实，确保说明与实际实现一致。

## 安全准则
- 安全是最高准则：任何异常或不确定状态必须优先停机/报警，禁止继续运行。
- 参数解析必须严格遵循定义范式，禁止对参数进行宽松解析或容错修复。

## 会话启动
- 每次新对话开始后，第一步必须先读取本文件（`AGENTS.md`/`AGENTS.MD`）再执行任何分析或改动。
- 首次回复需明确一句“已读取 AGENTS 约束并按其执行”，用于确认规则已生效。

## 不需要编译验证

# Automation 全局说明（一次性现状版）

## 1. 系统定位与主链路
- 项目是基于 Windows Forms 的低代码自动化平台，核心对象是“流程（Proc）-步骤（Step）-操作（OperationType）”。
- 系统围绕五类能力运行：流程编排与执行、运动控制与 IO、TCP/串口通讯、PLC 通讯、账户权限与安全锁定。
- 启动链路：
  - `Program.Main` 先处理 `AIFlow.AiFlowCli.TryHandle(args)`，命中后关闭 AI 页面入口（`SF.AiFlowEnabled=false`）。
  - 启动阶段强制加载 `Config/AppConfig.json`；文件缺失时自动生成默认值（`CommMaxMessageQueueSize=1000`），加载失败会提示错误并终止启动。
  - 初始化账户系统（`AccountStore/UserContextStore/UserLoginStore`），加载 `Config/Account.json`。
  - 启动时尝试使用默认系统管理员 `system/software_123` 登录会话。
  - 进入 `FrmMain`，装配所有子窗体与运行时 Store，创建 `ProcessEngine`，加载配置并初始化硬件。

## 2. 运行时全局对象（SF）
- `SF` 是全局运行容器，统一持有窗体实例、`ProcessEngine`、`MotionCtrl`、各类配置 Store 与权限会话。
- 路径由可执行目录派生：
  - `SF.ConfigPath = <exe>/Config/`
  - `SF.workPath = <exe>/Config/Work/`
- 权限与安全入口：
  - `SF.EnsurePermission`：动作级权限校验并负责错误提示。
  - `SF.HasPermission`：状态/按钮可用性校验。
  - `SF.SetSecurityLock`：记录原因、停止全部流程、刷新权限 UI。
  - `SF.SetUserSession`：切换用户并刷新权限 UI。
- 流程发布入口：`SF.PublishProc(procIndex)`，发布前执行流程结构归一化和跳转地址校验。

## 3. 主窗体装配与生命周期
- `FrmMain` 构造阶段完成：
  - 初始化 `cardStore/valueStore/dataStructStore/trayPointStore/alarmInfoStore/comm/plcStore`。
  - 创建 `ProcessEngine` 并注入 `EngineContext`（流程、变量、数据结构、运动、通讯、PLC、IO 映射、工站、自定义函数）。
  - 绑定日志器（UI + 本地文件）与报警处理器（`WinFormsAlarmHandler`）。
  - 创建并嵌入子窗体（`TopLevel=false + Dock=Fill`）。
- `FrmMain_Load` 执行顺序：
  - 刷新变量、卡/IO、数据结构、通讯、报警、PLC 配置。
  - 初始化运动控制（`InitCardType -> InitCard -> DownLoadConfig -> SetAllAxisSevonOn -> SetAllAxisEquiv`）。
  - 非锁定模式下自动启动 `Proc.head.AutoStart=true` 且未禁用的流程。
- 运行期系统状态变量：
  - 固定读取变量名：`复位状态`、`系统状态`。
  - `复位状态` 允许值为 `0/1/2`，类型必须为 `double`。
  - 任一前置条件不满足触发 `FailSystemStatus` 并进入安全处理路径。
- 关闭流程：
  - 停监控线程、停全部流程并等待、释放通讯、停止运动、释放引擎、保存流程停点与关键配置。

## 4. 页面结构与导航
- `FrmMenu` 顶部按钮当前从左到右完整顺序：
  - 流程、工站、变量、IO调试、通讯、PLC、变量调试、控制卡配置、AI。
- `AI` 按钮显示前提：
  - `SF.AiFlowEnabled=true` 时显示并可按权限启用。
  - `SF.AiFlowEnabled=false` 时隐藏且禁用。
- 页面切换由 `SF.curPage` 驱动，`FrmMenu` 根据目标页面重排主窗体区域可见性：
  - 流程页：树 + 指令表 + 属性区 + 工具栏 + 状态区。
  - 工站页：主区显示 `FrmStation`，隐藏流程编辑区。
  - 控制卡/IO页：树区显示 `FrmCard`，表区显示 `FrmIO`，工具栏切换为 IO 监视动作。
  - 变量调试页：主区显示 `FrmValueDebug`，隐藏流程编辑区。
- 变量按钮行为：
  - 进入 `FrmValue` 前执行 `SF.frmValue.FreshFrmValue()`，界面值以运行态变量为准。

## 5. 工具栏与交互行为
- `FrmToolBar` 负责保存/取消、运行控制、搜索定位、报警配置、IO 监视、程序设置、账户管理、用户登录。
- 运行动作权限：
  - 暂停/继续、单步依赖 `Process.Run`。
  - 停止、停止全部不做权限限制。
  - 停止全部会跳过名称以“系统”开头的流程。
- 常用辅助动作权限：
  - 流程搜索/定位依赖 `Process.Search`。
  - IO 监视依赖 `IOMonitor.Use`。
  - 打开程序目录依赖 `Tool.OpenConfigFolder`。
  - 账户管理依赖 `Account.Manage`（恢复会话同样允许）。
- 右键目标策略（统一按鼠标命中项）：
  - `FrmProc`：右键空白会清空流程/步骤/操作选择并清空属性绑定。
  - `FrmDataGrid`：右键空白会清空选中行并取消菜单目标。
  - `FrmStation` 点位表：右键空白取消菜单，不回退历史行。

## 6. 流程模型与编辑体系
- 模型关系：`Proc -> Step -> OperationType`。
- 编辑器分工：
  - `FrmProc` 管理流程树、流程/步骤增删与结构归一化。
  - `FrmDataGrid` 管理步骤内操作列表、右键菜单、拖拽重排。
  - `FrmPropertyGrid` 管理对象属性编辑。
- 指令编号显示约束：
  - 指令表“编号”列固定绑定 `OperationType.Num`。
  - 属性编辑器不再动态改写 `Num` 的 `Browsable` 特性，避免影响数据绑定导致编号列空白。
- 流程加载规则（`FrmProc.Refresh`）：
  - 扫描 `Config/Work/*.json`，索引必须从 `0` 连续递增。
  - 缺失或加载失败会记入 `loadErrors`，设置 `SF.ProcConfigFaulted=true` 并触发停机提示。
- 流程保存规则：
  - 支持单流程保存与重建 `Work` 目录连续索引。
  - 跳转地址统一使用 `proc-step-op`，禁止跨流程，保存前做边界校验。
- 指令超时默认值策略：
  - 统一约束：所有带“超时”参数的指令默认值禁止为 `0`。
  - 通用超时容器 `TimeOutC.TimeOut` 默认 `3000ms`（`IoCheck`、`WaitProc` 等沿用该默认值）。
  - 通讯等待/接收指令默认超时为 `3000ms`（`WaitTcpParam`、`WaitSerialPortParam`、`ReceoveTcpMsg`、`ReceoveSerialPortMsg`）。
  - 运动等待类默认超时为 `120000ms`（`StationRunPos`、`StationRunRel`、`WaitStationStop`）。

## 7. 流程引擎（ProcessEngine）
- 引擎结构：
  - 每流程一个 `ProcAgent`，命令队列容量 128。
  - 命令类型包含 `StartAt/RunSingleOpOnce/Pause/Resume/Step/Stop`。
  - `ProcessControl` 用信号门闩控制暂停、单步、停止。
- 状态发布：
  - 引擎维护 `EngineSnapshot`，通过 `SnapshotChanged` 发布；`FrmMain` 用缓存 + 定时器合并刷新 UI。
- 执行主链路：
  - `RunProc -> RunStep -> ExecuteOperation`。
  - 运行过程中支持断点、暂停信号、单步、热发布流程映射（按 Step/Operation 的 `Id` 对位）。
  - 单步状态语义：单步放行后当前指令执行期间状态显示为 `Running`，当前指令执行完成并进入下一条指令待执行阶段时状态恢复为 `SingleStep`。
  - 热更新日志语义：热更新成功默认不输出日志，热更新失败才记录错误日志并触发报警/停机流程。
- 报警模型：
  - 任意异常通过 `MarkAlarm` 写入 `ProcHandle.isAlarm/alarmMsg`。
  - 报警决策支持 `Stop/Ignore/Goto1/Goto2/Goto3`。
  - 弹框类报警由 `IAlarmHandler`（`WinFormsAlarmHandler`）在 UI 线程处理。

## 8. 操作库全量分类（ExecuteOperation 分发）
- 自定义与逻辑：`CallCustomFunc`、`Delay`、`Goto`、`ParamGoto`、`PopupDialog`。
- IO：`IoOperate`、`IoCheck`、`IoLogicGoto`。
- 流程协同：`ProcOps`、`WaitProc`。
- 变量与字符串：`GetValue`、`ModifyValue`、`StringFormat`、`Split`、`Replace`。
- 数据结构：`Set/Get/Copy/Insert/Del/Find/GetCount DataStructItem`。
- 通讯：`TcpOps`、`WaitTcp`、`SendTcpMsg`、`ReceoveTcpMsg`、`SerialPortOps`、`WaitSerialPort`、`SendSerialPortMsg`、`ReceoveSerialPortMsg`、`SendReceoveCommMsg`。
- PLC：`PlcReadWrite`。
- 运动与工站：`HomeRun`、`StationRunPos`、`StationRunRel`、`SetStationVel`、`StationStop`、`WaitStationStop`、`ModifyStationPos`、`GetStationPos`、`CreateTray`、`TrayRunPos`。

## 9. 运动控制、卡、工站、IO
- 控制卡与轴：
  - `CardConfigStore` 持久化 `card.json`。
  - 轴参数包含脉冲当量、回零模式、速度参数等。
  - 运动控制封装入口 `MotionCtrl`，卡类型由 `InitCardType` 初始化。
- 工站：
  - `DataStation` 持有轴映射、回零顺序、点位集合（`ListDataPos`/`dicDataPos`）和运行状态。
  - `DataStation.json` 为工站与点位持久化文件。
- IO：
  - `FrmIO` 管理 `IOMap` 与命名 IO 字典 `DicIO`，配置文件为 `IOMap.json`。
  - IO 监视由 `FrmIO.ToggleIOMonitor` 与工具栏按钮联动。

## 10. 通讯系统（TCP/串口）
- 运行结构：
  - `CommunicationHub` 统一管理 TCP Client/Server 与 SerialPort 通道。
  - 帧模式支持 `Raw` 与 `Delimiter`，接收结果统一为 `CommReceiveResult`。
  - 接收等待队列上限来自 `AppConfig.CommMaxMessageQueueSize`，`CommunicationHub` 启动时仅从 `AppConfigStorage` 启动缓存读取，不在模块内重复读取配置文件。
- `FrmComunication` 交互规则：
  - TCP 与串口表均包含“分隔符”列，固定选项：`无`、`\n`、`\r\n`。
  - 映射规则：`无 -> FrameMode=Raw`；`\n/\r\n -> FrameMode=Delimiter + FrameDelimiter`。
  - 通道名、地址、端口、串口参数均做严格校验，错误直接提示并回退显示。
- 生命周期约束：
  - 窗体关闭采用隐藏策略（不销毁窗口）。
  - `Dispose` 前执行 `ReleaseRuntimeResources`：停止状态定时器、取消循环发送、解绑 `SF.comm.Log`。

## 11. PLC 系统
- 配置模型：
  - `PlcDevice` 描述设备连接参数，`PlcMapItem` 描述变量映射关系。
  - 支持协议：`S7`、`ModbusTcp`。
- 存储与校验：
  - `PlcConfigStore` 管理 `PlcDevice.json`、`PlcMap.json`。
  - 设备与映射均执行严格合法性校验（协议、IP、端口、方向、数量、变量名等）。
- 运行态：
  - `PlcHub` 统一连接、重连、读写；单次异常会断开会话并返回错误。
  - `FrmPlc` 的映射循环按方向执行读写，同步到 `valueStore`；任一失败会停止映射并报警。

## 12. 变量与数据结构
- 变量系统（`ValueConfigStore` + `FrmValue`）：
  - 容量固定 `0..999`，文件 `value.json`。
  - 变量键为名称，索引槽位固定，支持运行态值变更事件。
  - 编辑规则：名称必填；`Type` 空值按 `double` 处理；`double` 空值按 `0` 处理；`string` 允许空字符串。
  - 支持常用标记、复制粘贴、监控窗口、按名/按索引读写。
- 数据结构系统（`DataStructStore` + `FrmDataStruct`）：
  - 文件 `DataStruct.json`。
  - 字段名称/类型/值字典强一致校验，支持结构和项的增删改查复制计数。

## 13. 账户、权限与锁定模式
- 角色：
  - `SystemAdmin`、`Admin`、`Operator`。
- 默认系统管理员：
  - 用户名 `system`，口令 `software_123`。
  - 系统管理员账号不可新增、删除、禁用、改名，口令不可修改。
- 权限目录：
  - 权限键由 `PermissionCatalog` 定义，覆盖流程、卡/IO、工站、IO调试、通讯、变量、变量调试、AI、PLC、报警、程序设置、工具、账户管理。
  - 默认权限：`SystemAdmin=全量`；`Admin=全量-Account.Manage`；`Operator=Process.Access/Process.Run/Process.Search`。
- 账户配置安全：
  - `Account.json` 必须是 `ENC:` 前缀密文。
  - 加密方案：AES-CBC + HMAC-SHA256 + PBKDF2。
  - 根字段仅允许 `Accounts`；账户字段白名单固定。
- 锁定模式：
  - 账户配置异常或权限配置异常触发 `SF.SetSecurityLock`。
  - 锁定后仅允许默认系统管理员进行恢复登录（`IsRecovery=true`），恢复会话只保留 `Account.Manage`。
- 对外接口：
  - `SF.userContextStore`：`GetUserName/GetUserRole/GetSnapshot`。
  - `SF.userLoginStore`：`TryLogin/TryLogout/IsLoggedIn`。

## 14. 配置文件全量清单
- 流程与编辑：
  - `Config/Work/*.json`：按索引保存单流程（`0.json` 起连续）。
- 运行数据：
  - `Config/value.json`：变量表。
  - `Config/DataStruct.json`：数据结构集合。
  - `Config/AlarmInfo.json`：报警元数据（固定 1000 条）。
- 设备与工站：
  - `Config/card.json`：控制卡与轴。
  - `Config/IOMap.json`：命名 IO 映射。
  - `Config/DataStation.json`：工站与点位。
  - `Config/MarkPoints.json`：标记点数据。
  - `Config/card_*.ini`：运动卡下发参数。
- 通讯与 PLC：
  - `Config/SocketInfo.json`：TCP 配置。
  - `Config/SerialPortInfo.json`：串口配置。
  - `Config/PlcDevice.json`：PLC 设备配置。
  - `Config/PlcMap.json`：PLC 映射配置。
  - `Config/IODebugMap.json`：IO 调试映射。
- 账户与程序：
  - `Config/Account.json`：账户与权限密文配置。
  - `Config/AppConfig.json`：程序运行参数（当前键：`CommMaxMessageQueueSize`，默认 `1000`）。
  - `AppConfig.json` 在程序启动阶段由 `AppConfigStorage.TryLoad` 强制加载；缺失时自动创建默认文件，字段缺失/类型错误/非正数会终止启动。
  - `AppConfigStorage` 在 `TryLoad/TrySave` 成功后刷新进程内缓存；运行期读取可通过 `TryGetCached` 获取当前缓存值。
- 反序列化约束：
  - 所有通过 `FrmMain.ReadJson<T>` 或各 Store `Load` 读取的 JSON 使用 `TypeNameHandling.All + ObjectCreationHandling.Replace`。
  - 该策略要求配置文件字段结构与模型严格一致，不做宽松修复。

## 15. 运行期故障与排查入口
- 启动即退出（程序配置异常）：
  - 查 `Config/AppConfig.json` 是否存在 `CommMaxMessageQueueSize`，类型是否为整数且值大于 `0`。
- 启动即锁定：
  - 先查 `Config/Account.json` 是否为合法 `ENC:` 密文，字段是否符合白名单。
- 流程禁止运行：
  - 查 `SF.ProcConfigFaulted`，再查 `Config/Work` 是否从 `0.json` 连续、跳转地址是否跨流程或越界。
- 通讯初始化异常：
  - 查 `Config/AppConfig.json` 的 `CommMaxMessageQueueSize` 是否为正整数。
  - 查 `SocketInfo.json/SerialPortInfo.json` 的参数合法性与分隔符取值。
- PLC 映射中断：
  - 查 `PlcDevice.json` 连接参数、`PlcMap.json` 方向/地址/数量/变量名。
  - 查对应变量是否存在于 `value.json`。
- 系统状态变量异常：
  - 查变量 `复位状态` 与 `系统状态` 是否存在、类型是否 `double`、`复位状态` 值是否在 `0/1/2`。

## 16. 扩展开发入口
- 新增操作类型：
  - 在 `OperationType` 定义属性与编辑器行为。
  - 在 `ProcessEngine.ExecuteOperation` 增加分发分支并实现执行函数。
  - 对异常路径统一调用报警逻辑，禁止静默失败。
- 新增权限：
  - 在 `Security/PermissionCatalog.cs` 增加权限键与定义项，并同步默认角色模板与分组入口约束。
- 新增自定义函数：
  - 在 `CustomFunc.functionMap` 注册委托，流程通过 `CallCustomFunc` 调用。
- 新增配置项：
  - 明确配置文件、默认值、校验规则、错误处理策略，并同步更新本文件对应章节。

## 17. 依赖与驱动
- 主要 NuGet 依赖位于 `packages.config`，当前包含：`Newtonsoft.Json`、`S7netplus`、`System.Buffers`、`System.Memory`、`System.Numerics.Vectors`、`System.Runtime.CompilerServices.Unsafe`、`System.Runtime.InteropServices.RuntimeInformation`。
- 本地驱动与随程序分发文件统一放在项目根目录 `SourceDLL/`。
- 构建输出规则：`Automation.csproj` 在 `Build` 后自动将 `SourceDLL/` 下全部文件复制到 `$(TargetDir)`，并保留 `SourceDLL` 内部相对子目录结构。
- 运动控制底层：
  - `MotionControl/LTDMC.cs`：`LTDMC.dll` P/Invoke。
  - `MotionControl/LeiSei3000.cs`：雷赛卡运动与 IO 实现。
