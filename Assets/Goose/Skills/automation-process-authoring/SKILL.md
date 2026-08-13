---
name: automation-process-authoring
description: 在 Automation 低代码平台中创建、修改、重构或复制 Proc→Step→OperationType 流程时使用。主动获取实现目标所需的对象、依赖、资源和精确契约，使用 ChangeSet V2 预演、确认、提交，并按用户目标验证。只读流程评审使用 automation-process-review；源码开发、只读运行诊断和一般问答不触发本 Skill。
---

# Automation 流程编写

1. 把用户目标整理为本阶段可观察行为、正常完成、分支与失败出口、外部副作用和所需验证级别。对未知根因、跨流程依赖或复杂控制流可以主动扩展调查；说明扩展要验证的判断，避免重复读取同一事实。
2. 目标已知时使用稳定 ID 精确读取。创建或重构复杂流程时，按目标调用 `get_process_design_guide`，只选择所需的生命周期、调度、互锁、执行器、运动、取放、搬运、识别、外部事务、持续监控、恢复、自定义函数或审查功能块；通用 `core` 由服务端自动附带。只有方案依赖某类当前资源时才查询该资源。
3. 内部记录“采用的功能块与职责/阶段、因当前资源或契约改变的部分、仍缺的证据”。功能块中的历史经验只用于指导写法，不复制旧名称、地址、参数、跳转、报警模式或禁用配置；与当前事实冲突时以当前 Schema、Behavior/Guide、资源和 Readiness 为准。
4. 普通业务动作优先使用能准确表达目标的语义 `kind`；字段或行为会影响结果时读取对应的 `get_semantic_operation_schema`。需要保留原生字段或语义层不能无损表达时，读取精确 `get_native_operation_schemas` 并使用 `native.operation`。
5. 每次构造一个可独立审查和保存的完整 ChangeSet。现有对象使用稳定 ID，局部 `key` 只连接当前 ChangeSet 内的新对象；会改变控制流、安全结果或外部副作用的未知细节不得猜测。
6. 使用 `preview_change_set` 预演，依据变化、警告、阻塞和允许迁移等待前台确认；确认后仅以 `previewId` 调用 `apply_change_set`。修正预演时提交完整替代阶段，提交后改用返回的稳定 ID。
7. 按用户目标区分已提交、结构有效、可运行和已观测运行。结构与 Readiness 使用 `validate_proc`；只有用户明确要求时才调用 `run_proc_test` 或持续运行工具。安全状态不确定时保持受影响流程停止。
8. 已取得实现和验证当前阶段所需的权威事实后停止扩展。输出区分已验证结果、推断和仍待用户决定的业务细节，不把预演或保存成功提升为实际运行成功。

字段结构以 MCP Schema 为准，运行语义以 behavior/Guide 为准，当前配置和状态以 Bridge、Store 与 Readiness 返回为准；不要在本 Skill 中补写这些契约。
