# Automation 平台路由

## 任务入口

- 用户只要求“设计、方案、结构或怎么写”而没有明确要求落入当前配置时，这是方案任务：按目标调用一次 `get_process_design_guide`，通常只选一个主主题，基于明确假设直接给出方案；不要加载写入 Skill、枚举当前项目资源或发起预演。
- 只读检查、审查、比较或解释现有 `Proc → Step → OperationType` 时，加载 `automation-process-review` Skill。
- 用户明确要求创建、修改、重构、复制或写入当前配置时，加载 `automation-process-authoring` Skill，并走 ChangeSet V2。
- 有明确运行症状时使用 `diagnose_issue` 获取运行现场证据；不要用静态配置检查替代实际运行证据。
- Automation 源码开发按目标调用 `get_platform_development_context`：HMI 使用 `hmi`，平台 API 使用 `platform-api`，自定义函数使用 `custom-function`；目标不明确时读取 `catalog`。

## 事实边界

- 只有答案或写入确实依赖当前对象时才读取资源；目标已知就按名称或稳定 ID 精确读取，不先做全平台盘点。
- `step.disabled`、`operation.disabled` 必须如实保留，但禁用本身不自动等于 Bug；覆盖性检查遵循评审 Skill 的分页与原始 finding 规则。
- 配置事实、推断和未知信息分别陈述；方案中的假设不是当前项目事实。

字段结构以 MCP Schema 为准，运行语义以 behavior/Guide 为准，当前配置与资源以 Bridge/Store 返回为准，启动条件以 Readiness 为准；`get_process_design_guide` 是唯一的按需流程设计知识入口，功能块必须适配当前事实。不要在常驻上下文中复制这些动态契约。
