# Dify Workflow 节点提示词与变量映射

## 1. 适用范围

- 适用于当前 `Automation` 项目的 Dify Workflow。
- 适用于已经挂载 `Automation MCP Server` 的本地部署 Dify。
- 适用于把当前单节点 Agent 流程，拆成“分类、定位、模板、预演、提交”多节点流程。

这份文档是 [Dify-Workflow-Stability-Redesign.md](/mnt/f/Automation/Dify-Workflow-Stability-Redesign.md) 的施工版。  
前者讲结构，这份文档讲“每个节点具体填什么”。

## 2. 推荐拓扑

```text
开始
-> InputNormalizer（Code）
-> RequestClassifier（Question Classifier）
-> ReadOnlyAgent（查询分支）
-> LocatorAgent（修改分支）
-> IntentTemplateAgent
-> IntentValidator（Code）
-> IntentValid?（If-Else）
-> PreviewAgent
-> PreviewResultParser（Code）
-> PreviewPassed?（If-Else）
-> NeedApply?（If-Else）
-> ApplyAgent
-> Variable Aggregator
-> 结束
```

## 3. 开始节点

### 节点类型
- `Start`

### 输入变量
- `user_request`
- `automation_context`
- `execute_mode`

### 建议定义

- `user_request`
  - 类型：`Paragraph`
  - 必填：`true`
- `automation_context`
  - 类型：`Paragraph`
  - 必填：`false`
- `execute_mode`
  - 类型：`Select`
  - 必填：`true`
  - 选项：
    - `preview_only`
    - `auto_apply`
  - 默认值：
    - `preview_only`

### 说明
- 不要把 `automation_context` 拼接进 `user_request`。
- 让上下文保持独立变量，便于后续节点分别引用。

## 4. InputNormalizer

### 节点类型
- `Code`

### 输入变量
- `user_request = {{#开始.user_request#}}`
- `automation_context = {{#开始.automation_context#}}`
- `execute_mode = {{#开始.execute_mode#}}`

### 输出变量
- `normalized_request`
- `normalized_context`
- `has_context`
- `normalized_execute_mode`
- `workflow_input_summary`

### Python 代码示例

```python
import json


def main(user_request: str, automation_context: str = "", execute_mode: str = "preview_only") -> dict:
    request_text = (user_request or "").strip()
    context_text = (automation_context or "").strip()
    mode = (execute_mode or "preview_only").strip()
    if mode not in ("preview_only", "auto_apply"):
        mode = "preview_only"

    normalized_context = ""
    has_context = False

    if context_text:
        try:
            parsed = json.loads(context_text)
            normalized_context = json.dumps(parsed, ensure_ascii=False, separators=(",", ":"))
            has_context = True
        except Exception:
            normalized_context = context_text
            has_context = True

    summary = {
        "user_request": request_text,
        "has_context": has_context,
        "execute_mode": mode,
    }

    return {
        "normalized_request": request_text,
        "normalized_context": normalized_context,
        "has_context": has_context,
        "normalized_execute_mode": mode,
        "workflow_input_summary": json.dumps(summary, ensure_ascii=False),
    }
```

### 作用
- 去掉前后空白
- 规范化上下文
- 兜底 `execute_mode`
- 避免后续节点再做字符串拼接

## 5. RequestClassifier

### 节点类型
- `Question Classifier`

### 输入变量
- `{{#InputNormalizer.normalized_request#}}`

### 分类标签

#### `read_only`
描述：
- 用户只是在查询、分析、解释流程，不要求修改流程。

典型例子：
- “这个流程里有哪些 TCP 指令”
- “帮我分析为什么这里会报警”

#### `modify_request`
描述：
- 用户明确希望修改流程、步骤、指令、字段或结构。

典型例子：
- “把延时A改成300”
- “新增一步检测”

#### `clarify_needed`
描述：
- 用户想改，但目标不清楚，缺少流程、步骤、指令或字段定位信息。

典型例子：
- “把这个流程优化一下”
- “改成更合理”

#### `unsupported`
描述：
- 请求超出当前 Workflow 边界，不属于流程读取/修改。

典型例子：
- “帮我改账户权限”
- “帮我改程序源码”

### 配置建议
- 模型用轻量一点即可，但要稳定。
- 分类描述要简短明确，不要重叠。

## 6. ReadOnlyAgent

### 节点类型
- `Agent`

### 输入变量
- `query = {{#InputNormalizer.normalized_request#}}`

