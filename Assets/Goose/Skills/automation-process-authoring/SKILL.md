---
name: automation-process-authoring
description: 在 Automation 低代码平台中创建、修改、重构或复制 Proc→Step→OperationType 流程时使用。按需读取实现目标必需的当前事实，通过 Process Blueprint 或 ChangeSet V2 预演、确认、提交并验证。只读流程评审使用 automation-process-review；仅给设计方案、源码开发、只读运行诊断和一般问答不触发本 Skill。
---

# Automation 流程编写

1. 先确定本次要写入的可观察行为、失败出口和外部副作用。创建或重构复杂流程时按目标调用 `get_process_design_guide`，通常只选一个主主题；历史功能块只指导结构，不提供当前资源名或参数。
2. 只读取写入所依赖的当前对象。目标已知时按名称或稳定 ID 精确读取；资源名称未知时使用带关键词的搜索或过滤，不为“全面了解环境”枚举无关对象。
3. 用户要求占位、骨架或先写结构时保留 Step 结构，并在未知动作处使用 `config.placeholder`，使结果可保存但保持 `incomplete`。不得用固定延时、普通弹框、常量或伪状态把缺失行为包装成 `ready/runnable`。
4. 已确定的业务动作使用准确的语义 `kind`；字段或行为确有疑问时才读取对应 `get_semantic_operation_schema`。仅在语义层不能无损表达时读取精确 `get_native_operation_schemas`。本阶段所需变量放在同一 ChangeSet 的 `variables`，不先用独立变量工具旁路创建。
5. 新建一个完整流程时，把流程、变量、步骤和指令组织成单个 `preview_process_blueprint`；平台会将其确定性编译为一个 ChangeSet V2 原子阶段，不要手工展开 create/append 动作。修改、重构或复制既有对象时，使用稳定 ID 构造一个完整 `preview_change_set` 阶段。
6. 预演后根据变化、警告、阻塞和允许迁移等待前台确认，再仅以 `previewId` 调用 `apply_change_set`。修正活动预演时使用 `replacePreviewId` 完整替代旧阶段；提交后开始新阶段并改用返回的稳定 ID。
7. 使用 `validate_proc` 核对最终结构与 Readiness。只有用户另行明确要求 `run_proc_test` 或运行时才进入运行控制任务；报告时区分已提交、结构有效、可运行和已观测运行。

字段结构以 MCP Schema 为准，运行语义以 behavior/Guide 为准，当前配置和状态以 Bridge、Store 与 Readiness 返回为准；不要在本 Skill 中补写这些契约。
