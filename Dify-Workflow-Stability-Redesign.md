# Dify Workflow 稳定性重构方案

## 1. 当前流程现状

当前导出的 Dify Workflow 位于 [Automation-proc.yml](/mnt/c/Users/Administrator/Desktop/Automation-proc.yml)。

现状结构非常简单：

```text
开始 -> 单个 Agent -> 结束
```

这份流程的主要问题不是“提示词不够长”，而是“所有职责都压在一个 Agent 节点里”。

### 1.1 当前单 Agent 同时承担的职责
- 理解用户意图
- 判断是查询还是修改
- 读取流程和 Schema
- 读取意图模板
- 组装中间意图
- 调用 `preview_intent`
- 根据报错修复
- 再调用 `apply_intent`
- 最后组织回复

这会导致三个稳定性问题：
- 职责过多，模型容易在不同阶段漂移。
- 错误定位困难，Dify 调试时只能看到“Agent 很慢”或“Agent 失败”，很难知道是分类错、模板错、字段错还是提交错。
- 工具边界不清，模型容易在“查询”“预演”“提交”之间跳来跳去。

### 1.2 当前 YAML 中的具体不稳定点

从 [Automation-proc.yml](/mnt/c/Users/Administrator/Desktop/Automation-proc.yml) 可以直接看到：

- `query` 当前是：

```text
{{#1773383704012.user_request#}}{{#1773383704012.automation_context#}}
```

这会把用户原话和上下文直接裸拼接，没有分隔、没有结构、没有显式变量边界。

- `mcp_prompts_as_tools=true`
- `mcp_resources_as_tools=true`

而你当前的 `Automation MCP Server` 并没有真正提供可用的 `resources/prompts` 内容，这会增加无效探测和噪声。

- `maximum_iterations=20`

对这种“读流程 -> 组意图 -> 预演 -> 提交”的确定性任务来说偏高，容易让 Agent 在错误恢复时反复兜圈。

## 2. 重构目标

目标不是让模型“更聪明”，而是让 Workflow 变成可控的多阶段流水线：

```text
输入标准化
-> 请求分类
-> 查询分支 / 修改分支
-> 读取与定位
-> 模板与意图生成
-> 意图校验
-> 预演
-> 条件分支
-> 提交
-> 回复汇总
```

这样做的收益：
- 每个节点只负责一件事。
- 每一段失败都有明确归属。
- 变量可以被逐段观察和缓存。
- 后续更容易插入“人工确认”“仅预演模式”“审计日志”等节点。

## 3. 推荐的新流程结构

## 3.1 输入节点

### 开始
建议保留并扩展输入变量：

- `user_request`
- `automation_context`
- `execute_mode`

`execute_mode` 建议使用枚举：
- `preview_only`
- `auto_apply`

默认推荐：
- `preview_only`

原因：
- 先把稳定性做上来。
- 让默认行为更保守。

## 3.2 预处理节点

### 节点 1：代码节点 `InputNormalizer`

职责：
- 清洗 `user_request`
- 处理空白的 `automation_context`
- 生成结构化的标准输入对象

建议输出：
- `normalized_request`
- `normalized_context`
- `has_context`
- `execute_mode`
- `workflow_input_summary`

这里不要再把上下文直接拼到用户问题后面。

建议输出形态：

```json
{
  "user_request": "延时A 指令延时改为300",
  "automation_context": {...},
  "has_context": true,
  "execute_mode": "preview_only"
}
```

## 3.3 请求分类

### 节点 2：Question Classifier `RequestClassifier`

分类建议至少拆成 4 类：
- `read_only`
- `modify_request`
- `clarify_needed`
- `unsupported`

分类标准：
- `read_only`
  - 只查询、不写入，例如“这个流程里有哪些 TCP 指令”
- `modify_request`
  - 明确要改流程，例如“把延时A改成300”
- `clarify_needed`
  - 目标不明确，例如“把这个流程改一下”
- `unsupported`
  - 超出能力边界，例如要求改账户、改 PLC 配置、改程序代码

