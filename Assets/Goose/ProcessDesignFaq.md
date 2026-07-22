# Automation 流程设计 FAQ

> 版本：1.3
> 用途：以 Q&A 形式提供常见流程设计模式的补充参考，减少 `preview_change_set` 反复试错。
> 高频骨架（循环、重试等）的正例与反例见本 FAQ 各主题；本 FAQ 同时负责边界演算与扩展场景。
> 字段细节、枚举和条件必填以当前 MCP Schema 为准；本 FAQ 只说明控制流如何组合。

---

<!-- faq:loop:start -->

## 循环流程

### 触发条件

当用户请求出现以下任一关键词时，按本主题构造 ChangeSet：**循环、计数、重复执行、循环 N 次、for 循环、循环流程**。

### 循环 ChangeSet 必填字段速查

首次构造时逐项核对，漏任何一项都会导致 `preview_change_set` 直接返回 `INVALID_ARGUMENT`：

| 对象 | 必填字段 | 说明 |
|------|----------|------|
| `process.create` | `key`, `name` | `key` 用于本阶段内部引用，`name` 是流程显示名。 |
| `step.append` | `key`, `name` | `key` 用于本阶段内部引用，`name` 是步骤显示名。 |
| `operation.append` | `key`, `kind`, `name` | `key` 是 `branch.*` 跳转的目标；`kind` 是指令类型；`name` 是指令显示名。 |
| `variables[]` | `name`, `scope`, `type`, `policy` | `name` 是变量精确名称；`policy` 用 `reuse`（已存在或允许创建）或 `create`（强制新建）。 |

### Q1：用户要求“创建一个循环 N 次的流程”，ChangeSet 应如何组织？

A1：优先采用语义指令 `variable.set` → `variable.add` → `branch.number_compare` 形成闭环，**不需要**原生 `跳转`/`逻辑判断`/`修改变量`，也通常不需要额外 `flow.goto`。下面是带字段说明的完整骨架：

```json
{
  "changeSet": {
    "title": "创建循环计数流程",
    "actions": [
      {
        "type": "process.create",
        "process": { "key": "loop_proc", "name": "循环计数流程", "autoStart": false, "disable": false }
      },
      {
        "type": "step.append",
        "targetProcess": { "key": "loop_proc" },
        "step": { "key": "loop_step", "name": "循环体", "disable": false }
      },
      {
        "type": "operation.append",
        "targetProcess": { "key": "loop_proc" },
        "targetStep": { "key": "loop_step" },
        "operation": {
          "key": "init_counter",
          "kind": "variable.set",
          "name": "初始化计数器",
          "variable": "循环计数器",
          "value": "0"
        }
      },
      {
        "type": "operation.append",
        "targetProcess": { "key": "loop_proc" },
        "targetStep": { "key": "loop_step" },
        "operation": {
          "key": "inc_counter",
          "kind": "variable.add",
          "name": "计数器加一",
          "variable": "循环计数器",
          "amount": 1
        }
      },
      {
        "type": "operation.append",
        "targetProcess": { "key": "loop_proc" },
        "targetStep": { "key": "loop_step" },
        "operation": {
          "key": "check_counter",
          "kind": "branch.number_compare",
          "name": "判断循环是否结束",
          "variable": "循环计数器",
          "comparison": "gte",
          "compareValue": 10,
          "whenTrue": { "operationKey": "end_loop" },
          "whenFalse": { "operationKey": "inc_counter" }
        }
      },
      {
        "type": "operation.append",
        "targetProcess": { "key": "loop_proc" },
        "targetStep": { "key": "loop_step" },
        "operation": {
          "key": "end_loop",
          "kind": "flow.end",
          "name": "结束循环"
        }
      }
    ],
    "variables": [
      { "name": "循环计数器", "scope": "public", "type": "double", "value": "0", "policy": "reuse" }
    ]
  }
}
```

设计要点：

- `variable.add` 放在 `branch.number_compare` 之前，确保第 N 次累加完成后再做判断。
- 循环次数为 N 时，比较值填 N；若先判断后累加，则比较值和初始值需要相应调整。
- `whenTrue`/`whenFalse` 使用 `operationKey` 指向同一 ChangeSet 内定义的指令 `key`。
- 变量必须在 `variables` 中声明；已存在的变量使用 `"policy": "reuse"`。
- **禁止省略字段**：`process`/`step`/`operation` 都必须带 `key` 和 `name`；`variables` 每项必须带 `name`、`scope`、`type`、`policy`。

### Q2：循环流程常见反例是什么？

A2：以下写法会导致反复预演失败，禁止这样做：

```json
{
  "changeSet": {
    "actions": [
      { "type": "process.create", "process": { "autoStart": false, "disable": false } }
    ],
    "variables": [{ "scope": "public", "type": "double", "value": "0", "policy": "create" }]
  }
}
```

错误点：

1. `process.create` 缺少 `name` 和 `key`。
2. 没有 `step.append`。
3. 没有 `operation.append`。
4. `variables` 中的对象缺少 `name`（如写成 `{ "scope": "public", "type": "double", "value": "0", "policy": "create" }`）。
5. 不构成循环。

