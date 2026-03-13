# AIFlow 流程编写辅助设计稿

## 1. 目标与边界

### 1.1 目标
- 为 `Automation` 提供一套面向 LLM 的流程编写辅助能力。
- 允许用户通过自然语言描述修改意图，由 LLM 理解后读取流程、分析目标、生成结构化修改，再由 `Automation` 完成保存与发布。
- 保持 `MCP Server` 为纯转发层，不承载流程业务逻辑。

### 1.2 当前范围
- `Dify` 为本地部署。
- `MCP Server` 只做协议适配与数据转发。
- `Automation` 承担全部业务能力：流程读取、结构转换、Schema 导出、Patch 预演、保存、发布。
- 当前阶段先不处理显式授权、独立 token、操作审计、审批确认等安全能力，后续再补。

### 1.3 非目标
- 不让 LLM 直接编辑 `Config/Work/*.json` 原始文件。
- 不让 `MCP Server` 直接访问或修改流程文件。
- 不让写接口直接接收自然语言。
- 不把整份原始流程 JSON 作为 LLM 的读写协议。

## 2. 现有实现锚点

### 2.1 核心模型
- 流程模型为 `Proc -> Step -> OperationType`。
- `Proc`、`ProcHead`、`Step` 定义位于 `FrmProc.cs`。
- `OperationType` 及全部指令子类定义位于 `OperationType.cs`。

### 2.2 指令全集入口
- 全部可编辑指令类型当前由 `FrmPropertyGrid.OperationTypeList` 统一注册。
- 该注册表可以作为流程辅助工具的“指令目录”唯一来源。

### 2.3 属性元数据入口
- `OperationType` 与各指令子类已广泛使用：
  - `DisplayName`
  - `Description`
  - `Category`
  - `ReadOnly`
  - `Browsable`
  - `TypeConverter`
  - `MarkedGoto`
- 这套元数据可直接生成 LLM 可编辑 Schema，无需手工维护第二份配置。

### 2.4 现有发布链路
- 流程归一化入口：`FrmProc.NormalizeProc`
- 跳转校验入口：`FrmProc.ValidateProcGotoTargets`
- 单流程保存与发布入口：`FrmDataGrid.SaveSingleProc`
- 运行态发布入口：`SF.PublishProc`
- 文件序列化入口：`FrmMain.SaveAsJson`

### 2.5 设计原则
- 所有写操作必须复用现有发布链路。
- LLM 读模型、写模型、发布模型必须分离。
- LLM 可以生成修改建议，但不能绕过 `Automation` 的结构归一化与发布校验。

## 3. 总体架构

## 3.1 组件分工
- `Dify Agent`
  - 负责理解用户意图。
  - 负责选择调用哪个工具。
  - 负责根据读取结果生成结构化 Patch。
- `MCP Server`
  - 负责暴露 MCP Tools。
  - 负责把 Tool 调用 1:1 转发给 `Automation Bridge`。
  - 不做流程校验、流程解析、文件读写、发布控制。
- `Automation Bridge`
  - 负责把 `Automation` 的流程能力包装成可调用 RPC。
  - 负责导出 LLM 可读 DTO、Schema、Reference Catalog。
  - 负责 Patch 预演、保存、发布。
- `Automation Core`
  - 维持原有流程编辑与运行逻辑。
  - 维持现有归一化、校验、保存、发布实现。

### 3.2 调用链
1. 用户输入自然语言修改意图。
2. `Dify Agent` 调用读取类 MCP 工具，获取流程摘要与细节。
3. `Dify Agent` 调用 Schema 类工具，获取目标指令可编辑字段与约束。
4. `Dify Agent` 生成结构化 Patch。
5. `Dify Agent` 调用 `preview_patch`。
6. `Automation Bridge` 预演修改并返回 Diff、归一化结果、校验错误。
7. 预演通过后，`Dify Agent` 调用 `apply_patch`。
8. `Automation Bridge` 保存并发布流程。

