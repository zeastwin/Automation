---
name: automation-process-review
description: 在 Automation 低代码平台中只读检查、审查、比较或解释 Proc→Step→OperationType 流程时使用。允许按目标主动扩展到相关流程、引用、变量、资源和运行证据，同时保持覆盖范围、原始 finding、事实与推断边界。创建或修改流程改用 automation-process-authoring。
---

# Automation 流程评审

1. 先确定用户要求的范围和证据层级：单个流程、多个指定流程、全量配置，或带现场症状的运行诊断。编辑器选择只帮助定位，不扩大用户要求，也不代表修改授权。
2. 已知目标时精确读取；全局评审、未知根因、依赖分析或同类流程比较可以主动扩展范围。每次扩展用于验证明确风险或假设，不因“可能有用”反复读取相同事实。
3. 跨流程初筛使用 `audit_proc_batch`，并保留每条原始 finding。只要返回 `nextFindingOffset`，就按相同流程批次和 `indexRevision` 继续读取；没有读完所需分页时不得宣称完整覆盖。汇总计数只用于导航，不代替原始事实。
4. 单流程静态结构和启动条件使用 `validate_proc`，配合 `get_proc_overview` 定位；不要再读取完整流程或用多个工具重复验证同一层事实。当前运行状态、报警或断点属于 `RuntimeDiagnostic`，用户提供明确现场症状时切换后使用 `diagnose_issue`，不得在静态评审中猜测现场结论。
5. 已定位到步骤或指令后，优先读取 `get_operation_context`、`get_op_details` 或引用关系；判断跨步骤控制流时使用 `get_flow_graph`，不再读取完整流程。需要判断字段合法性或运行行为时，再读取对应 Schema 或 behavior/Guide。指令字段事实可按 `opId::operation.field.<字段名>` 引用，类型和位置使用对应 `operation.*` 事实。
6. `step.disabled` 和 `operation.disabled` 是必须保留的配置事实，不得隐藏、折叠掉或自动判定为 Bug。结合 Readiness、引用关系、运行证据或已验证业务目标说明其实际影响；名称和相邻位置只能支持推断。
7. 缺少计数器复位不能单独证明幂等缺陷。幂等性只判断“同一业务事件或重试是否会重复产生不可逆副作用”；正常累计、状态推进或一次性计数不是缺陷证据。
8. 恢复能力必须由可执行路径、状态迁移、调用关系或运行证据证明。`autoStart`、流程名称、孤立的调用者数量，以及“看起来应该恢复”都不能单独证明存在或缺少恢复路径。
9. 占位指令及其 `Note` 只表达待确认设计，不是实际设备动作、跳转目标或修复授权。评审可以报告配置缺口，但不得从占位文本推导具体写入。
10. 控制流节点可达不等于业务条件能够成立；分别说明结构可达性、条件生产者、资源就绪和已观测运行。最终解释必须服从宿主机械事实，不把“没有找到生产者”写成“节点不可达”。
11. 当证据足以回答当前范围时停止调查并形成结果。结束时提交结构化 `reviewHandoff`：`proven_defect` 只包含有稳定目标、直接证据、宿主事实引用和最小改动的 findings；设计或资源尚缺用 `configuration_gap`，证据不足用 `unresolved`，没有证明缺陷用 `no_defect`。只有 `proven_defect` findings 能作为后续修改依据。
12. 只读评审不调用写入或运行控制工具。用户要求修复时，先提交可信评审交接，再加载 `automation-process-authoring` 进入 ChangeSet V2；只有用户明确要求测试或运行时才获取相应授权。

字段结构以 MCP Schema 为准，运行语义以 behavior/Guide 为准，当前配置和状态以 Bridge、Store、Readiness 与运行证据为准；本 Skill 只维护评审方法，不复制动态契约。
