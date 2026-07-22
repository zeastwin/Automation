# 流程与指令模型设计

## 目标

流程模型只保存能够独立决定运行语义的业务事实。Inspector、JSON、AI Schema、编译器、就绪校验和运行时从同一模型投影，不在模型中保存仅供界面使用的派生状态。

## 字段设计规则

1. 集合是子项数量的唯一事实源。模型保存数组，不再保存 `Count`、`IOCount`、`ProcCount`、`ReadItemCount` 等镜像字段。
2. 固定数值使用 .NET 数值类型。需要表达“未填写”时使用可空数值；需要在固定值和变量之间选择时使用带明确 discriminator 的值来源对象，不使用字符串数值或负数哨兵拼接协议。
3. 资源引用使用稳定 ID 或精确名称，并由 Schema 标记 `referenceType`。物理索引只用于当前显示或运行时编译结果，不作为跨阶段编辑身份。
4. 有限模式使用枚举作为模型事实。枚举成员使用稳定英文标识，中文名称只由 Inspector 展示层提供。
5. 公共字段使用 PascalCase，字段名在继承树内按不区分大小写比较也必须唯一。`Id` 只表示流程、步骤或指令的稳定身份。
6. 属性 setter 只提交该属性事实，并对本属性的确定性范围进行校验。集合调整、UI 可见性和资源候选刷新由编辑层完成。
7. 保存校验只检查 `saveRequired`、结构和确定性不变量；运行资源与动态值由 `ProcessReadinessService` 和运行闸门检查。

## 集合契约

- `InlineListAttribute` 是集合编辑元数据与真实数量边界的单一来源。
- Inspector 的数量输入直接调整集合，增加项时使用对应元素的模型默认值。
- `StructuredOperationCompiler` 将集合投影为 JSON 数组，并把真实边界投影为 `minItems/maxItems`。
- AI 只提交数组内容。数组长度天然决定数量，更新后不需要再同步任何数量字段。
- 当前只有 PLC 按项读写存在已经由协议和运行时证明的 `1..100` 边界；其他集合不增加任意上限。

## AI 编写与理解流程

- 普通业务目标优先使用语义 `kind`；语义层不能准确表达时读取精确原生 Schema。
- 原生数组字段直接表达完整子项，不暴露 UI 数量控件或派生数量。
- 业务跳转使用 `TrueGoto/FalseGoto/DefaultGoto` 等明确名称；报警分支保留 `Goto1/Goto2/Goto3`，不存在仅靠大小写区分的字段。
- 通讯资源字段使用 `ConnectionName`，与指令稳定身份 `Id` 分离，并通过 `referenceType` 引导精确资源读取。
- Schema 中仍为字符串的数值外观字段分为两类：资源/变量引用保持引用语义；固定值与变量双来源字段后续应整体升级为 discriminator 对象，不能机械改成 `int` 后丢失动态表达能力。
- `OperationDefinitionRegistry` 在注册时递归校验顶层字段、内联对象和数组元素字段：名称必须为 PascalCase，不允许仅大小写不同的字段，数组必须初始化且满足边界，每个注册指令必须具有专用行为契约。
- `OperationBehaviorCatalog` 已覆盖全部 47 种注册指令，并只为 TCP、串口和 PLC 数据通信指令投影固定间隔重试及接收结果判定。AI 读取原生 Schema 时同时得到用途、执行顺序、控制流、通信失败分类、条件字段、约束和失败模式，不需要从属性名猜测运行语义。
- 指令显示名 `Name` 只承担显示和日志定位。自定义函数使用 `FunctionName`，插入结构体数据项使用 `ItemName`，两者均作为可配置业务字段出现在 AI Schema 中。
- 调试断点 `IsBreakpoint` 是编辑器维护状态，不进入 AI 原生指令字段。

## 严格配置边界

- 固定延时使用可空整数 `DelayMs`；结构体、结构项、字段索引以及字符串位置/数量使用 `int`/`int?`。这些字段由 JSON 类型和 `NumericRangeAttribute` 校验，不在运行时重复解析字符串。
- 已强类型化的固定延时与延时变量仍是两个真实来源字段，并严格拒绝同时配置；其他双来源字段按指令整体迁移为 `ValueSource<T>` 时统一收口。
- 流程文件和指令剪贴板使用 `MissingMemberHandling.Error`。字段重命名后不保留旧 JSON 别名，旧字段会使配置加载失败并进入现有流程配置故障报警链，不会被静默忽略后以默认值运行。
- `GetDataStructItem` 的单字段模式直接写入 `OutputValueIndex/OutputValueName` 指向的变量，不再把目标变量当前值解释成第二个变量名。

## 全量摸查结论

| 类型 | 当前决策 | 理想权威来源 |
|---|---|---|
| 列表数量镜像 | 已删除 | 集合 `Count` |
| PLC 按项数量边界 | 已统一 | `InlineListAttribute` 的 `1..100` |
| 业务跳转与报警跳转大小写冲突 | 已消除 | 明确命名的跳转属性 |
| 通讯 `ID` 与稳定 `Id` 冲突 | 已消除 | `ConnectionName` 与基类 `Id` |
| 非负固定数值字符串 | 已改为 `int`/`int?` 并投影最小值 | 强类型属性与 `NumericRangeAttribute` |
| 小写、拼写错误和含混业务字段 | 已统一且注册时强制检查 | PascalCase 明确业务名 |
| `Name` 被业务参数复用 | 已拆分 | `Name`、`FunctionName`、`ItemName` 各自唯一语义 |
| 原生指令行为缺失 | 47 种指令已全部覆盖 | `OperationBehaviorCatalog` |
| 固定值/变量双来源 | 保留现有运行能力，后续整体替换 | `ValueSource<T>` discriminator |
| 变量的名称/索引并行字段 | 保留现有运行能力，后续整体替换 | 强类型 `ValueReference` |
| 中文字符串模式 | 后续按行为 Catalog 逐类替换 | 稳定枚举 + 中文显示元数据 |
| setter 修改 `BrowsableAttribute` | 后续迁移到 Inspector 条件投影 | `IPropertyVisibilityProvider`/行为 Catalog |

后四类必须按单个指令的模型、行为 Catalog、Schema、编译器、就绪校验和运行时一起迁移，不应通过批量字符串替换制造另一套不完整契约。