### 3.3 传输建议
- `Dify -> MCP Server`：HTTP MCP
- `MCP Server -> Automation Bridge`：优先 `Named Pipe`
- `Automation Bridge -> Automation Core`：进程内调用

选择 `Named Pipe` 的原因：
- 当前项目是 WinForms + .NET Framework 4.7.2，本机通信更适合走本地 IPC。
- `MCP Server` 只需做 RPC 转发，不需要和 `Automation` 共用 UI 线程与运行对象。

## 4. 三层协议模型

### 4.1 总体原则
- 对 LLM 的“读”使用 `Read Model`
- 对 LLM 的“写”使用 `Patch Model`
- 对 LLM 的“约束”使用 `Schema Model`

三者职责不同，禁止混用。

## 4.2 Read Model

### 4.2.1 ProcOverviewDto
用途：
- 让 LLM 快速定位目标流程、步骤、指令。
- 控制 token 规模。
- 提供流程搜索和候选排序所需的语义线索。

建议结构：

```json
{
  "procIndex": 0,
  "procId": "guid",
  "name": "上料流程",
  "disable": false,
  "autoStart": false,
  "stepCount": 3,
  "steps": [
    {
      "stepIndex": 0,
      "stepId": "guid",
      "name": "等待到位",
      "disable": false,
      "opCount": 4,
      "ops": [
        {
          "opIndex": 0,
          "opId": "guid",
          "operaType": "IoCheck",
          "name": "检测到位",
          "summary": "检测输入 IO 到位信号为 ON，超时 3000ms"
        }
      ]
    }
  ]
}
```

### 4.2.2 ProcDetailDto
用途：
- 让 LLM 在确定目标后查看完整可编辑结构。
- 该 DTO 是写接口的输入基础，但不等于内部模型或文件格式。

建议结构：

```json
{
  "procIndex": 0,
  "procId": "guid",
  "head": {
    "name": "上料流程",
    "autoStart": false,
    "disable": false,
    "pauseIoParams": [],
    "pauseValueParams": []
  },
  "steps": [
    {
      "stepIndex": 0,
      "stepId": "guid",
      "name": "等待到位",
      "disable": false,
      "ops": [
        {
          "opIndex": 0,
          "opId": "guid",
          "num": 0,
          "operaType": "IoCheck",
          "name": "检测到位",
          "alarmType": "报警停止",
          "alarmInfoId": "",
          "goto1": null,
          "goto2": null,
          "goto3": null,
          "note": "",
          "isStopPoint": false,
          "disable": false,
          "fields": {
            "IOName": "到位信号",
            "ExpectStatus": true,
            "TimeOut": 3000
          },
          "summary": "检测输入 IO 到位信号为 ON，超时 3000ms"
        }
      ]
    }
  ]
}
```

### 4.2.3 Read Model 规则
- 必须带稳定标识：
  - `procIndex`
  - `procId`
  - `stepId`
  - `opId`
- 不直接暴露内部序列化用的 `$type`
- 不直接暴露 WinForms PropertyGrid 专用表现字段
- `goto` 推荐转成结构化对象，不直接给字符串 `proc-step-op`
- 每条指令都要有一条机器生成的 `summary`

### 4.2.4 Summary 生成规则
- `summary` 由 `Automation Bridge` 通过模板生成，不由 LLM 生成。
- 目的不是完全还原业务语义，而是帮助 LLM 检索目标。
- 模板生成至少包含：
  - 指令类型
  - 核心对象
  - 核心参数
  - 超时/跳转/报警等关键行为

示例：
- `IoCheck`：检测输入 IO `{IOName}` 为 `{状态}`，超时 `{TimeOut}ms`
- `WaitTcp`：等待 TCP 通道 `{TcpName}` 收到消息，超时 `{TimeOut}ms`
- `ModifyValue`：把变量 `{ValueName}` 按 `{ModifyType}` 修改为 `{InputValue}`

