---
name: automation-process-review
description: 在 Automation 低代码平台中只读检查、审查、比较或解释 Proc→Step→OperationType 流程时使用。允许按目标主动扩展到相关流程、引用、变量、资源和运行证据，同时保持覆盖范围、原始 finding、事实与推断边界。创建或修改流程改用 automation-process-authoring。
---

# Automation 流程评审

1. 先确定用户要求的范围和证据层级：单个流程、多个指定流程、全量配置，或带现场症状的运行诊断。编辑器选择只帮助定位，不扩大用户要求，也不代表修改授权。
2. 已知目标时精确读取；全局评审、未知根因、依赖分析或同类流程比较可以主动扩展范围。每次扩展用于验证明确风险或假设，不因“可能有用”反复读取相同事实。
3. 跨流程初筛使用 `audit_proc_batch`，并保留每条原始 finding。只要返回 `nextFindingOffset`，就按相同流程批次和 `indexRevision` 继续读取；没有读完所需分页时不得宣称完整覆盖。汇总计数只用于导航，不代替原始事实。
4. 单流程的结构和启动条件使用 `validate_proc`。只有问题涉及当前运行状态、报警或断点时才使用 `diagnose_proc`；用户提供明确现场症状时优先使用 `diagnose_issue`。不要用多个工具重复验证同一层事实。
5. 已定位到步骤或指令后，优先读取 `get_operation_context`、精确指令详情或引用关系；只有判断跨步骤控制流确实需要时才读取流程全文。需要判断字段合法性或运行行为时，再读取对应 Schema 或 behavior/Guide。
6. `step.disabled` 和 `operation.disabled` 是必须保留的配置事实，不得隐藏、折叠掉或自动判定为 Bug。结合 Readiness、引用关系、运行证据或已验证业务目标说明其实际影响；名称和相邻位置只能支持推断。
7. 当证据足以回答当前范围时停止调查并形成结果。大范围工作按稳定阶段交付，明确已检查范围、已验证问题、风险或推断、证据缺口以及尚未覆盖的范围。
8. 只读评审不调用写入或运行控制工具。用户要求修复时，先报告当前证据，再加载 `automation-process-authoring` 进入 ChangeSet V2；只有用户明确要求测试或运行时才获取相应授权。

字段结构以 MCP Schema 为准，运行语义以 behavior/Guide 为准，当前配置和状态以 Bridge、Store、Readiness 与运行证据为准；本 Skill 只维护评审方法，不复制动态契约。