看到 `preview_change_set` 返回错误时，**不要**把结构简化成空壳，而应回到本主题 Q1 的完整骨架补齐字段。

### Q3：循环里的计数器自增，用 `variable.add` 还是 `variable.compute`？

A3：

- **固定数值累加**（如每次 +1）：用 `variable.add`，`amount` 填固定值，最简洁。
- **两个变量相加减、乘除、取模，或结果写入另一个变量**：用 `variable.compute`，指定 `sourceVariable`、`operator`、`operandValue`/`operandVariable`、`outputVariable`。

不要在 `variable.set` 里写算式或变量引用表达式；运行时值计算必须通过 `variable.add` 或 `variable.compute`。

### Q4：循环流程应该用语义指令还是原生指令（如 `逻辑判断`、`跳转`、`修改变量`）？

A4：**优先用语义指令**。计数循环、条件分支、固定延时、IO 等待等常见模式都有对应的语义 `kind`，参数更严格、行为更可预测。

只有在语义层确实无法表达当前控制流（例如需要保留某个特殊原生 `operaType` 的精确字段或运行时联动）时，才退回到 `native.operation`。反复预演失败后不要本能地转向原生指令；更常见的原因是变量未声明、`operationKey` 写错或 `branch.*` 目标为空对象。

### Q5：`preview_change_set` 反复提示变量不存在，如何处理？

A5：循环中使用的变量必须在 `changeSet.variables` 中声明，或确保它是平台上已存在的变量。未声明的变量即使被 `variable.set` 赋值，预演时也会被判定为缺失。流程局部变量使用 `"scope": "process"` 并指定 `ownerProcess`；跨流程或 HMI 共享状态使用 `"scope": "public"`。

### Q6：`branch.number_compare` 的 `whenTrue`/`whenFalse` 可以写空对象表示“继续下一条”吗？

A6：不可以。空对象 `{}` 不是顺序执行语义。需要继续到下一条时，显式填写下一条指令的 `operationKey`；需要结束流程时，指向一个 `kind: "flow.end"` 的指令。暂时无法确定目标时，可省略该字段并允许保存为 incomplete，但不要在分支中填 `{}`。

### Q7：`flow.goto` 与 `branch.*` 的跳转有什么区别？

A7：`branch.*` 本身携带条件判断和双路跳转，适合循环出口和条件分支；`flow.goto` 用于无条件跳转到指定指令。**计数循环不需要 `flow.goto`**，因为 `branch.number_compare` 的 `whenFalse` 已经能跳回循环体开头。只有无条件循环头或特定调度场景才需要 `flow.goto`。

### Q8：跨步骤跳转时 `OperationTarget` 如何填写？

A8：当前步骤内跳转只需提供 `operationKey`；跨步骤时附加 `stepKey`（同一阶段新步骤）或 `stepId`（已提交步骤）。不要只用 `stepKey` 而遗漏 `operationKey`。`flow.goto` 和 `branch.*` 的目标都是**指令级目标**，不是步骤级目标。

### Q9：循环里如何加入超时或有限重试退出？

A9：在计数循环基础上再增加一个计数器（如 `重试计数器`），每次循环体失败时累加，达到上限后跳转到 `flow.end` 或异常处理指令。结构如下：

```text
variable.set 重试计数器 = 0
→ 执行循环体动作
→ 判断动作是否成功
   ├─ 成功 → 结束循环
   └─ 失败 → variable.add 重试计数器 +1
            → branch.number_compare 重试计数器 >= 上限
               ├─ true → 异常处理 / flow.end
               └─ false → 跳回“执行循环体动作”
```

实现要点：

- 用两个 `branch.number_compare` 分别控制“业务成功/失败”和“重试次数耗尽”。
- 超时场景用 `io.wait` 的 `timeoutMs` + `onFailure` 目标，或在循环内用 `wait` 累计时间后判断。
- 重试次数和超时二者都出现时，任一条件满足即退出循环，避免无限重试。

### Q10：`preview_change_set` 报 `variables[0].name 不能为空` 怎么办？

A10：`changeSet.variables` 数组的每一项必须包含 `name`、`scope`、`type`、`policy` 四个字段。最常见错误是只写了：

```json
{ "scope": "public", "type": "double", "value": "0", "policy": "create" }
```

正确写法：

```json
{ "name": "循环计数器", "scope": "public", "type": "double", "value": "0", "policy": "reuse" }
```

如果变量已经存在，`policy` 用 `"reuse"`；如果确定要新建且不存在才能保存，`policy` 用 `"create"`。`value` 可省略，但 `name` 绝对不能省略。

### Q11：`preview_change_set` 报操作指令缺少字段怎么办？

A11：每个 `operation.append` 中的 `operation` 对象至少包含 `key`、`kind`、`name`：

```json
{
  "key": "init_counter",
  "kind": "variable.set",
  "name": "初始化计数器",
  "variable": "循环计数器",
  "value": "0"
}
```

`key` 是同一 ChangeSet 内跳转的目标身份；`kind` 是指令类型；`name` 是指令显示名。不要只写 `kind` 和业务字段。

<!-- faq:loop:end -->
