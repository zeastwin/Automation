# AIFlow 示例文件说明

## 文件清单
- `core.base.json`：基础 Core 示例（1 流程/1 步骤/4 操作）
- `spec.sample.json`：Spec 示例，内部包含同一份 Core
- `delta.add-delay.json`：Delta 示例，给 `core.base.json` 追加一个 Delay 操作
- `scenario.pass.json`：仿真场景（按真实逻辑进入成功分支）
- `scenario.force-fail.json`：仿真场景（强制进入失败分支）

## UI 内快速验证建议
1) 进入 AI 面板 → 加载 `core.base.json` → 点击“验证”应通过
2) 加载 `scenario.pass.json` → 点击“仿真”应走成功分支
3) 加载 `scenario.force-fail.json` → 点击“仿真”应走失败分支
4) 加载 `delta.add-delay.json` → 点击“Diff”应看到新增操作

> 注意：Apply/回滚会写入 `Config/Work`，请在测试环境操作。