## 4.3 Schema Model

### 4.3.1 目标
- 告诉 LLM 某类指令“能改什么、怎么改、哪些值合法”。
- 避免模型猜字段名、猜取值、猜显隐条件。

### 4.3.2 OperationSchemaDto

```json
{
  "operaType": "IoCheck",
  "displayName": "IO检测",
  "description": "等待或检测指定输入状态，在超时或异常时按报警策略处理。",
  "fields": [
    {
      "fieldKey": "IOName",
      "displayName": "IO名称",
      "description": "要检测的输入 IO 名称。",
      "dataType": "string",
      "required": true,
      "readOnly": false,
      "enumValues": ["到位信号", "夹紧信号"],
      "referenceType": "io.input",
      "visible": true
    },
    {
      "fieldKey": "TimeOut",
      "displayName": "超时",
      "description": "检测等待超时时间，单位毫秒。",
      "dataType": "int",
      "required": true,
      "readOnly": false,
      "enumValues": [],
      "referenceType": null,
      "visible": true
    }
  ]
}
```

### 4.3.3 Schema 生成来源
- 指令类型来源：`FrmPropertyGrid.OperationTypeList`
- 字段来源：反射当前指令实例的可编辑属性
- 字段展示名来源：`DisplayName`
- 字段说明来源：`Description`
- 字段只读性来源：`ReadOnly`
- 字段显隐来源：`Browsable`
- 枚举候选来源：`TypeConverter.GetStandardValues`
- 跳转字段识别来源：`MarkedGoto`

### 4.3.4 Schema 生成方式
- 必须基于“真实指令实例”生成，而不是只看静态类型。
- 原因：
  - 某些字段是否显示由运行中属性控制。
  - 例如 `AlarmType` 会动态影响 `AlarmInfoID/Goto1/Goto2/Goto3` 的可见性。
- 正确做法：
  - 创建指令实例
  - 写入当前字段值
  - 刷新动态属性
  - 再通过 `TypeDescriptor` 导出最终可见字段集合

### 4.3.5 字段类型映射
- `string` -> `string`
- `bool` -> `boolean`
- `int/long` -> `integer`
- `double/float/decimal` -> `number`
- `Guid` -> 仅标识字段，不暴露给业务编辑
- `List<T>` -> `array`
- `MarkedGoto` -> `gotoTarget`
- 带 `TypeConverter` 且有标准值的字段 -> `enum`
- 指向变量、IO、工站、通讯、PLC 的字段 -> `reference`

## 4.4 Reference Catalog

### 4.4.1 目标
- 告诉 LLM 当前流程上下文里有哪些合法引用对象。
- 避免模型凭空创造变量名、IO 名、工站名、通讯名。

### 4.4.2 建议导出内容
- `valueNames`
- `ioInputNames`
- `ioOutputNames`
- `stationNames`
- `stationPointNamesByStation`
- `tcpChannelNames`
- `serialPortNames`
- `plcDeviceNames`
- `dataStructNames`
- `alarmInfoIds`

### 4.4.3 获取方式
- 优先直接复用各 Store 或现有窗体缓存对象。
- 如果字段本身已有 `TypeConverter` 且候选来自运行时对象，则以 `TypeConverter` 导出的值为准。

## 5. Patch Model

### 5.1 设计目标
- 写接口只接收结构化动作，不接收自由文本。
- 动作必须可审计、可复现、可预演、可回放。
- 动作必须以稳定标识为目标，不依赖名称唯一性。

### 5.2 PatchEnvelope

```json
{
  "procIndex": 0,
  "baseProcId": "guid",
  "actions": [
    {
      "type": "update_operation_fields",
      "stepId": "guid",
      "opId": "guid",
      "expectedOperaType": "IoCheck",
      "fieldChanges": {
        "TimeOut": 5000
      }
    }
  ]
}
```

