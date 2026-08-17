---
name: automation-process-authoring
description: 在 Automation 低代码平台中创建、修改、重构或复制 Proc→Step→OperationType 流程时使用。按需读取实现目标必需的当前事实，通过 ChangeSet V2 预演、确认、提交并验证。只读流程评审使用 automation-process-review；仅给设计方案、源码开发、只读运行诊断和一般问答不触发本 Skill。
---

# Automation 流程编写

1. 明确当前功能块的可观察目标、完成证据、外部副作用和失败出口。用户只指定成功/失败出口时，只建立对应控制流，不自动补报警、停机、复位、重试或提示等副作用。创建或重构复杂流程时按目标读取一次 `get_process_design_guide`，通常使用单个主题和 `compact`；只有当前取舍依赖完整背景时才使用 `full`。
2. 当前功能块依赖现场资源而用户未给出精确对象时，设计指南已返回 `goalCoverage.resourceRequests` 就直接把这些类别交给 `list_authoring_resources`；否则一次按目标相关类别查看有界目录。`motion` 同时返回工站、实际轴配置和已规划/已示教点位，IO 按输入/输出分开。目录项有 `resourceRef` 时直接复制到支持该引用的字段，不改写展示名称；绑定失败直接采用错误结果中的同类型候选。新流程私有变量直接在当前 ChangeSet 中按 `ownerProcess.key` 声明，既有流程私有变量按稳定 `ownerProcess.procId` 使用。目录项证明资源存在，不自动证明电气极性、安全位或业务角色；角色无法消歧时保留占位或询问用户。`authoringGaps` 表示已发现相关资源但仍缺精确目标等事实：它不证明该功能不需要；改用另一机构会改变目标含义时，询问用户或用占位保留原目标。运动工站已有实际轴但没有目标点位时，按当前目标规划有业务含义的点位名并在原生运动动作中使用；确认提交流程后申请 `ResourceEdit`，用 `plan_motion_points` 把这些名称批量登记为待示教点位。planned 点位可以保存和继续搭建流程，但人工示教坐标前保持 `incomplete` 且不能运行；不得猜坐标。单个输入的反向状态也不能代替相反机械终态反馈。基础 Schema 已足以表达的熟悉语义 kind 直接预演；陌生、歧义或原生动作才用 `resolve_operation_capability` 一次批量解析，其唯一命中只证明动作类型和契约已确定，不校验外部资源。
3. 已知动作使用准确语义 `kind`；字符串置空用 `string.clear`，double 清零用 `number.zero`。只有尚缺外部事实的动作或结果使用一等 `config.placeholder`；已知的分支、清理和成功/失败出口仍写入结构，不能吞进占位 `message`。重试优先使用当前真实指令支持的 `RetryCount`；需要流程级重试时，直接用变量复位/累加、分支和跳转表达必要的业务语义。占位不运行并使流程保持 `incomplete`；保持占位时可用 `operation.update` 修订名称、说明和计划出口，事实补齐后用 `operation.replace` 完整替换，不得用延时、弹框、常量或伪状态冒充真实能力。
4. 新建流程直接使用 `ProcessCreate.preview_change_set`。简单且事实充分时可一次完整预演；复杂、带回环或资源未决时，首阶段用局部 `key` 提交一个 `autoStart=false`、可独立审查和保存的安全功能块，必要时它只是新流程骨架。每次预演只承担当前阶段的原子一致，不要求一个用户目标一次写完。
5. 首阶段确认并 `apply_change_set` 后，保留返回的 `authoringLease.leaseId` 与稳定 `procId`。继续搭建同一个新流程时仍留在 `ProcessCreate`：后续 `preview_change_set` 原样传 `authoringLeaseId`，所有 `targetProcess` 和流程变量 `ownerProcess` 使用该稳定 `procId`，可按需要追加、插入、更新、替换、移动或删除步骤与指令。当前阶段的 `operationKey` 跨步骤全局唯一时可直接引用，只有同名歧义时才补充步骤选择器；已提交对象优先使用稳定 ID。只有凭据不可用、已过期或目标是独立既有流程时才申请 `ProcessEdit`。需要核对已提交结构时直接用 `inspect_process`，不重新发现已确认对象。
6. 预演后核对变化摘要、Readiness、警告、阻塞、分支和出口，再等待前台确认并仅用 `previewId` 提交。预演失败按错误类型修复：资源绑定错误采用 `bindingRepair.candidates`，字段或行为错误才使用对应小契约；不为同一错误重复读取 Schema，也不盲目拆成无意义小步。提交后使用稳定 ID 和 `validate_proc` 验证；Readiness 只证明结构状态，结束前还要按所选设计指南的功能槽核对用户目标，区分已实现、占位和缺少事实。自然语言声明不是实现证据。
7. 最终区分已提交、结构有效、可运行和已观测运行；允许的占位必须明确列为待补齐项。运行测试仍需用户明确授权。

字段结构服从 MCP Schema，运行语义服从 behavior/Guide，当前事实服从 Bridge、Store 与 Readiness；本 Skill 不复制动态字段、错误码和状态机契约。