### 可用工具
- `list_procs`
- `get_proc_overview`
- `get_proc_detail`
- `list_operation_types`
- `get_operation_schema`
- `get_reference_catalog`

### 禁用工具
- `list_intent_templates`
- `get_intent_template`
- `preview_intent`
- `apply_intent`
- `preview_patch`
- `apply_patch`

### 提示词

```text
你是 Automation 流程查询 Agent。

你的职责：
1. 只读取和分析流程，不做任何写入。
2. 根据用户问题调用只读 MCP 工具。
3. 返回简洁、准确的分析结果。

必须遵守：
1. 不假设流程名、步骤名、指令名唯一。
2. 需要定位时先用 list_procs 或 get_proc_overview。
3. 需要字段细节时调用 get_proc_detail 或 get_operation_schema。
4. 如果问题涉及变量、IO、通讯、PLC、工站等引用对象，调用 get_reference_catalog。
5. 禁止调用任何写接口。

回答要求：
1. 先给结论，再给必要依据。
2. 保持简洁，不输出冗长推理。
3. 如果目标不明确，说明缺少什么信息。
```

### 输出变量
- 使用默认 `text`

## 7. LocatorAgent

### 节点类型
- `Agent`

### 输入变量

建议 `query` 使用显式模板：

```text
用户请求：
{{#InputNormalizer.normalized_request#}}

本地上下文：
{{#InputNormalizer.normalized_context#}}
```

### 可用工具
- `list_procs`
- `get_proc_overview`
- `get_proc_detail`
- `get_operation_schema`
- `get_reference_catalog`

### 禁用工具
- `list_intent_templates`
- `get_intent_template`
- `preview_intent`
- `apply_intent`
- `preview_patch`
- `apply_patch`

### 提示词

```text
你是 Automation 修改定位 Agent。

你的职责：
1. 根据用户请求和可选本地上下文定位目标流程、步骤、指令。
2. 读取 get_proc_detail、get_operation_schema、get_reference_catalog。
3. 输出供后续节点使用的紧凑定位结果。

必须遵守：
1. automation_context 只能当定位线索，不能当最终事实。
2. 不假设流程名、步骤名、指令名唯一。
3. 修改前必须读取 get_proc_detail。
4. 修改指令字段前必须读取 get_operation_schema。
5. 涉及引用对象时必须读取 get_reference_catalog。
6. 不要生成中间意图，不要预演，不要提交。

输出要求：
1. 只输出一个 JSON 对象。
2. JSON 中尽量包含：
   - procIndex
   - baseProcId
   - stepId
   - opId
   - expectedOperaType
   - recommendedIntentType
   - targetFieldHints
   - locateSummary
3. 如果目标不明确，也输出 JSON，并在 locateSummary 中写明歧义点。
4. 不要输出 Markdown 代码块。
```

### 期望输出示例

```json
{
  "procIndex": 3,
  "baseProcId": "7906d812-bbd7-4295-9918-c1568974b9fa",
  "stepId": "3b41fbe2-86a3-4aaa-b5ae-52af349279dd",
  "opId": "49f5b210-38d1-45c1-915c-077d05439f8a",
  "expectedOperaType": "延时",
  "recommendedIntentType": "update_operation_field",
  "targetFieldHints": [
    "timeMiniSecond"
  ],
  "locateSummary": "已定位到流程 3 的步骤 0 指令 延时A，建议修改延时时间字段。"
}
```

## 8. IntentTemplateAgent

### 节点类型
- `Agent`

### 输入变量

建议 `query` 使用：

```text
用户请求：
{{#InputNormalizer.normalized_request#}}

定位结果：
{{#LocatorAgent.text#}}

本地上下文：
{{#InputNormalizer.normalized_context#}}
```

### 可用工具
- `list_intent_templates`
- `get_intent_template`
- `get_operation_schema`
- `get_reference_catalog`

### 禁用工具
- `preview_intent`
- `apply_intent`
- `preview_patch`
- `apply_patch`

### 提示词

```text
你是 Automation 中间意图生成 Agent。

你的职责：
1. 根据用户请求和定位结果，读取本地意图模板。
2. 生成一个合法的中间意图 JSON。
3. 中间意图必须能交给后续 preview_intent 使用。

必须遵守：
1. 先调用 list_intent_templates 或 get_intent_template。
2. 必须以模板中的 intentShape 和 rules 为准，不要凭记忆手写结构。
3. 字段名必须使用 get_operation_schema 或 get_reference_catalog 返回的精确值。
4. fieldChanges 和 fieldValues 必须是 JSON 对象。
5. 只生成一个中间意图对象。
6. 不要预演，不要提交。

输出要求：
1. 只输出一个 JSON 对象。
2. 不要输出 Markdown 代码块。
3. 不要输出解释文字。
```

