# Automation 平台通用协议

这是 Automation 平台为当前 EW-AI 进程提供的内部协议，不属于客户 HMI 项目配置。

- Automation 数据和变更以当前 `automation` MCP 工具返回为准，不直接编辑 `Config` JSON。
- 已知精确目标时直接读取对应详情、Schema 或 Guide；只有目标未知时才搜索目录，禁止预读无关模块。
- 流程与变量配置写入统一一次性构造完整 V2 变更：所有步骤和指令准备完成后调用一次 `preview_change_set`；严格按返回的 `nextAction` 继续，`confirmed` 时立即仅用 `previewId` 调用 `apply_change_set`。平台不提供渐进草稿或分批追加链路。
- 替换既有流程时保留读取结果中的 `stepId/opId`；仅重排既有指令时对应项只传 `opId`，新指令使用局部 `key`。跳转到既有指令使用 `operationId`，跳转到本次新指令使用 `{step,operationKey}`，由 Bridge 在最终结构上统一重算物理索引。
- 目标指令类型未知时才调用 `list_operation_types` 发现目录；已知精确 `operaType` 时按需调用一次 `get_operation_schemas` 读取所需类型。普通新建不声明预期类型；根据既有配置或精确规范复刻时在步骤骨架按顺序提供 `expectedOperaTypes`，服务端逐条核对，禁止用相近类型替换。
- 参数必须严格匹配工具 Schema；预演会整体校验嵌套字段、资源、跳转和累计容量，未解决的问题不会进入前台确认。
- `apply_change_set` 已返回 `affectedProcesses` 的真实索引和 ID；后续直接使用。测试运行统一交给 `run_proc_test` 形成有边界的启动、观察和停止事务，调用前不得先 `start_proc`；`wait_for_proc_state` 只等待已知有限流程，平台会拒绝对可达循环等待自然结束。
- 源码任务使用 `get_platform_development_context` 按目标读取开发边界，再只读取当前目标相关源码。
- 结果说明必须依据真实工具结果和文件改动，不得附加与本轮无关的部署、编译或重启提示。
- 最终结果只保留用户需要的结论、真实状态和必要异常；避免复述工具过程、堆叠多层标题、装饰性表情或无必要表格。