### 5.3 MVP 动作集
- `update_proc_head_fields`
- `update_step_fields`
- `append_step`
- `insert_step`
- `delete_step`
- `move_step`
- `append_operation`
- `insert_operation`
- `update_operation_fields`
- `delete_operation`
- `move_operation`

### 5.4 动作说明

#### `update_proc_head_fields`
- 修改流程头字段。
- 允许字段：
  - `Name`
  - `AutoStart`
  - `Disable`

#### `update_step_fields`
- 修改步骤字段。
- 当前允许字段：
  - `Name`
  - `Disable`

#### `append_step`
- 在流程末尾追加新步骤。

#### `insert_step`
- 在指定索引插入新步骤。
- 必须带：
  - `insertIndex`

#### `delete_step`
- 删除指定步骤。
- 必须带：
  - `stepId`

#### `move_step`
- 在同一流程内移动步骤位置。
- 必须带：
  - `stepId`
  - `targetIndex`
- `targetIndex` 表示移除源步骤后的最终索引。

#### `insert_operation`
- 在指定步骤的指定索引插入新指令。
- 必须带：
  - `stepId`
  - `insertIndex`
  - `operaType`
  - `fieldValues`

#### `append_operation`
- 在指定步骤末尾追加指令。

#### `update_operation_fields`
- 修改现有指令字段。
- 必须带：
  - `stepId`
  - `opId`
  - `expectedOperaType`
  - `fieldChanges`

#### `delete_operation`
- 删除指定指令。

#### `move_operation`
- 支持同一步骤内移动，也支持跨步骤移动。
- 必须带：
  - `stepId`
  - `opId`
  - `targetIndex`
- 可选带：
  - `targetStepId`
- `targetIndex` 表示从源步骤移除当前指令后的最终索引。

### 5.5 Patch 约束
- 每次 Patch 仅允许操作一个流程。
- 每个动作必须有目标标识。
- `expectedOperaType` 必须参与校验，防止目标漂移。
- 不允许 LLM 直接提交完整流程树替换。
- 不允许使用名称作为唯一定位主键。
- `delete/move/insert` 这类结构化动作由 `Automation Bridge` 负责自动重写同流程内跳转地址。

## 6. Bridge RPC 设计

### 6.1 MCP Tools 到 Bridge RPC 的映射
- `list_procs` -> `ListProcs`
- `get_proc_overview` -> `GetProcOverview`
- `get_proc_detail` -> `GetProcDetail`
- `get_operation_schema` -> `GetOperationSchema`
- `get_reference_catalog` -> `GetReferenceCatalog`
- `preview_patch` -> `PreviewPatch`
- `apply_patch` -> `ApplyPatch`

### 6.2 MCP 层职责
- 参数反序列化
- RPC 请求转发
- 超时控制
- 统一错误映射
- correlationId 日志透传

### 6.3 MCP 层禁止事项
- 禁止直接读取流程文件
- 禁止修改流程对象
- 禁止复刻归一化规则
- 禁止复刻跳转校验
- 禁止直接发布流程

## 7. 预演与提交流程

### 7.1 PreviewPatch
执行步骤：
1. 读取当前流程对象
2. 深拷贝流程
3. 对副本应用 Patch
4. 运行 `NormalizeProc`
5. 运行跳转地址校验
6. 生成 Diff
7. 返回预演结果

返回内容建议包含：
- `success`
- `normalizedProcDetail`
- `diffSummary`
- `diffActions`
- `validationErrors`
- `warnings`

### 7.2 ApplyPatch
执行步骤：
1. 重新读取当前流程
2. 校验 `procIndex/baseProcId`
3. 重新应用同一份 Patch
4. 重新归一化与校验
5. `SaveAsJson`
6. `PublishProc`
7. 返回最终结果