不要让主 Agent 自己先猜再决定是否写入，应该先用专门分类节点做路由。

## 3.4 查询分支

### 节点 3A：Agent `ReadOnlyAgent`

职责：
- 只负责查询、解释、总结
- 不允许写入

建议只开放这些工具：
- `list_procs`
- `get_proc_overview`
- `get_proc_detail`
- `list_operation_types`
- `get_operation_schema`
- `get_reference_catalog`

明确禁止：
- `preview_intent`
- `apply_intent`
- `preview_patch`
- `apply_patch`

输出：
- `read_response`

## 3.5 修改分支

### 节点 3B：Agent `LocatorAgent`

职责：
- 定位流程、步骤、指令
- 读取 detail/schema/reference
- 只做“查”和“定位”

建议开放工具：
- `list_procs`
- `get_proc_overview`
- `get_proc_detail`
- `get_operation_schema`
- `get_reference_catalog`

不要在这个节点里让它直接预演或提交。

建议输出一个紧凑对象：

```json
{
  "procIndex": 3,
  "baseProcId": "7906d812-bbd7-4295-9918-c1568974b9fa",
  "stepId": "3b41fbe2-86a3-4aaa-b5ae-52af349279dd",
  "opId": "49f5b210-38d1-45c1-915c-077d05439f8a",
  "expectedOperaType": "延时",
  "targetFieldHints": ["timeMiniSecond"]
}
```

### 节点 4：Agent `IntentTemplateAgent`

职责：
- 读取本地模板
- 根据上一节点的定位结果，生成中间意图 JSON

建议开放工具：
- `list_intent_templates`
- `get_intent_template`

必要时可保留：
- `get_operation_schema`

不要开放提交类工具。

输出：
- `intent_json_text`

### 节点 5：代码节点 `IntentValidator`

职责：
- 去掉 Markdown 代码块包裹
- 校验 `intent_json_text` 是否为合法 JSON 对象
- 校验必须字段是否存在

建议最少检查：
- `intentType`
- `procIndex`
- `baseProcId`

对不同 `intentType` 再做附加字段检查，例如：
- `update_operation_field` 必须有：
  - `stepId`
  - `opId`
  - `fieldChanges`

这个节点的意义非常大：
- 把“LLM 结构漂移”拦在预演前
- 避免把明显坏的意图送进 MCP

输出：
- `intent_json`
- `intent_valid`
- `intent_error_message`

### 节点 6：If/Else `IntentValid?`

- `false` -> 直接进入错误回复分支
- `true` -> 继续预演

### 节点 7：Agent `PreviewAgent`

职责：
- 只负责调用 `preview_intent`
- 读回预演结果并做简短说明

建议只开放工具：
- `preview_intent`

不要再开放读流程、模板、提交等杂项工具。

输出：
- `preview_result_text`
- `preview_result_json`

### 节点 8：代码节点 `PreviewResultParser`

职责：
- 解析 `preview_result_json`
- 提取：
  - `ok`
  - `messages`
  - `changes`
  - `procDetail`

生成：
- `preview_ok`
- `preview_summary`
- `preview_error`

### 节点 9：If/Else `PreviewPassed?`

- 失败 -> 直接结束，返回预演失败信息
- 成功 -> 继续判断是否提交

### 节点 10：If/Else `NeedApply?`

条件：
- `execute_mode == auto_apply`

分支：
- 否 -> 返回预演成功，不提交
- 是 -> 继续提交

### 节点 11：Agent `ApplyAgent`

职责：
- 只负责调用 `apply_intent`

建议只开放工具：
- `apply_intent`

输出：
- `apply_result_text`
- `apply_result_json`

## 3.6 汇总与结束

### 节点 12：Variable Aggregator

汇总这些分支输出：
- `read_response`
- `intent_error_message`
- `preview_summary`
- `apply_result_text`

统一成：
- `final_response`

### 节点 13：结束

输出：
- `final_response`