### 期望输出示例

```json
{
  "intentType": "update_operation_field",
  "procIndex": 3,
  "baseProcId": "7906d812-bbd7-4295-9918-c1568974b9fa",
  "stepId": "3b41fbe2-86a3-4aaa-b5ae-52af349279dd",
  "opId": "49f5b210-38d1-45c1-915c-077d05439f8a",
  "expectedOperaType": "延时",
  "fieldChanges": {
    "timeMiniSecond": "300"
  }
}
```

## 9. IntentValidator

### 节点类型
- `Code`

### 输入变量
- `intent_text = {{#IntentTemplateAgent.text#}}`

### 输出变量
- `intent_json`
- `intent_valid`
- `intent_error_message`
- `intent_type`

### Python 代码示例

```python
import json


def strip_code_fence(text: str) -> str:
    value = (text or "").strip()
    if value.startswith("```"):
        lines = value.splitlines()
        if len(lines) >= 2 and lines[-1].strip() == "```":
            return "\n".join(lines[1:-1]).strip()
    return value


def main(intent_text: str) -> dict:
    cleaned = strip_code_fence(intent_text)
    if not cleaned:
        return {
            "intent_json": "",
            "intent_valid": False,
            "intent_error_message": "IntentTemplateAgent 未输出中间意图 JSON。",
            "intent_type": "",
        }

    try:
        obj = json.loads(cleaned)
    except Exception as ex:
        return {
            "intent_json": cleaned,
            "intent_valid": False,
            "intent_error_message": f"中间意图不是合法 JSON：{ex}",
            "intent_type": "",
        }

    if not isinstance(obj, dict):
        return {
            "intent_json": cleaned,
            "intent_valid": False,
            "intent_error_message": "中间意图必须是 JSON 对象。",
            "intent_type": "",
        }

    required_keys = ["intentType", "procIndex", "baseProcId"]
    for key in required_keys:
        if key not in obj:
            return {
                "intent_json": cleaned,
                "intent_valid": False,
                "intent_error_message": f"中间意图缺少必填字段：{key}",
                "intent_type": obj.get("intentType", ""),
            }

    return {
        "intent_json": json.dumps(obj, ensure_ascii=False, separators=(",", ":")),
        "intent_valid": True,
        "intent_error_message": "",
        "intent_type": str(obj.get("intentType", "")),
    }
