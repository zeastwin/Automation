# 流程引擎源码导航

`Engine/` 负责 `Proc -> Step -> OperationType` 的定义校验、编辑变换、编译、运行绑定和确定性执行。它不依赖 WinForms，也不负责配置文件存储或页面交互。

| 目录 | 主要入口 | 职责 |
| --- | --- | --- |
| `Models/` | `Proc`、`Step`、`ValueRef` | 引擎内部流程结构与值引用模型 |
| `Definitions/` | `OperationDefinitionRegistry`、`OperationBehaviorCatalog` | 原生指令类型、字段、行为和引用元数据的权威来源 |
| `Compilation/` | `AiChangeSetCompiler`、`StructuredOperationCompiler` | ChangeSet、语义指令和原生指令的严格编译 |
| `Editing/` | `OperationEditingService`、`ProcessEditingService` | 流程结构变换、跳转重写、发布门禁与变量生命周期 |
| `Validation/` | `ProcessDefinitionService`、`ProcessReadinessService` | 配置可保存性和流程可运行性校验 |
| `Execution/` | `ProcessEngine`、`ProcessRuntimeBinder` | 运行状态、绑定、调度以及各类指令执行 |
| `Extensibility/` | `CustomFunc` | 平台内部自定义函数注册与执行容器 |

## 放置规则

- `Definitions/` 描述确定性事实，`Compilation/` 消费这些事实生成模型，禁止维护第二份近似定义。
- `Validation/` 只判断结构与启动条件；实际运行语义位于 `Execution/`。
- 编辑变换必须保留稳定 ID、符号跳转和失败回滚边界。
- 新文件应进入明确子目录，`Engine/` 根目录只保留本导航文档。
