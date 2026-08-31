# 旧项目流程经验整理：AI 接管工作流

## 1. 任务目标

从 Automation 3.0 旧项目中提取真实流程证据，由 AI 负责审核、比较、归纳和工程化改写，最终只把已经可用于指导当前 Automation AI 写流程的规范放入可用流程规范库。

这不是旧项目迁移，也不是把旧流程转换成模板。旧项目只能证明“过去曾这样实现”，不能证明该实现正确、安全或适合当前平台。

最终链路固定为：

```text
旧项目只读证据 -> AI 审核与归纳 -> 可用规范 -> ProcessKnowledge -> get_process_design_guide
```

AI 是链路中的正式审核与整理环节，不要求用户手工逐条整理。证据不足或无法形成通用能力时，直接不收录；不要在 Automation 库中保存候选、审核中、废弃或占位内容。

## 2. 固定路径与职责

| 路径 | 职责 |
| --- | --- |
| `F:\Auto\Automation` | 当前 Automation 源码和最终可用规范 |
| `F:\Auto\Automation\McpServer\ProcessKnowledge` | 只保存审核后可用的规范 |
| `F:\Auto\Automation\McpServer\Guides\ProcessDesignGuide.md` | 当前通用流程设计 Guide |
| `F:\Auto\Transform4SNsdemo` | 旧项目只读抽取器 |
| `F:\Auto\Transform4SNsdemo\runs` | 原始证据、标准化案例和审核工作区，不是运行时知识库 |

旧项目源目录始终只读。不要启动旧平台，不要写回旧项目，不要把旧配置直接导入 Automation 流程。

当前已登记来源和证据摘要以 `ProcessKnowledge/provenance/sources.json` 为准；当前可用规范以 `ProcessKnowledge/catalog.json` 为准，不依赖聊天历史。

## 3. 新对话接管动作

新对话读完本文后按以下顺序执行：

1. 读取 `ProcessKnowledge/catalog.json`、相关 `blocks/*.md` 和 `provenance/sources.json`，了解已有能力，避免生成重复规范。
2. 确认用户指定的旧项目源目录。源目录不存在、缺少 `NsDemo` 或缺少真实 `bin\AnyCPU\Proc` 时，先报告证据缺口，不猜测流程。
3. 使用 Transform 抽取完整证据包；已有同一快照时先校验并复用，不重复生成。
4. 先做项目清单和风险扫描，再按功能主题读取相关标准化案例；不要一次把全部原始 JSON 塞进上下文。
5. AI 直接完成事实审查、跨项目比较和规范判断。只有确实形成稳定通用能力时才修改 ProcessKnowledge。
6. 先完善已有规范；只有出现新的单一可观察目标时才新增规范。
7. 运行知识库校验、MCP 编译和 Profile 校验，确认运行时只返回已登记规范。
8. 向用户报告吸收了什么、拒绝了什么、证据缺口和验证结果；不要用“导出成功”代替审核结论。

## 4. 抽取旧项目

默认使用已编译的只读抽取命令：

```powershell
& 'F:\Auto\Transform4SNsdemo\bin\Release\net8.0-windows\Transform4SNsdemo.exe' `
  --extract-knowledge-candidates `
  '<旧项目根目录>' `
  'F:\Auto\Transform4SNsdemo\runs\<项目标识>-knowledge' `
  'F:\Auto\Automation\McpServer\Guides\ProcessDesignGuide.md'
```

然后校验证据包：

```powershell
& 'F:\Auto\Transform4SNsdemo\bin\Release\net8.0-windows\Transform4SNsdemo.exe' `
  --validate-knowledge-candidates `
  'F:\Auto\Transform4SNsdemo\runs\<项目标识>-knowledge'
```

主要证据入口：

- `Tools/ProcessKnowledgeCuration/Get-KnowledgeDigest.ps1`：证据摘要器，把 normalized-cases 压缩为总览表（每案例一行）+ 指令序列明细（约 3% 体积）。甄别首选入口：先读总览选候选，按结构族去重，再对候选读明细；避免整包加载 JSON。
- `knowledge_candidates/manifest.json`：流程、步骤、指令、禁用数量、结构指纹和证据哈希。
- `knowledge_candidates/normalized-cases/*.json`：按流程保留步骤、指令顺序、类型、禁用、报警和跳转关系。摘要已覆盖甄别所需的结构语义；其中 FieldKeys 是按指令类型重复的键名清单，甄别不需要。
- `extracted_data.json`：需要核对具体历史字段值时按证据引用定点读取，不整包加载。
- `source_config/`：变量、结构、IO、报警、通讯和真实流程源文件快照。
- `code/`：自定义处理器和通讯语义的补充证据，不能覆盖真实流程 XML。

抽取器只有在无法读取一类真实证据、哈希或标准化结构有缺陷时才需要改造。不要为了某个旧项目的命名差异增加特定规则。

## 5. AI 审核方法