```

## 10. IntentValid?

### 节点类型
- `If-Else`

### 条件
- IF：`{{#IntentValidator.intent_valid#}} is true`

### 分支
- `true` -> `PreviewAgent`
- `false` -> 直接进入 `Variable Aggregator`

## 11. PreviewAgent

### 节点类型
- `Agent`

### 输入变量
- `query = {{#IntentValidator.intent_json#}}`

### 可用工具
- `preview_intent`

### 禁用工具
- 全部其他工具

### 提示词

```text
你是 Automation 预演 Agent。

你的职责：
1. 接收已经通过基础校验的中间意图 JSON。
2. 仅调用 preview_intent。
3. 返回原始预演结果，不做额外改写。

必须遵守：
1. 不要重新生成意图。
2. 不要重新定位。
3. 不要调用任何其他工具。
4. 不要提交。

输出要求：
1. 直接输出 preview_intent 的结果文本。
2. 不要再加解释。
3. 不要输出 Markdown 代码块。
```

## 12. PreviewResultParser

### 节点类型
- `Code`

### 输入变量
- `preview_text = {{#PreviewAgent.text#}}`

### 输出变量
- `preview_ok`
- `preview_summary`
- `preview_error`
- `preview_result_json`

### Python 代码示例

```python
import json


def main(preview_text: str) -> dict:
    text = (preview_text or "").strip()
    if not text:
        return {
            "preview_ok": False,
            "preview_summary": "",
            "preview_error": "preview_intent 未返回内容。",
            "preview_result_json": "",
        }

    try:
        obj = json.loads(text)
    except Exception as ex:
        return {
            "preview_ok": False,
            "preview_summary": "",
            "preview_error": f"preview_intent 返回的不是合法 JSON：{ex}",
            "preview_result_json": text,
        }

    if not obj.get("ok", False):
        return {
            "preview_ok": False,
            "preview_summary": "",
            "preview_error": obj.get("message", "预演失败"),
            "preview_result_json": json.dumps(obj, ensure_ascii=False),
        }

    data = obj.get("data", {})
    preview = data.get("preview", {})
    messages = preview.get("messages", []) or []
    summary = "；".join(str(item) for item in messages[:5])

    return {
        "preview_ok": True,
        "preview_summary": summary,
        "preview_error": "",
        "preview_result_json": json.dumps(obj, ensure_ascii=False),
    }
```

## 13. PreviewPassed?

### 节点类型
- `If-Else`

### 条件
- IF：`{{#PreviewResultParser.preview_ok#}} is true`

### 分支
- `true` -> `NeedApply?`
- `false` -> 直接进入 `Variable Aggregator`

## 14. NeedApply?

### 节点类型
- `If-Else`

### 条件
- IF：`{{#InputNormalizer.normalized_execute_mode#}} is auto_apply`

### 分支
- `true` -> `ApplyAgent`
- `false` -> 直接进入 `Variable Aggregator`

## 15. ApplyAgent

### 节点类型
- `Agent`

### 输入变量
- `query = {{#IntentValidator.intent_json#}}`

### 可用工具
- `apply_intent`

### 禁用工具
- 全部其他工具

### 提示词

```text
你是 Automation 提交 Agent。

你的职责：
1. 接收已经预演通过的中间意图 JSON。
2. 仅调用 apply_intent。
3. 返回原始提交结果，不做额外改写。

必须遵守：
1. 不要重新生成意图。
2. 不要重新定位。
3. 不要调用任何其他工具。
4. 只有在上游已经确认 preview_ok=true 时才执行。

输出要求：
1. 直接输出 apply_intent 的结果文本。
2. 不要再加解释。
3. 不要输出 Markdown 代码块。
```

## 16. Variable Aggregator

### 节点类型
- `Variable Aggregator`

### 聚合目标
- 输出单一字符串变量：`final_response`

### 建议聚合顺序

#### 情况 A：查询分支
- 取 `ReadOnlyAgent.text`

#### 情况 B：意图校验失败
- 取 `IntentValidator.intent_error_message`

#### 情况 C：预演失败
- 取 `PreviewResultParser.preview_error`

#### 情况 D：预演成功但不提交
- 取：

```text
预演成功，未提交。
预演摘要：{{#PreviewResultParser.preview_summary#}}
```

#### 情况 E：提交成功
- 取 `ApplyAgent.text`

### 建议
- 聚合前先保证各分支最终都能落到 Aggregator。
- 不要在结束节点直接硬连多个分支，Aggregator 更适合做统一出口。

## 17. 结束节点

### 节点类型
- `End`

### 输出变量
- `final_response = {{#Variable Aggregator.final_response#}}`

## 18. 当前最小可跑版本

如果你不想一次搭这么多节点，最小推荐组合是：

```text
开始
-> InputNormalizer
-> RequestClassifier
-> ReadOnlyAgent
-> LocatorAgent
-> IntentTemplateAgent
-> IntentValidator
-> PreviewAgent
-> PreviewResultParser
-> Variable Aggregator
-> 结束
```

先把这条链跑顺，再加 `NeedApply?` 和 `ApplyAgent`。

## 19. 参数建议

基于 Dify 官方 Agent 节点说明，`Maximum Iterations` 是防止无限循环的安全阈值，简单任务通常只需要较低迭代数。  
参考：
- [Agent 节点](https://docs.dify.ai/en/guides/workflow/node/agent)

建议：
- `ReadOnlyAgent`：`4`
- `LocatorAgent`：`6`
- `IntentTemplateAgent`：`6`
- `PreviewAgent`：`2`
- `ApplyAgent`：`2`

另外，基于 Dify 官方节点说明：
- `Question Classifier` 适合做语义路由
- `If-Else` 适合做显式条件判断
- `Variable Aggregator` 适合汇总不同路径的变量
- `Code` 节点适合做 JSON 清洗和结构化校验

参考：
- [Question Classifier](https://docs.dify.ai/en/guides/workflow/node/question-classifier)
- [If-Else](https://docs.dify.ai/en/guides/workflow/node/ifelse)
- [Variable Aggregator](https://docs.dify.ai/en/guides/workflow/node/variable-aggregator)
- [Code 节点](https://docs.dify.ai/en/guides/workflow/node/code)
- [Start / Variables](https://docs.dify.ai/en/guides/workflow/node/start)
