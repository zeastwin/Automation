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
- `Diagnostic/Editor` 只作为用户权限外壳；普通 AI 任务先由 `AiTaskCapabilityRouter` 路由到固定的最小任务能力包，各并发任务不得通过全局 Profile 热切换共享工具面。
- Schema 描述结构，`OperationBehaviorCatalog` 描述运行语义，Readiness 描述启动阻塞；不要用提示词掩盖代码契约问题。
- `saveRequired` 与 `runRequired` 保持分层；提交后使用稳定 `procId/stepId/opId`，预演局部 `key` 不跨提交阶段。
- 新建单个完整流程优先使用 Process Blueprint，并在 MCP 内确定性编译为一个 ChangeSet V2；既有流程修改继续使用原子 ChangeSet，不增加第二条 Bridge 写入链。
- 修改原生/语义指令、Schema、资源类型、ChangeSet 或 AI 前台时，同步检查模型、编译、行为、校验、运行和测试；只读取该任务需要的 `05-AI与Bridge.md` 章节。

## Prompt、Skill 与可信输出

- 运行时规则按职责放置：System Prompt 只保存跨任务稳定原则，`automation.md` 保存平台路由和证据边界，Skill 保存任务工作流，结构、行为、状态和合法迁移由代码契约提供；不要在多个运行时提示层复制同一规则。
- 只有能覆盖一类真实问题、经过复现或保护明确不变量的规则才进入 Prompt；不为一次随手测试、单个措辞或孤立失败增加特定提示。先判断问题属于模型行为、上下文路由、工具契约、数据质量还是运行实现。
- 纯方案设计不进入写入 Skill或枚举当前资源；只读流程审查与流程写入使用独立 Skill，只读请求不推导写入或运行控制授权。占位写入使用 `config.placeholder` 保持 `incomplete`，不得用延时、弹框或猜测资源伪造成可运行。具体路由和任务方法只在 `Assets/Goose/automation.md` 与对应 Skill 维护。
- AI 契约必须保留原始事实并区分事实、推断和证据缺口；汇总不得替代明细，分页不得丢项或混合不同修订。精确字段、数量边界和错误码只在 Schema、Bridge/MCP 与 `05-AI与Bridge.md` 维护。
- 修改受管 Prompt、平台上下文或 Skill 时，同步更新资源版本、嵌入/输出复制、启动部署、内容校验、运行分析和契约测试；同版本内容漂移恢复内置内容，高于内置版本的本机文件按当前部署策略保留并报告。
- 架构文档只描述当前程序；一次性交接、阶段计划和工作区快照完成接管后删除，不让历史状态与当前契约长期并存。

## 验证与日志

- 先运行与改动直接相关的测试；跨模块或共享契约改动再运行 `dotnet test Automation.sln -c Debug -p:Platform=x64`。没有发布配置特有风险时不重复跑 Release。
- 架构回归只保护真实依赖方向、退役入口和安全边界，不设置文件行数等机械指标。
- 配置、状态机和安全改动应验证失败无副作用、必要回滚和重复调用结果；仅编译通过不算完成。
- 运行日志统一写入 `D:\AutomationLogs\` 的既有分类目录，不写程序执行目录。