### 5.1 先建立事实清单

至少统计：

- 流程、Step、Operation、禁用指令和结构族数量；
- 主要指令类型、报警忽略、自动处理和跳转分布；
- 变量、数据结构、IO、报警、TCP、串口和 PLC 证据；
- 测试流程、明确标注无用的流程、超大单步骤和大量禁用区域；
- 缺少的运行日志、行为契约、现场安全验证或自定义处理器证据。

禁用、报警忽略和自动处理首先是配置事实，不自动等于缺陷；但在没有完成证据、安全授权和有限恢复边界时，不能作为推荐写法。

### 5.2 按功能能力分组

主分组使用“要完成什么”，例如身份绑定、工位交接、材料路径维护、校准、追溯提交。设备类型和工艺类型只是辅助标签，不能成为唯一目录结构。

一个可收录功能块应满足：

- 只有一个可观察目标；
- 有明确适用和不适用边界；
- 输入、前置条件和副作用可界定；
- 命令与完成反馈分开；
- 失败、超时、恢复和幂等可说明；
- 可按当前 Automation 资源和 Schema 重新绑定；
- 不依赖客户名、固定地址、固定槽位、固定次数或旧跳转坐标。

### 5.3 甄别，而不是照抄

可以吸收：职责划分、阶段边界、状态所有权、动作反馈、事务终态、恢复条件和可观察性。

通常拒绝：

- 客户、机台和工位专有名称；
- IP、端口、IO 地址、变量索引、点位和旧指令 ID；
- 固定槽位数量、固定重试次数和未经验证的固定时间；
- 深层跳转、超大单步骤、空步骤和测试流程结构；
- 禁用分支、报警忽略、自动处理和弹框跳转的默认用法；
- 只发命令、不确认终态；
- 只因多个项目重复出现就认定正确。

跨项目重复只能提高比较价值。单项目也可以形成规范，但必须能用当前工程不变量独立证明其合理性，并明确证据局限。

## 6. 写入可用规范库

库保持最小结构：

```text
ProcessKnowledge/
├── schema.json
├── catalog.json
├── blocks/*.md
├── provenance/sources.json
└── Validate-ProcessKnowledge.ps1
```

### 6.1 优先修改已有规范

新证据与已有 `patternId` 目标一致时：

1. 更新对应 Markdown 中真正新增或修正的通用约束；
2. 在 `catalog.json` 的 `sourceRefs` 增加精确案例引用；
3. 必要时补充设备、工艺和风险标签；
4. 在 `provenance/sources.json` 登记来源和总体甄别结论；
5. 不建立“同一能力的某设备版”副本，差异先写入“当前事实与适配”。

### 6.2 新增规范

只有已有规范不能表达一个新的可观察目标时才新增。`catalog.json` 条目必须符合 `schema.json`，Markdown 必须包含：

- `## 可观察目标`
- `## 适用边界`
- `## 当前事实与适配`
- `## 参考阶段`
- `## 完成证据`
- `## 失败、超时与恢复`
- `## 反模式`（校验脚本强制；4~5 条"别这样做 + 后果 + 正确做法"，从来源项目的甄别结论与失败模式提炼，不写泛泛通用条目）
- `## 幂等与甄别结论`

块内出现定量建议（超时、重试、容量、周期）时必须标注"典型起步值，现场定型"，禁止照抄历史数值；参考格式见 `variables.design` 的"定量参考惯例"与 `data-struct.design` 的"容量与周期惯例"。

设备框架块（`device-frame.*`，topics 固定 `composition`）使用独立结构，回答"针对某种设备怎么搭流程框架"：

- `## 设备画像`
- `## 功能单元构成`（单元/职责/典型流程/引用功能块表）
- `## 单元间衔接`
- `## 框架变化点`
- `## 搭建顺序`
- `## 完成证据`
- `## 关联块清单`（校验脚本强制；patternId 列表构成"任务包"——运行时 AI 钻取该框架后按清单一次拉全关联块；必须包含数据设计层 `variables.design`、`data-struct.design`、`custom-function.code-process-collaboration`、`observability.design`）
- `## 反模式`（框架级搭建陷阱：单元漏建、顺序错误、形态混用、复制漂移）
- `## 黄金样例`（可选但强烈建议；见下）
- `## 幂等与甄别结论`

黄金样例固定格式：来源项目名引言（声明"只取结构与顺序，参数按当前项目重建"）→ 流程族清单（按单元归组并标注单元映射，测试残留流程显式标注不吸收）→ 关键流程骨架表（步骤/职责/为什么这样排）→ 历史反面证据标注（如双实现并存、NULL 流程）。素材从证据包 `extracted_data.json` 的 `Processes` 提取流程名与步骤标签，配合 `normalized-cases` 核对；黄金样例的作用是给运行时 AI 提供"写好的长什么样"，与反模式（别做成什么样）互补。

