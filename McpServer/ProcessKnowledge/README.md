# Automation 可用流程规范库

本目录只保存已经完成旧项目甄别、可以提供给 Automation AI 的流程规范。没有候选、审核中、废弃和空分类目录；一个条目能进入 `catalog.json` 就表示它可用，删除和替代历史由 Git 保存。

新对话需要接管旧项目经验整理时，先完整阅读 `AI-Curation-Workflow.md`。

知识生产链路在库外完成：

```text
旧项目只读证据 -> AI 审核与归纳 -> 可用规范 -> 本目录 -> get_process_design_guide
```

`Transform4SNsdemo/runs/` 保存原始证据和审核过程，本目录不复制它们。AI 运行时只读取 `catalog.json` 登记的 Markdown，不接触来源摘要和中间结果。

## 三层知识架构（设计思想）

| 层 | 载体 | 回答的问题 | 生命周期 |
| --- | --- | --- | --- |
| 路由层 | `Assets/Goose/automation.md`「整机流程族」段 | 一台设备由什么构成、什么时候需要哪个兄弟流程 | 常驻每会话 |
| 组合层 | `composition` 主题 + `device-frame.*.md` | 单机怎么构成：功能单元表、单元间衔接、变化点、搭建顺序 | 按需 |
| 功能块层 | 其余 `blocks/*.md` + `Guides/ProcessDesignGuide.md` | 某类功能块内部怎么写 | 按需 |

层间只索引不复制：功能块说"怎么做"，组合层说"有哪些、先做哪个"，路由层说"全景与边界"。新增知识时先判断属于哪一层——归纳完功能块后必须自问"组织这些块的分类图景落盘了吗"（策展人盲区：组织层知识因内化而最容易漏写）。

## 当前库存

- 48 个功能/设备框架块（26 个功能块 + 13 个 `device-frame.*` 单机设备框架 + 7 个 20260828 批次新功能块 + 数据设计层 2 块 + `observability.design`），来源 129 个证据包（`provenance/sources.json` 含逐包甄别结论与 manifest SHA256；20260828 批次按同型号结构族去重深审代表机）。设备框架覆盖形态去重结论：129 包 ≈ 60 独立型号 ≈ 15~18 形态族，已立 13 框架；单包弱证据形态（NA/Sedona 组装类、Coil-UV、镀膜插锅机等）暂不立框架，等同类第二例出现再立。
- 17 个主题（core + 16），`composition` 只针对单台设备（多机台产线编排不收录）；`get_process_design_guide` 支持 patternIds 按块钻取，库变大后先 compact 取索引再收窄目标块；设备框架钻取响应带 `relatedPatternIds` 任务包清单，按清单一次拉全关联块。
- 20260828 批次确认了两类设备软件形态：NS 全流程平台（流程直接控制轴与 IO）与 PLC 主控-PC 采集代理（PC 只做信息代理）；后者由 `device-frame.acquisition-proxy` 与 communication.* 块覆盖。
- 主检索维度是 `capabilities`；`topics` 决定 `get_process_design_guide` 按主题返回哪些块。设备和工艺只是多值标签，不建立额外树形目录；真实差异直接写入规范的"当前事实与适配"章节，出现实际复用压力后再建立新抽象。

## 块结构增强（20260829 批次）

在原有"规则型"结构上补充四个正交维度，全部寄宿在现有块与框架内，不新增获取机制：

1. **反模式**：每个功能块含 4~5 条"别这样做 + 后果 + 正确做法"（校验脚本强制 `## 反模式` 节），设备框架含框架级搭建陷阱。
2. **定量参考惯例**：`variables.design`（超时/去抖/重试/节拍典型起步值）与 `data-struct.design`（容量与落盘周期）；标注"起步值，现场定型"，防止模型拍脑袋也防止照抄历史数值。
3. **权衡裁决顺序**：`ProcessDesignGuide.md` core 区新增"规则冲突时的裁决顺序"（安全 > 正确 > 可查 > 效率），冲突时按序裁决不随机选边。
4. **黄金样例**：设备框架的 `## 黄金样例` 节（可选）挂真实项目流程族骨架 + 逐段排序理由，只取结构与顺序，参数按当前项目重建；13 个设备框架全部覆盖（联丰素材 + NA/AutoRoxer/Crown/拆盖板/SIP-TO-HSG/LCF-MLA）。

## 主题契约（硬约束，违者 MCP 启动拒启）

1. `catalog.json` 条目的 `topics` ⊆ `ProcessDesignGuideCatalog.SupportedTopics`，且与 `schema.json` 枚举、`get_process_design_guide` 工具描述保持一致；每个非 core 主题在 `Guides/ProcessDesignGuide.md` 必须有 `<!-- process-design:{topic}:start/end -->` 区块。
2. `Program.cs` 启动时校验整个知识目录：契约不同步直接拒启（exit code 2），不允许拖到运行期让所有指南请求连环失败（`ProcessKnowledgeCatalog` 逐条校验，一条坏条目会让全部主题请求失败）。
3. 条目 `sourceRefs` 必须逐条可在对应 `Transform4SNsdemo/runs/*-knowledge/knowledge_candidates/normalized-cases` 中验证存在。
4. 新增主题四处一次改齐：白名单 + schema 枚举 + 工具描述 + 指南区块（历史教训：vision、quality 两次漏登记导致运行期连环失败）。

## 纳入标准

每份规范必须：

1. 只有一个可观察目标，尺度大于单条指令、小于整条产线流程（整机组合块除外，组合块是跨块索引）。
2. 写明适用边界、当前事实、阶段、完成证据、失败、超时、恢复和幂等。
3. 至少引用一份可回查证据，但不把出现频率当作正确性证明。
4. 删除旧名称、地址、参数、索引、跳转和默认报警策略，只保留经工程判断仍成立的设计能力。
5. 与当前 Automation Schema、行为契约、资源和 Readiness 重新适配。

## 术语与载体约定

- 仓位占用记录不称"账本"；记录载体按最简优先选择一种：流程变量 > 数据结构 > 自定义代码（SDK/消息交互）> 本地文件（json 等）。旧项目的载体选择只是证据，不是推荐。

新增规范时直接由 AI 完成证据审核和归纳；证据不足就不写入本库，不在库中留下占位条目。
