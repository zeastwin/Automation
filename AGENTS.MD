## 语言约定
- 所有代码注释、技术文档、提交说明一律使用**简体中文**。
- 代码中的标识符、API 名称、JSON 字段保持英文；用户可见字符串按 UI 设计需求呈现。
- 面向开发者的说明、分析、总结全部用简体中文输出。

## 编码约定
- 避免过度包装：仅做透传/一行调用的 helper 不要新增，直接在调用处展开。

# Automation Agents 速览

项目概述：基于 Windows Forms 的低代码自动化平台，核心是可编辑的“流程/步骤/操作”图谱，协同运动控制卡、IO、TCP/串口通讯与自定义逻辑。入口 `Program.cs` → `FrmMain`，主窗体内嵌各功能子窗体。

- **配置布局**：`Config/Work/*.json` 存流程；`Config/value.json` 存变量；`Config/card.json`、`Config/IOMap.json`、`Config/DataStruct.json`、`Config/DataStation.json` 定义硬件/IO/数据结构；`Config/MarkPoints.json` 记录标记点。
- **全局入口**：`SF` 静态类持有所有窗体、运动控制器 `MotionCtrl`、运行器 `DataRun` 以及路径 `workPath`、`ConfigPath`，UI/运行期均从此处获取依赖。

## 核心概念
- **流程模型**：`Proc` → `Step` → `OperationType`。通过 `FrmProc`（树）、`FrmDataGrid`（操作列表）、`FrmPropertyGrid`（属性编辑）修改，序列化为 JSON（启用 `TypeNameHandling.All`）。
- **运行引擎**：`DataRun` 为每个流程开启线程，`ProcHandle` 记录索引、状态与 `ManualResetEvent`。`RunProc`/`RunStep` 支持暂停、单步、跳转、报警；`ExecuteOperation` 将具体操作派发到对应执行函数。
- **变量**：`FrmValue` 管理 0–999 的 `DicValue`，含类型/名称/备注，持久化到 `Config/value.json`，提供按名/按索引读写。
- **数据结构**：`FrmDataStruct` 定义结构体集合，操作可对项进行设/取/复制/插入/删除/查找/计数。

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
- 监控/搜索：`FrmState`、`FrmSearch`/`FrmSearch4Value`、`FrmInfo`、`FrmTest`。

## 扩展指引
- 新增操作：继承 `OperationType` 定义属性/转换器/报警行为，在 `DataRun.ExecuteOperation` 添加分支并实现执行函数。
- 自定义逻辑：向 `CustomFunc.functionMap` 注册委托，名称即出现在 `CallCustomFunc`。
- 硬件调整：使用 `FrmCard`/`FrmIO` UI 修改，`FrmToolBar` 保存后刷新缓存列表/字典。
- 运行问题：操作设置 `ProcHandle.isAlarm`/`isGoto`，按 `AlarmType` 选择跳转或弹框（`Message`）策略。

## 补充梳理（实现细节）
- **运行时目录**：`SF.ConfigPath`/`SF.workPath` 由 exe 路径拼出，调试时写入 `bin/Debug/Config`（Release 同理），流程按 `0.json`…`N.json` 编号加载。
- **窗体装配**：`FrmMain` 构造时创建所有子窗体并写入 `SF`，`loadFillForm` 以 `TopLevel=false` + `Dock=Fill` 嵌入面板。
- **启动顺序**：`FrmMain_Load` 依次刷新变量/IO/卡/数据结构/通讯/报警，并完成运动卡初始化、配置下载、使能伺服、设定当量。
- **页面切换**：`FrmMenu` 通过 `SF.curPage` 切换流程编辑/卡与 IO/工站等界面，动态重排面板与工具条按钮可见性。
- **流程调度**：`DataRun` 为每个流程创建 `Thread`，`ProcHandle` 的 `m_evtRun/m_evtTik/m_evtTok` 实现运行/暂停/单步。
- **报警处理**：执行异常标记 `isAlarm`，`Message` 弹窗根据 `AlarmType` 触发停止/忽略/跳转或多按钮选择。

## 配置文件补充
- `Config/Work/*.json` 按索引存单个流程，加载时以文件数量决定流程数量。
- `Config/value.json` 存变量字典，`FrmValue` 以 0–999 索引映射到表格。
- `Config/card.json`、`Config/IOMap.json`、`Config/DataStation.json` 分别存卡、IO 映射、工站数据。
- `Config/DataStruct.json` 存数据结构集合，项内包含 `str`/`num` 字典。
- `Config/AlarmInfo.json`、`Config/SocketInfo.json`、`Config/SerialPortInfo.json`、`Config/IODebugMap.json`、`Config/MarkPoints.json` 存报警/通讯/调试/标记点。

## 依赖与驱动
- `MotionControl/LTDMC.cs` P/Invoke `LTDMC.dll`，`MotionControl/LeiSei3000.cs` 实现 LS 运动与 IO。
- `packages.config` 使用 `Newtonsoft.Json`、`OxyPlot`、`S7.Net`、`System.Drawing.Common` 等依赖。
- 运行时会读取 `Config/card_0.ini` 等 ini 做运动卡参数下载。
