---
name: automation-process-review
description: 在 Automation 低代码平台中只读检查、审查、比较或解释 Proc→Step→OperationType 流程时使用。允许按目标主动扩展到相关流程、引用、变量、资源和运行证据，同时保持覆盖范围、原始 finding、事实与推断边界。创建或修改流程改用 automation-process-authoring。
---

# Automation 流程评审

1. 确定用户要求的范围和证据层级。编辑器选择只帮助定位，不扩大范围，也不代表修改或运行授权。
2. 单流程先调用一次 `inspect_process` 取得紧凑结构摘要、Readiness、确定性流程图和流程引用。修订未变化且覆盖结论所需字段时优先复用已返回事实；若字段被省略、状态可能已变化或新缺口会影响结论，则精确回读，必要时请求 `includeOperationDetails=true`。
3. 跨流程初筛使用 `audit_proc_batch` 并保留原始 findings；按同一 `indexRevision` 读完当前所需分页后才声明覆盖范围。汇总计数只用于导航。
4. 已定位缺口时再使用 `get_operation_context`、`get_op_details`、引用或资源工具；只有判断字段合法性或运行行为确有需要时才读取对应 Schema/Guide。当前报警、断点和现场状态转入 `RuntimeDiagnostic`，静态评审不猜现场结论。
5. 保留禁用项、占位、原始数量和分页事实。结构可达不等于业务条件成立；名称、相邻位置和“没有找到”只能支持推断。幂等缺陷必须证明同一业务事件会重复不可逆副作用；恢复能力必须由可执行路径、状态迁移、调用关系或运行证据证明。
6. `config.placeholder` 只证明对应动作或结果仍未决；占位 `message` 中写了重试、超时、清理或出口，不等于这些结构已经实现。缺少外部资源或真实结果条件属于 `configuration_gap`；用户已明确、无需外部事实且机械流程图中缺失的控制结构，可作为结构缺陷证明。
7. 当现有证据已覆盖用户要求的范围，且剩余缺口不再影响结论时直接给出评审答复；若缺口可能改变结论，则继续按缺口读取或明确标为未决。只有需要把 finding 交给后续修复时才调用 `submit_review_handoff`：已证明且可安全修复的缺陷用 `proven_defect`，同时说明明确要求与机械结构差异并引用宿主事实。没有提交时宿主保存不授权修复的 `unresolved`，不要求为了包装普通评审再调用工具。
8. 只读评审不调用写入或运行控制工具。用户要求修复时先完成可信交接，再进入 authoring/ChangeSet V2；运行测试仍需用户明确授权。

字段结构服从 MCP Schema，运行语义服从 behavior/Guide，当前事实服从 Bridge、Store、Readiness 与运行证据；本 Skill 不复制动态契约。