## 4. 每个 Agent 的工具边界

这是稳定性的关键，不要所有 Agent 都放全量工具。

### `ReadOnlyAgent`
- 只读工具

### `LocatorAgent`
- 只读工具

### `IntentTemplateAgent`
- 模板工具
- 必要的 schema 工具

### `PreviewAgent`
- 只允许 `preview_intent`

### `ApplyAgent`
- 只允许 `apply_intent`

这样可以减少：
- 工具误用
- 工具循环调用
- preview 成功后又重新去改意图
- apply 阶段又回头读模板

## 5. 当前这份 YAML 立刻该改的参数

即使你暂时不重搭全流程，也建议立刻改这几项：

### 5.1 不要直接拼接 query

当前：

```text
{{#start.user_request#}}{{#start.automation_context#}}
```

建议改成独立变量输入，不要裸拼。

如果当前插件限制 Agent 只能接单一 query，至少要改成显式模板：

```text
用户请求：
{{#start.user_request#}}

本地上下文：
{{#start.automation_context#}}
```

### 5.2 关闭无用 MCP 探测

建议：
- `mcp_prompts_as_tools = false`
- `mcp_resources_as_tools = false`

原因：
- 你当前服务没有真正提供这些能力。
- 这类探测会制造额外噪声和告警。

### 5.3 降低单 Agent 最大迭代次数

当前：
- `maximum_iterations = 20`

建议：
- `8` 到 `10`

原因：
- 当前任务不是开放式多轮搜索，而是相对确定的工具链。
- 迭代过高更容易漂。

### 5.4 拆出 `preview_only`

建议新增输入变量：
- `execute_mode`

默认：
- `preview_only`

这会显著提高整体稳定性，因为大部分问题会在预演阶段暴露，而不会直接落盘。

## 6. 最推荐的第一版落地顺序

如果你想最小成本提高稳定性，不要一次把所有节点都加满。建议分两轮做。

### 第一轮
- 开始
- `InputNormalizer`
- `RequestClassifier`
- `ReadOnlyAgent`
- `LocatorAgent`
- `IntentTemplateAgent`
- `IntentValidator`
- `PreviewAgent`
- `PreviewPassed?`
- 结束

这一轮先做到：
- 查询稳定
- 修改能稳定预演
- 默认不提交

### 第二轮
- 加 `NeedApply?`
- 加 `ApplyAgent`
- 加 `Variable Aggregator`

这一轮再做到：
- 自动提交
- 统一输出

## 7. 结论

这份 [Automation-proc.yml](/mnt/c/Users/Administrator/Desktop/Automation-proc.yml) 当前最大的问题不是提示词，而是拓扑过于简单。  
`开始 -> 单Agent -> 结束` 对流程修改类任务不够稳。

更合适的结构是：

```text
开始
-> 输入标准化
-> 请求分类
-> 查询分支 / 修改分支
-> 定位与读取
-> 模板与意图生成
-> 意图校验
-> 预演
-> 预演结果判断
-> 提交判断
-> 提交
-> 汇总
-> 结束
```

这个改法对你当前项目最有价值的点有三个：
- 把“分类、定位、意图生成、预演、提交”拆开，减少单 Agent 漂移。
- 通过代码节点和条件节点，把明显格式错误挡在 MCP 之前。
- 默认走 `preview_only`，把错误尽量留在可恢复阶段。

## 8. 参考资料

本方案参考了 Dify 官方节点能力说明：
- [Question Classifier](https://docs.dify.ai/en/guides/workflow/node/question-classifier)
- [If-Else](https://docs.dify.ai/en/use-dify/nodes/ifelse)
- [Variable Aggregator](https://docs.dify.ai/en/guides/workflow/node/variable-aggregator)
- [Workflow Key Concepts / DSL](https://docs.dify.ai/en/guides/workflow/node/start)
- [Variable Inspect](https://docs.dify.ai/versions/3-2-x/en/user-guide/workflow/debug-and-preview/variable-inspect)
