---
name: automation-process-authoring
description: 在 Automation 低代码平台中创建、修改、重构或复制 Proc→Step→OperationType 流程时使用。按需读取实现目标必需的当前事实，通过 ChangeSet V2 预演、确认、提交并验证。只读流程评审使用 automation-process-review；仅给设计方案、源码开发、只读运行诊断和一般问答不触发本 Skill。
---

# Automation 流程编写

1. 明确当前功能块的可观察目标、完成证据、外部副作用和失败出口。用户只指定成功/失败出口时，只建立对应控制流，不自动补报警、停机、复位、重试或提示等副作用。创建或重构复杂流程时按目标读取一次 `get_process_design_guide`，通常使用单个主题和 `compact`；只有当前取舍依赖完整背景时才使用 `full`。
2. 用 `resolve_authoring_inputs` 批量解析本功能块的变量、IO、工站、点位、通讯或报警绑定，用 `resolve_operation_capability` 批量解析业务动作。聚合结果足够时直接继续；只有歧义或字段缺口才读取推荐的精确契约。候选不是绑定事实，`bindingAllowed=false` 时保留占位、声明新变量或询问用户。
3. 已知动作使用准确语义 `kind`。只有尚缺外部事实的动作或结果使用一等 `config.placeholder`；已知的分支、清理和成功/失败出口仍写入结构，不能吞进占位 `message`。重试优先使用当前真实指令支持的 `RetryCount`；需要流程级重试时，直接用变量复位/累加、分支和跳转表达必要的业务语义。占位不运行并使流程保持 `incomplete`；保持占位时可用 `operation.update` 修订名称、说明和计划出口，事实补齐后用 `operation.replace` 完整替换，不得用延时、弹框、常量或伪状态冒充真实能力。
4. 新建流程直接使用 `preview_change_set`。简单且事实充分时可一次完整预演；复杂、带回环或资源未决时，先以 `autoStart=false` 提交可独立审查和保存的安全功能块，再申请 `ProcessEdit` 按稳定 ID 续建。每次预演只承担当前阶段的原子一致，不要求一个用户目标一次写完。
5. `ProcessCreate` 的当前 ChangeSet 只使用 `process.create`、`step.append` 和 `operation.append`；为新流程和新步骤提供局部 `key`，后续动作精确引用这些 key。`operationKey` 跨步骤全局唯一时可直接使用，只有同名歧义时才附加 `stepId/stepKey`；提交后的目标优先使用稳定 `operationId`。预演后核对变化摘要、Readiness、警告、阻塞、分支和出口；自然语言声明不是实现证据。工具返回结构化修复建议时，根据完整问题集修正当前功能块，不用盲目小步猜测。
6. 预演后等待前台确认，再仅用 `previewId` 调用 `apply_change_set`。提交后使用返回的稳定 ID 和 `validate_proc` 验证，不重新发现已确认对象。若用户已明确且不依赖外部事实的控制结构没有落入机械证据，申请 `ProcessEdit` 补齐，不能直接宣称完成。
7. 最终区分已提交、结构有效、可运行和已观测运行；允许的占位必须明确列为待补齐项。运行测试仍需用户明确授权。

字段结构服从 MCP Schema，运行语义服从 behavior/Guide，当前事实服从 Bridge、Store 与 Readiness；本 Skill 不复制动态字段、错误码和状态机契约。
