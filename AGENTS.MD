# Automation 仓库协作约定

## 基本原则

- 代码注释、技术文档和提交说明使用简体中文；标识符、API、JSON 字段保持英文。
- 平台开发只维护一条当前契约。修改模型、Schema、运行时、Bridge/MCP、编辑器时同步更新整条链路，并删除被替代的旧入口、字段、分支和说明。
- 只为真实职责、状态或不变量建立抽象。单页面稳定 UI 逻辑可以留在页面，不按文件行数或目录对称强行拆分。
- 保留用户已有的无关改动；只修改当前任务需要的范围。

## 获取上下文

- 只读取与任务直接相关的源码和文档；已知精确边界时直接读源码，不批量加载 Markdown。
- 架构入口按范围选择：启动 `01`、配置 `02`、流程/Engine `03`、编辑器 `04`、AI/Bridge `05`、运动安全 `06`；目录职责不清楚时再读对应 README。

## 核心架构边界

- 单个平台实例由 `AutomationPlatformHost` 持有 `PlatformRuntime`；Store、Engine、设备、安全和编辑会话通过实例显式传递，不恢复静态全局容器或静态窗体引用。
- HMI 只通过 `IAutomationPlatform` 使用平台能力，不直接引用平台运行时、窗体、Store、Engine 或原生运动对象。
- 窗体负责布局、确认、事件转发和渲染；跨页面状态、配置事务、设备协议、运行闸门和复用规则进入现有 Workspace、Store 或应用服务。
- 流程配置内存、`Work/` 事务和运行控制分别由 `ProcessDefinitionRepository`、`ProcessWorkDirectoryTransaction`、`IProcessRuntimeControl` 负责。手工编辑和 AI 复用现有流程/变量配置服务。
- `Automation.Runtime.Contracts` 保持无 UI、无实现依赖；没有实际复用或依赖问题时不新增程序集和装饰接口。
- 配置路径统一来自 `PlatformRuntime.Paths`，运行时释放统一进入 `PlatformShutdownCoordinator`。

## 运行、配置与安全

- 流程未结束时重复启动应明确失败并保留当前实例；涉及设备或人员风险的不确定状态优先停机、报警。
- 保存只检查结构与 `saveRequired`；`ProcessReadinessService` 和启动闸门检查 `runRequired`。缺少运行资源可保存为 `incomplete`，但不能启动。
- 缺失配置按当前契约生成并持久化默认值；已有值格式错误、越界或违反约束时严格失败，不静默猜测或修复。
- 非致命服务异常应降级完成平台/HMI 初始化；流程配置故障必须停止流程、设置运行闸门并禁止启动。
- 辅助诊断开关不得改变流程状态、安全停止、基础报警或失败日志。

## AI、MCP 与 Bridge

- 流程结构写入只走 ChangeSet V2：`preview_change_set` → 用户确认 → `apply_change_set(previewId)`；不得恢复旧 intent/patch/create_batch 或旁路写入。
- `McpServer/McpToolProfile.cs` 是工具集合权威来源；公共参数使用 `Automation.Protocol` 强类型 DTO 和生成 Schema。
- Schema 描述结构，`OperationBehaviorCatalog` 描述运行语义，Readiness 描述启动阻塞；不要用提示词掩盖代码契约问题。
- `saveRequired` 与 `runRequired` 保持分层；提交后使用稳定 `procId/stepId/opId`，预演局部 `key` 不跨提交阶段。
- 修改原生/语义指令、Schema、资源类型、ChangeSet 或 AI 前台时，同步检查模型、编译、行为、校验、运行和测试；只读取该任务需要的 `05-AI与Bridge.md` 章节。

## 验证与日志

- 先运行与改动直接相关的测试；跨模块或共享契约改动再运行 `dotnet test Automation.sln -c Debug -p:Platform=x64`。没有发布配置特有风险时不重复跑 Release。
- 架构回归只保护真实依赖方向、退役入口和安全边界，不设置文件行数等机械指标。
- 配置、状态机和安全改动应验证失败无副作用、必要回滚和重复调用结果；仅编译通过不算完成。
- 运行日志统一写入 `D:\AutomationLogs\` 的既有分类目录，不写程序执行目录。
