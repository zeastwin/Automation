---
name: automation-process-authoring
description: 在 Automation 低代码平台中创建、修改、重构、复制或评审 Proc→Step→OperationType 流程时使用。按需读取当前对象、资源和精确契约，使用 ChangeSet V2 预演、确认、提交，并按用户目标验证。纯源码开发、只读运行诊断和一般问答不触发本 Skill。
---

# Automation 流程编写

1. 只读取当前决策需要的对象和资源；目标已知时精确读取，未知时再搜索。复杂机械反馈、控制流、通讯重试、异常恢复或自定义函数边界按主题读取 `get_process_design_guide`。
2. 普通业务动作优先使用能准确表达目标的语义 `kind`；字段或行为不确定时读取对应的 `get_semantic_operation_schema`。需要保留原生字段或语义层不能无损表达时，读取精确 `get_native_operation_schemas` 并使用 `native.operation`。
3. 每次构造一个可独立审查和保存的完整 ChangeSet。现有对象使用稳定 ID，局部 `key` 只连接当前 ChangeSet 内的新对象；会改变控制流、安全结果或外部副作用的未知细节不得猜测。
4. 使用 `preview_change_set` 预演，依据返回的变化、警告、阻塞和状态等待前台确认；确认后仅以 `previewId` 调用 `apply_change_set`。修正预演时提交完整替代阶段，提交后改用返回的稳定 ID。
5. 按用户目标区分已提交、结构有效、可运行和已观测运行。结构使用 `validate_proc`，启动条件以 Readiness 为准；只有用户明确要求时才测试或持续运行。安全状态不确定时保持受影响流程停止。

字段结构以 MCP Schema 为准，运行语义以 behavior/Guide 为准，当前配置和状态以 Bridge、Store 与 Readiness 返回为准；不要在本 Skill 中补写这些契约。
