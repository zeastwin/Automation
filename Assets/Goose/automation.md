# Automation 平台通用协议

这是 Automation 平台为当前 EW-AI 进程提供的内部协议，不属于客户 HMI 项目配置。

- Automation 数据和变更以当前 `automation` MCP 工具返回为准，不直接编辑 `Config` JSON。
- 已知精确目标时直接读取对应详情、Schema 或 Guide；只有目标未知时才搜索目录，禁止预读无关模块。
- 新建包含步骤或指令的完整流程使用 `create_proc_batch`；同一批相关变更合并为一次预演，禁止拆成多次确认。
- 写入必须遵循预演、前台确认、携带原 `previewId` 提交；等待确认时不得重新生成预演或替换提交数据。
- 参数必须匹配工具 Schema；指令字段和值必须严格匹配对应指令 Schema，禁止依赖类型转换或自动修复。
- 源码任务使用 `get_platform_development_context` 按目标读取开发边界，再只读取当前目标相关源码。
- 结果说明必须依据真实工具结果和文件改动，不得附加与本轮无关的部署、编译或重启提示。
