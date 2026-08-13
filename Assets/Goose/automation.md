# Automation 平台路由

## 任务入口

- 只读检查、审查、比较或解释 `Proc → Step → OperationType` 流程时，先加载 `automation-process-review` Skill；用户随后要求修改时再切换到 `automation-process-authoring`。
- 创建、修改、重构或复制流程时，先加载 `automation-process-authoring` Skill，并遵守 ChangeSet V2 的预演、确认和提交链。
- 创建或重构复杂流程时，在明确行为目标后按目标调用 `get_process_design_guide`，组合生命周期、调度、互锁、执行器、运动、取放、搬运、识别、外部事务、持续监控、恢复等功能块；`core` 由服务端自动附带。功能块包含从历史项目甄别出的重要写法，但旧名称、地址、参数、跳转、报警模式和禁用配置不得作为当前事实或直接复制。
- 有明确运行症状时使用 `diagnose_issue` 获取运行现场证据；不要用静态配置检查替代实际运行证据。
- Automation 源码开发按目标调用 `get_platform_development_context`：HMI 使用 `hmi`，平台 API 使用 `platform-api`，自定义函数使用 `custom-function`；目标不明确时读取 `catalog`。

## 调查与证据

- 目标已知时优先使用名称或稳定 ID 精确读取；全局评审、未知根因、依赖分析或同类流程比较可以主动扩展范围，但每次扩展应服务于明确判断，避免多个工具重复返回同一事实。
- `audit_proc_batch` 用于跨流程配置事实和覆盖索引；`validate_proc` 用于单流程结构与 Readiness；`diagnose_proc` 用于单流程运行状态和风险清单；定位到具体指令后优先使用局部上下文，只有理解完整控制流确有必要时才读取流程全文。
- `step.disabled`、`operation.disabled` 是必须保留的配置事实，不得隐藏或丢弃，但禁用本身不自动等于 Bug。只有结合 Readiness、引用关系、运行证据或已验证业务目标，才能判断其影响。
- 返回含 `hasMore`、`nextOffset`、`nextFindingOffset` 或其他继续标记时，继续读取所需范围；未完成分页不得声称已经覆盖全部问题。汇总计数只帮助导航，不能替代原始 finding。
- 配置事实、基于事实的推断和证据缺口分别陈述。变量当前值、指令名称和相邻上下文可以形成待验证假设，不能单独证明用户意图。
- 已有权威结果足以支持当前结论时停止调查并交付；大范围任务按稳定阶段给出结果，不为可能有用但与当前判断无关的信息持续扩张。

字段结构以 MCP Schema 为准，运行语义以 behavior/Guide 为准，当前配置与资源以 Bridge/Store 返回为准，启动条件以 Readiness 为准；`get_process_design_guide` 是唯一的按需流程设计知识入口，功能块必须适配当前事实。不要在常驻上下文中复制这些动态契约。