### 7.3 为什么不直接从 Preview 结果落盘
- 预演和提交之间可能有流程被其他编辑修改。
- 重新读、重新应用、重新校验更符合当前系统实际行为。

## 8. 错误模型

### 8.1 错误设计目标
- 错误必须足够精确，便于 LLM 自动修复。
- 禁止只返回“失败”。

### 8.2 错误分类
- `PROC_NOT_FOUND`
- `STEP_NOT_FOUND`
- `OP_NOT_FOUND`
- `OP_TYPE_MISMATCH`
- `FIELD_NOT_EDITABLE`
- `FIELD_VALUE_INVALID`
- `REFERENCE_NOT_FOUND`
- `GOTO_INVALID`
- `PROC_RUNNING_NOT_ALLOWED`
- `PUBLISH_FAILED`
- `INTERNAL_ERROR`

### 8.3 错误返回建议

```json
{
  "success": false,
  "errorCode": "FIELD_VALUE_INVALID",
  "message": "字段 TimeOut 值无效，必须为大于 0 的整数。",
  "target": {
    "procIndex": 0,
    "stepId": "guid",
    "opId": "guid",
    "fieldKey": "TimeOut"
  },
  "repairHints": [
    "请先读取该指令的 operation schema。",
    "请把 TimeOut 改为正整数毫秒值。"
  ]
}
```

## 9. LLM 调用约束

### 9.1 系统提示词必须固定的工作流
1. 不假设流程名、步骤名、指令名唯一。
2. 定位目标前必须先调用 `list_procs` 或 `get_proc_overview`。
3. 修改前必须先读取 `get_proc_detail`。
4. 修改某类指令前必须读取 `get_operation_schema`。
5. 写操作只允许使用 `preview_patch` 和 `apply_patch`。
6. 写接口只接受结构化 Patch，不接受自然语言。
7. 预演失败时必须根据错误信息修正后再重试。

### 9.2 LLM 不应承担的职责
- 不负责猜测隐藏字段
- 不负责猜测枚举候选
- 不负责猜测变量名、IO 名、工站名
- 不负责自己推导跳转字符串格式
- 不负责直接生成可落盘的原始 JSON

## 10. 实施顺序

### 10.1 第一阶段：最小闭环
- `Automation Bridge`
  - `ListProcs`
  - `GetProcDetail`
  - `GetOperationSchema`
  - `PreviewPatch`
  - `ApplyPatch`
- `MCP Server`
  - 1:1 转发上述 RPC
- `Dify Agent`
  - 固定调用顺序提示词

### 10.2 第二阶段：提升检索准确率
- 增加 `ProcOverviewDto`
- 增加 `Reference Catalog`
- 增加指令 `summary`
- 增加基于语义的目标候选排序

### 10.3 第三阶段：提升编辑成功率
- 扩展 Patch 动作集
- 增加更细粒度的 `diffSummary`
- 增加自动修复提示
- 增加运行态约束与安全控制

## 11. 当前版本的取舍

### 11.1 当前先不做
- AI 编辑显式授权
- 独立 token
- 审批确认
- 审计日志
- 多用户并发编辑冲突处理

### 11.2 当前必须做
- Read Model
- Schema Model
- Patch Model
- Preview/Apply 双阶段
- 复用现有归一化、保存、发布链路

## 12. 结论
- 这套流程编写辅助工具的核心，不是让 LLM 直接“写流程文件”，而是让 LLM 在受约束的结构化协议上工作。
- `Cursor` 一类工具的核心方法论，在本项目中的落点是：
  - 用 `Read Model` 解决理解问题
  - 用 `Schema Model` 解决约束问题
  - 用 `Patch Model` 解决执行问题
  - 用 `Preview/Apply` 解决稳定性问题
- 只要 `Automation Bridge` 把上述三层协议实现出来，`Dify + MCP` 就可以成为一套可控的流程编写辅助工具，而不是一个直接改配置文件的高风险聊天机器人。
