---
name: automation-process-authoring
description: 在 Automation 低代码平台中创建、修改、重构或复制 Proc→Step→OperationType 流程时使用。按需读取实现目标必需的当前事实，通过 Process Blueprint 或 ChangeSet V2 预演、确认、提交并验证。只读流程评审使用 automation-process-review；仅给设计方案、源码开发、只读运行诊断和一般问答不触发本 Skill。
---

# Automation 流程编写

1. 先明确当前功能块的可观察目标、完成证据、外部副作用和失败出口。创建或重构复杂流程时按目标调用一次 `get_process_design_guide`，通常只选一个主主题；只提取会改变当前实现的知识，不生成固定格式的长篇设计卡。
2. 使用 `resolve_authoring_inputs` 一次提交本功能块所需的绑定意图，每个 requirement 只代表一个变量、IO、工站、点位、通讯或报警用途。使用 `resolve_operation_capability` 解析业务动作可由哪个语义 kind 或已注册原生类型表达。聚合结果已经给出类型、作用域和兼容性时不再逐项读取；只有仍有歧义或字段确有疑问时，才调用返回中推荐的精确契约工具。候选不等于绑定事实，`bindingAllowed=false` 时选择占位、声明新变量或询问用户，不凭相似名称猜测。
3. 已知动作使用准确的语义 `kind`；正式报警使用 `alarm.raise`，普通提示才使用 `popup.message`。未知动作使用 `config.placeholder`，保留真实 Step 结构并使结果保持 `incomplete`；事实补齐后用 `operation.replace` 完整替换。不得用延时、弹框、常量或伪状态伪造可运行结果。本阶段所需变量放在同一蓝图或 ChangeSet 中。
4. 简单完整流程可一次 `preview_process_blueprint`。复杂流程先用预期 Step 和按功能块命名的占位提交 `autoStart=false` 的安全骨架并验证，再申请 `ProcessEdit`，按可独立审查的功能块逐段预演、确认、提交和回读。每次预演只需保证当前提交原子一致；不要求一个用户目标只预演一次。
5. 蓝图只描述当前创建阶段，由平台确定性编译为 ChangeSet V2。重试策略中的 `maxAttempts` 是包含首次尝试的总次数；调用方声明入口、判定、业务变量复位/清理和耗尽出口，内部计数器、复位与累加由编译器生成。修改既有流程或补齐骨架时使用稳定 ID。若工具返回 `issues`，按 `suggestedRepair` 一次修正全部已报告问题；`safeToRetry=true` 才重试同一功能块。
6. 预演后等待前台确认，再仅以 `previewId` 调用 `apply_change_set`；替换活动预演时使用 `replacePreviewId`。提交后立即使用返回的稳定 ID 和 `validate_proc` 验证，不重新发现已确认对象。最终说明区分已提交、结构有效、可运行和已观测运行；运行测试需要用户另行明确授权。

字段结构以 MCP Schema 为准，运行语义以 behavior/Guide 为准，当前配置和状态以 Bridge、Store 与 Readiness 返回为准。语义字段确有疑问时使用 `get_semantic_operation_schema`，真实原生类型确定后使用 `get_native_operation_schemas`；既有流程阶段写入使用 `preview_change_set`，`run_proc_test` 仍只在用户明确要求试运行时使用。不要在本 Skill 中补写这些契约。