设备框架只针对单台设备，多机台产线编排不收录。新机型先核对已有框架的变化点，确实表达不了才新增 `device-frame` 条目；同一机型的差异优先写进"框架变化点"，不建机型副本。立框架的形态门槛：该形态至少有独立且可互证的来源（单包弱证据形态先等第二例），框架数量保持小——模板膨胀会稀释任务包精准性。形态去重口径：同型号多台机/多版本 = 1 个型号，同形态型号 = 1 个框架。

正文只写可用规范，不写审核状态、工作计划、旧项目参数或待办。`sourceRefs` 和内部来源摘要负责可信回查，但 `get_process_design_guide` 不向运行时 AI 返回它们。

### 6.3 不收录的处理

不合格内容不在 Automation 库中建文件、不登记状态、不留占位。原始证据继续保存在 Transform 运行目录；需要回查时按哈希和案例 ID 定位。删除或替代现有规范时直接修改目录，历史由 Git 保存。

## 7. 如何在 Automation 中生效

`McpServer/ProcessKnowledgeCatalog.cs` 读取内嵌 `catalog.json` 和 `blocks/*.md`。`ProcessDesignGuideCatalog.Get(topics, detail, patternIds)` 按主题筛选后，把可用规范放入 `get_process_design_guide` 返回的 `knowledgeBlocks`；compact 投影携带功能块标准小节和设备框架的功能单元表等小节，`patternIds` 支持按块钻取，库变大时不放大单次返回。

新增普通规范时不需要新增 MCP 工具，也不需要修改 Prompt 或 Skill。通常委托只修改 `catalog.json`、对应 Markdown 和来源摘要；项目文件使用通配符嵌入规范正文。

只有新增了现有 `topics` 无法表达的一级主题时，才同步修改主题 Schema、Guide 区块、工具描述、Profile 自检和架构说明。不要为了一个设备或工艺名称扩展一级主题。

## 8. 完成门禁

每次变更至少执行：

```powershell
& '.\McpServer\ProcessKnowledge\Validate-ProcessKnowledge.ps1'
dotnet build '.\McpServer\McpServer.csproj' -c Debug --no-restore
dotnet '.\McpServer\bin\Debug\net8.0-windows\Automation.McpServer.dll' --verify-profile
```

必须同时满足：

- `catalog.json` 符合 Schema，ID、文件和来源引用唯一且存在；
- 每份 Markdown 章节完整，不含中间状态和典型旧项目参数；
- `blocks/` 没有未登记文件；
- MCP 可以加载全部内嵌规范，并只按请求主题返回；
- 旧写入链、ChangeSet V2 和现有工具 Profile 不受影响；
- 源旧项目保持只读，Automation 工作树中的无关用户改动没有被覆盖。

## 9. 当前基线

当前已收录的规范清单、主题标签和来源引用以 `ProcessKnowledge/catalog.json` 为准，本文不复述（避免清单与目录漂移）；已登记来源和甄别结论见 `provenance/sources.json`。

新项目应先判断它是强化已有规范、暴露规范缺陷，还是形成真正的新能力。不要以“每个项目至少产出几个规范”为目标；零新增也是有效审核结果。

## 10. 新对话测试用句

### 完整导入和审核

> 先完整阅读 `F:\Auto\Automation\McpServer\ProcessKnowledge\AI-Curation-Workflow.md`，然后只读抽取并审核旧项目 `<项目路径>`。你负责完成证据甄别，只把审核后真正可用的规范写入库；优先完善已有规范，不保留候选和占位内容，最后完成全部门禁验证。

### 只评估，不修改库

> 先阅读 `F:\Auto\Automation\McpServer\ProcessKnowledge\AI-Curation-Workflow.md`。只读评估旧项目 `<项目路径>` 中有哪些经验值得吸收、哪些不应吸收，给出精确案例证据和与现有 5 个规范的关系；这次不要修改任何文件。

### 继续处理已有证据包

> 先阅读 `F:\Auto\Automation\McpServer\ProcessKnowledge\AI-Curation-Workflow.md`，继续审核证据包 `F:\Auto\Transform4SNsdemo\runs\<项目标识>-knowledge`。不要重新抽取；直接完成分组、甄别和规范归纳，合格内容才进入 ProcessKnowledge。

### 检查知识库是否被污染

> 先阅读 `F:\Auto\Automation\McpServer\ProcessKnowledge\AI-Curation-Workflow.md`，审查当前 ProcessKnowledge 是否存在重复规范、旧项目参数、中间状态、过细分类或与 ProcessDesignGuide 冲突的事实。只报告证据和建议，不修改文件。

### 验证运行时召回

> 先阅读 `F:\Auto\Automation\McpServer\ProcessKnowledge\AI-Curation-Workflow.md`，验证 `get_process_design_guide` 针对 identify、transfer、actuator、motion、transaction 主题只返回相关可用规范，不暴露来源证据或审核中间结果；发现问题就修复并完成门禁验证。
