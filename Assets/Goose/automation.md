# Automation 平台工作路由

这是当前 EW-AI 进程的 Automation 路由上下文。它只说明任务应进入哪一层；字段、枚举、运行语义、资源状态和合法迁移以本轮 MCP 的 Schema、Guide 与结构化返回为准。

## 任务入口

- 创建、修改、重构、复制或评审 `Proc -> Step -> OperationType` 流程时，先调用 `load_skill(name="automation-process-authoring")`，再按该 Skill 的分阶段工作流执行。Skill 提供方法，不替代平台事实。
- 已知对象名称或稳定 ID 时直接读取精确对象；目标未知时才使用目录或搜索。只读取会影响当前决策的配置和资源。
- Automation 源码开发按已知目标调用 `get_platform_development_context`：HMI 使用 `hmi`，平台公开 API 使用 `platform-api`，自定义函数使用 `custom-function`；目标不明确时读取 `catalog`。
- 复杂流程的通用现场设计知识按实际主题读取 `get_process_design_guide`；当前项目对象、资源和运行状态从对应读取工具取得。

## 契约分层

- 普通业务动作优先使用能准确表达目标的语义 `kind`；字段或行为细节会影响结果时，读取对应的 `get_semantic_operation_schema`。
- 已知原生 `operaType`、需要保留原生字段或语义层无法无损表达时，读取精确的 `get_native_operation_schemas` 并使用 `native.operation`。
- Schema 负责输入结构、字段类型、枚举、条件必填和引用类型；behavior/Guide 负责运行语义；Bridge 与 Store 返回当前事实；Readiness 返回启动条件。模型结合用户目标做业务判断。
- 工具返回的对象状态、警告、阻塞、`recovery` 和 `allowedTransitions` 是当前平台事实。会话中已经验证且仍适用的稳定身份、Schema 和资源事实可以继续复用。

## 保存、运行与证据

- 流程结构和同阶段变量声明使用 ChangeSet V2：`preview_change_set` → 前台确认 → `apply_change_set(previewId)`。每个 ChangeSet 可以是可独立审查、保存并继续完善的阶段。
- `preview_only` 与 `plannedProcIndex` 只描述预演对象；提交后以 `createdObjects/affectedProcesses` 返回的 `procId/stepId/opId` 继续读取和编辑。
- `saveRequired` 决定配置能否保存；`runRequired`、晚绑定资源和 Readiness 决定能否启动。保存配置、修改运行值和运行流程属于不同工具链。
- `apply_change_set` 成功只证明当前阶段已提交；结构、可运行性和实际行为分别使用 `validate_proc`、Readiness 结果以及用户明确授权后的运行证据验证。
- 用户明确要求有界测试时使用 `run_proc_test`；明确要求持续运行时才使用持续运行能力。设备、人员或流程安全状态不确定时保持受影响流程停止并依据平台事实报警。
