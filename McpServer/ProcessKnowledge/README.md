# Automation 可用流程规范库

本目录只保存已经完成旧项目甄别、可以提供给 Automation AI 的流程规范。没有候选、审核中、废弃和空分类目录；一个条目能进入 `catalog.json` 就表示它可用，删除和替代历史由 Git 保存。

新对话需要接管旧项目经验整理时，先完整阅读 `AI-Curation-Workflow.md`。

知识生产链路在库外完成：

```text
旧项目只读证据 -> AI 审核与归纳 -> 可用规范 -> 本目录 -> get_process_design_guide
```

`Transform4SNsdemo/runs/` 保存原始证据和审核过程，本目录不复制它们。AI 运行时只读取 `catalog.json` 登记的 Markdown，不接触来源摘要和中间结果。

## 最小目录

```text
ProcessKnowledge/
├── schema.json             # catalog.json 的机器契约
├── catalog.json            # 可用规范索引和检索标签
├── blocks/*.md             # 可直接指导 AI 的完整规范
├── provenance/sources.json # 内部回查来源，不返回给运行时 AI
└── Validate-ProcessKnowledge.ps1
```

主检索维度是 `capabilities`。设备和工艺都只是多值标签，不建立额外树形目录；真实差异直接写入规范的“当前事实与适配”章节，出现实际复用压力后再建立新抽象。

## 纳入标准

每份规范必须：

1. 只有一个可观察目标，尺度大于单条指令、小于整条产线流程。
2. 写明适用边界、当前事实、阶段、完成证据、失败、超时、恢复和幂等。
3. 至少引用一份可回查证据，但不把出现频率当作正确性证明。
4. 删除旧名称、地址、参数、索引、跳转和默认报警策略，只保留经工程判断仍成立的设计能力。
5. 与当前 Automation Schema、行为契约、资源和 Readiness 重新适配。

新增规范时直接由 AI 完成证据审核和归纳；证据不足就不写入本库，不在库中留下占位条目。
