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
- `Diagnostic/Editor` 只作为用户权限外壳；每次普通 AI 请求先把原生 Goose 会话切到只开放 `TaskCoordinator.request_capability` 的工具面。所有工作 Profile 都保留同一控制面；每轮第一条成功决定立即锁定，后续申请忽略。模型根据本阶段新事实申请下一能力、结束或询问用户，不预生成整单阶段计划，不使用关键词路由或规划失败兜底，也不把多个 Profile 合并成大工具集。
- 能力申请只表达模型意图，不授予权限。代码校验 Profile、权限外壳、完全权限、阶段预算和合法迁移；普通流程与资源写入依赖预演确认，不由逐字授权引文硬拦截。运行控制、平台配置和源码修改仍必须逐字引用当前用户消息中的授权片段，历史、附件和模型输出不能单独扩大授权。申请缺失或连续不合法时停止且不执行。审查进入副作用能力前必须取得当前状态读取证据；`ProcessDesign` 完成前必须取得设计知识读取证据；运行前必须验证最近流程 ChangeSet、平台 migration 或资源 `configurationSaved`。只有失败结果不能证明无副作用时才标记不安全部分写入；无副作用预演失败后成功提交视为已恢复。
- 正常能力切换通过 Goose 会话扩展接口原地替换 Automation MCP 与内置扩展，必须保持同一个 `sessionId`，并在切换后核对实际扩展与 `request_capability` 工具。用户会话历史只持久化最终输出；正常新请求依赖原生会话连续性，不重复注入持久历史。只有原生会话丢失或状态不可信时才新建会话并注入有限可信恢复上下文，分析草稿、工具过程和重复总结不得进入持久历史。
- 源码只读定位优先使用受根目录、扩展名和结果数约束的 `search_platform_source`，配合 `read/tree` 不触发重建；direct `write/edit` 视为已修改，`SourceDevelopment` 中执行 Shell 视为修改状态不确定，两者都必须重新构建并加载新版本后再续做。复合任务部分完成后保留最后有效输出和副作用事实，停止或失败时不得自动重放整单请求。
- Schema 描述结构，`OperationBehaviorCatalog` 描述运行语义，Readiness 描述启动阻塞；不要用提示词掩盖代码契约问题。
- `saveRequired` 与 `runRequired` 保持分层；提交后使用稳定 `procId/stepId/opId`，预演局部 `key` 不跨提交阶段。
- 只读评审进入修改必须携带结构化 `reviewHandoff`；finding 必须逐项引用宿主从成功工具结果机械提取的事实，且只有 `repairability=safe_without_external_facts` 的已证明 finding 能直接作为评审修复依据。设计缺口、仍需用户选择、未决项和未证明缺陷不得被后续阶段升级为可直接修复的事实。
- 占位指令确认后只用 `operation.replace` 完整替换，不对占位 `Note` 做局部更新；清空字符串使用 `variable.clear`，不把 `variable.set value=""` 同时解释成缺参和清空。
- 新建单个完整流程直接进入 `ProcessCreate`，由该能力读取设计知识、发现必要资源并使用 Process Blueprint；不要先绕行纯方案能力。Blueprint 在 MCP 内确定性编译为一个 ChangeSet V2：同一步骤用 `operationKey`，跨步骤默认只用 `stepKey` 进入首指令，跳入中段必须显式使用 `entryMode=operation`。新建流程不得包含从入口不可达的有效指令，回环累加状态必须在本轮执行路径上先复位。既有流程修改继续使用原子 ChangeSet，不增加第二条 Bridge 写入链。
- 修改原生/语义指令、Schema、资源类型、ChangeSet 或 AI 前台时，同步检查模型、编译、行为、校验、运行和测试；只读取该任务需要的 `05-AI与Bridge.md` 章节。

## Prompt、Skill 与可信输出

- 运行时规则按职责放置：System Prompt 只保存跨任务稳定原则，`automation.md` 保存平台路由和证据边界，Skill 保存任务工作流，结构、行为、状态和合法迁移由代码契约提供；不要在多个运行时提示层复制同一规则。
- Prompt 是正式能力契约的一部分。对需要模型进行语义判断、歧义处理、风险取舍或事实措辞的问题，允许加入1～3句简短且可泛化的规则；一次可靠测试若暴露一类稳定问题即可作为依据，不必等待重复失败。能够由 Schema、状态机、权限或数据完整性契约确定性保证的约束仍下沉代码，安全边界不得只依赖 Prompt；不为单个措辞追加案例补丁。
- 纯方案设计不进入写入 Skill或枚举当前资源；只读流程审查与流程写入使用独立 Skill，只读请求不推导写入或运行控制授权。占位写入使用 `config.placeholder` 保持 `incomplete`，不得用延时、弹框或猜测资源伪造成可运行。领域方法放在 `Assets/Goose/automation.md` 与对应 Skill；能力申请和合法迁移只在代码契约维护。
- AI 契约必须保留原始事实并区分事实、推断和证据缺口；汇总不得替代明细，分页不得丢项或混合不同修订。精确字段、数量边界和错误码只在 Schema、Bridge/MCP 与 `05-AI与Bridge.md` 维护。
- 修改受管 Prompt、平台上下文或 Skill 时，同步更新资源版本、嵌入/输出复制、启动部署、内容校验、运行分析和契约测试；同版本内容漂移恢复内置内容，高于内置版本的本机文件按当前部署策略保留并报告。
- 架构文档只描述当前程序；一次性交接、阶段计划和工作区快照完成接管后删除，不让历史状态与当前契约长期并存。

## 验证与日志

- 先运行与改动直接相关的测试；跨模块或共享契约改动再运行 `dotnet test Automation.sln -c Debug -p:Platform=x64`。没有发布配置特有风险时不重复跑 Release。
- 架构回归只保护真实依赖方向、退役入口和安全边界，不设置文件行数等机械指标。
- 配置、状态机和安全改动应验证失败无副作用、必要回滚和重复调用结果；仅编译通过不算完成。
- 运行日志统一写入 `D:\AutomationLogs\` 的既有分类目录，不写程序执行目录。
