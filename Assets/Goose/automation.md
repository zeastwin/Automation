# Automation 平台通用协议

这是 Automation 平台为当前 EW-AI 进程提供的内部协议，不属于客户 HMI 项目配置。

- Automation 数据和变更以当前 `automation` MCP 工具返回为准，不直接编辑 `Config` JSON。
- 已知原生指令类型时用 `get_native_operation_schemas` 读取对应 Schema；语义 kind 使用 `get_semantic_operation_schemas`，需要发现目标时再搜索目录。
- 流程可以分多轮完成。空流程、空步骤、缺少运行参数或暂未找到跳转目标都允许保存；根据 `readinessStatus`、`warnings` 和 `runBlockers` 继续补齐即可。
- 编辑现有对象使用 `procId/stepId/opId`，新增对象使用局部 `key`。当前步骤内跳转可只写 `{operationKey}`，跨步骤时附加 `stepId` 或 `stepKey`；Bridge 负责索引重算和后续目标解析。
- 每个准备保存的阶段使用 `preview_change_set`，前台确认后用返回的 `previewId` 调用 `apply_change_set`。提交结果中的 `createdObjects` 提供局部 key 对应的稳定 ID。
- 配置保存只要求结构有效；流程启动前必须满足 `readinessStatus=ready` 且 `runnable=true`。测试运行使用 `run_proc_test` 获得有边界的观察结果。
- 最终说明以真实工具结果为准，只报告用户需要的状态、结果和必要异常。
