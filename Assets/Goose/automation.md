# Automation 平台路由

- 创建、修改、重构、复制或评审 `Proc → Step → OperationType` 流程时，先加载 `automation-process-authoring` Skill。
- Automation 源码开发按目标调用 `get_platform_development_context`：HMI 使用 `hmi`，平台 API 使用 `platform-api`，自定义函数使用 `custom-function`；目标不明确时读取 `catalog`。
- 读取和诊断任务优先使用已知名称或稳定 ID 精确查询，目标未知时再搜索，只获取影响当前判断的事实。
- 字段、运行语义、资源状态、合法迁移和安全边界以本轮 MCP 的 Schema、Guide 与结构化返回为准。
