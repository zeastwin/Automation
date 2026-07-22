# 平台编辑器源码导航

`Editor/` 只承载 WinForms 编辑器、页面局部状态和 UI 交互适配。平台运行时、配置事实源、流程执行与 Bridge 协议分别保留在 `Runtime/`、`Stores/`、`Engine/` 和 `Bridge/`。

| 目录 | 主要入口 | 职责 | 状态或提交边界 |
| --- | --- | --- | --- |
| `Shell/` | `FrmMain`、`EditorWorkspace` | 页面装配、菜单、工具栏、导航、初始化和关闭 | 平台实例由 `PlatformRuntime` 持有；关闭进入 `PlatformShutdownCoordinator` |
| `Process/` | `FrmProc`、`FrmDataGrid`、`FrmInspector` | 流程树、指令表、对象检查、搜索和定位 | 选择由 `ProcessEditorSelectionState` 持有；结构修改进入 Engine 编辑服务 |
| `Process/Inspector/` | `InspectorView`、字段控件 | 指令属性显示、编辑、选择和值转换 | 编辑草稿由 `EditorSessionCoordinator` 管理 |
| `Variables/` | `FrmValue`、`FrmValueDebug` | 变量配置与运行值调试 | 配置规则和提交进入 `VariableEditorService` |
| `Io/` | `FrmIO`、`FrmIODebug` | IO 配置、监视、调试布局和关联 | 设备读取进入 `IoDebugMonitorService`；配置提交进入 `IoDebugConfigurationEditorService` |
| `Motion/` | `FrmCard`、`FrmStation`、`FrmControl` | 控制卡、工站和手动运动 | 运动命令进入 `ManualMotionService` 与安全门禁 |
| `Communication/` | `FrmCommunication`、`FrmPlc` | 串口、Socket 与 PLC 配置调试 | 配置由对应 Store 持有，运行连接由通讯和 PLC Runtime 持有 |
| `Data/` | `FrmDataStruct`、`FrmAlarmConfig` | 数据结构与报警配置 | 正式配置由对应 Store 持有 |
| `Diagnostics/` | `FrmInfo`、`FrmRuntimeDiagnostics` | 日志、状态、流程图、断点、性能和事故诊断 | 只投影运行事实，不改变流程状态 |
| `Ai/` | `FrmAiAssistant`、`GooseAcpClient` | AI 对话、ACP、预演确认和渲染 | 会话由 `AiConversationCoordinator` 持有；写入走 ChangeSet V2 |
| `Common/` | `UiPalette`、通用弹窗与交互适配 | 编辑器共享视觉和 WinForms 基础设施 | 不持有业务配置 |

## 放置规则

- 窗体源码、Designer 和 `.resx` 放在同一功能目录，并保持项目文件中的 `DependentUpon`。
- 只服务单一页面的稳定 UI 逻辑与页面共置；跨页面业务规则进入对应应用服务。
- 新增编辑器文件先选择上述功能目录，仓库根目录不再放置 `Frm*.cs`。
- 文件移动不改变 namespace；程序集边界与 namespace 调整属于独立任务。
