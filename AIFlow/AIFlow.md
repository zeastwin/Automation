# AI 流程助手模块说明

> 版本说明：本文档基于当前代码实现，模块核心位于 `Automation/AIFlow/*`，UI 主窗体为 `FrmAiAssistant`。

## 1. 模块目标
- 在**不改变现有运行语义**的前提下，为流程编辑引入“可审计、可验证、可回滚”的 AI 交互式生成能力。
- 输入可为 Core/Spec/Delta，输出仍是**原始 Work JSON**，可被现有 UI/引擎直接加载。

## 2. 总体流程
1) 加载 Core/Spec（或从现有 Work 反编译生成 Core/Spec）
2) 加载 Delta（可选），生成新 Core 并对比 Diff
3) 验证（基础规则）
4) 仿真（逻辑模拟）
5) 落盘（生成 Work JSON，并记录回滚点）
6) 必要时回滚

## 3. 目录与文件结构
- `AIFlow/Contracts/`：数据契约
  - `AiFlowModels.cs`：Core/Spec 合约
  - `AiFlowContracts.cs`：协作 Contract 合约（Command/Ack）
- `AIFlow/Compiler/`：编译器
  - `AiFlowCompiler.cs`：Spec/Core → Work JSON
- `AIFlow/Delta/`：增量
  - `AiFlowDeltaModels.cs`：Delta 合约
  - `AiFlowDeltaApplier.cs`：Delta 应用器
- `AIFlow/Verifier/`
  - `AiFlowVerifier.cs`：基础验证（超时/引用/跨流程 goto 等）
- `AIFlow/Diff/`
  - `AiFlowDiff.cs`：Core 差异分析
- `AIFlow/Revision/`
- `AiFlowRevision.cs`：回滚点管理（Work 目录快照，仅允许 `Config\\Work`）
- `AIFlow/Simulator/`
  - `AiFlowScenario.cs`：场景输入
  - `AiFlowTrace.cs`：Trace 输出
  - `AiFlowSimulator.cs`：逻辑仿真
- `AIFlow/Collaboration/`
  - `AiFlowCollaborationAnalyzer.cs`：Command/Ack 协作分析
- `AIFlow/Decompiler/`
  - `AiFlowDecompiler.cs`：Work → Core/Spec
- `AIFlow/Telemetry/`
  - `AiFlowTelemetryRecorder.cs`：运行期 Snapshot Trace 采集
- `AIFlow/AiFlowCli.cs`：CLI 入口
- `AIFlow/AiFlowIo.cs`：读写工具
- `AIFlow/AiFlowAiConfig.cs`：AI 接口配置（`AIFlowAi.json`）
- `AIFlow/AiFlowAiClient.cs`：AI 调用与响应解析

## 4. 核心契约
### 4.1 Core（core-1）
```json
{
  "version": "core-1",
  "procs": [
    {
      "id": "p1",
      "name": "流程名",
      "autoStart": false,
      "pauseIo": ["IO1"],
      "pauseValue": ["Var1"],
      "steps": [
        {
          "id": "s1",
          "name": "步骤名",
          "ops": [
            {
              "id": "o1",
              "opCode": "Delay",
              "name": "延时",
              "disabled": false,
              "breakpoint": false,
              "note": "备注",
              "alarm": {
                "type": "报警停止",
                "alarmInfoId": "1",
                "goto1": "0-0-1"
              },
              "args": {
                "timeMiniSecond": "100"
              }
            }
          ]
        }
      ]
    }
  ]
}
```

### 4.2 Spec（spec-1）
```json
{
  "version": "spec-1",
  "kind": "core",
  "core": { ...同 core-1... }
}
```

### 4.3 Delta（delta-1）
```json
{
  "version": "delta-1",
  "baseRevision": "可选",
  "ops": [
    { "type": "add_step", "procId": "p1", "step": { ... } },
    { "type": "add_op", "procId": "p1", "stepId": "s1", "op": { ... } },
    { "type": "replace_args", "procId": "p1", "stepId": "s1", "opId": "o1", "args": { ... } },
    { "type": "move_op", "procId": "p1", "stepId": "s1", "opId": "o3", "index": 1 }
  ]
}
```

### 4.4 Scenario（scenario-1）
```json
{
  "version": "scenario-1",
  "start": { "procIndex": 0, "stepIndex": 0, "opIndex": 0 },
  "valuesByName": { "Var1": "123" },
  "valuesByIndex": { "0": "abc" },
  "decisions": {
    "p1/s1/o2": { "type": "goto", "target": "0-1-0" },
    "p1/s1/o3": { "type": "result", "result": true }
  },
  "allowUnsupported": false
}
```

### 4.5 Contract（contract-1）
```json
{
  "version": "contract-1",
  "contracts": [
    {
      "id": "C01",
      "type": "commandAck",
      "commandProcId": "pCmd",
      "ackProcId": "pAck",
      "cmdValueName": "CMD",
      "ackValueName": "ACK",
      "timeoutMs": 5000,
      "pollMs": 50
    }
  ]
}
```

## 5. 编译与校验规则
- `opCode` 必须是 OperationType 的**类名**（如 `Goto`、`ParamGoto`、`Delay`）。
- `goto` 必须为 `proc-step-op` 三段式，且**禁止跨流程**。
- 列表类参数的 `Count/IOCount/ProcCount` **禁止在 args 中直接设置**，由编译器自动生成并校验。
- `AlarmType` 必须为：`报警停止/报警忽略/自动处理/弹框确定/弹框确定与否/弹框确定与否与取消`。
- Verifier 做基础校验：超时配置、引用完整性、跨流程 goto、关键参数为空等。

## 6. 模拟器能力
支持的操作（逻辑仿真）：
- `Delay`、`Goto`、`ParamGoto`、`ModifyValue`、`GetValue`、`StringFormat`、`Split`、`Replace`

不支持的操作：
- IO/通讯/运动类操作默认视为**不支持**，若 `allowUnsupported=true` 则跳过执行并输出 `unsupported` 事件。

## 7. 协作分析
- `Command/Ack` 协议基础校验：
  - Command 流程是否包含 `ProcOps` 启动 Ack 流程
  - Command 是否写入 cmd 变量、等待 ack 变量
  - Ack 是否读取 cmd 变量、写入 ack 变量
- WaitProc “停止等待”互锁环检测（死锁环）。

## 8. 反编译
- `decompile` 会读取 `Work/*.json`，生成 `core-1`（可选生成 `spec-1`）。
- 生成的 `id` 采用 `p{procIndex}-s{stepIndex}-o{opIndex}` 形式。
- `Count/IOCount/ProcCount` 会被剥离，避免二次编译冲突。

## 9. UI 界面说明（FrmAiAssistant）
- 顶部：标题 + Revision
- 左侧：导航（提案/验证/仿真/Diff/Telemetry）+ 概览
- 右侧：对应功能页
- 底部：常用操作按钮（加载/验证/仿真/落盘/回滚/导出）

支持的界面功能：
- 自动选择当前流程（从 `FrmProc.SelectedProcNum` 读取）
- 显示 Work 目录（固定为 `SF.workPath`，仅允许 `Config\\Work`）
- 联动日志面板（`FrmInfo.PrintInfo`）
- AI 接口配置与生成：在“提案”页配置接口并直接生成 `FlowDelta/Core`

### 9.1 AI 接口配置
- 配置文件：`Config/AIFlowAi.json`
- 字段（`ai-1`）：
  - `endpoint`：OpenAI 兼容 Chat Completions 接口地址
  - `apiKey`：鉴权密钥
  - `model`：模型名
  - `authHeader`：鉴权头（默认 `Authorization`）
  - `authPrefix`：鉴权前缀（默认 `Bearer`，可为空）
  - `timeoutSeconds`：超时秒数
  - `temperature`：温度（0~2）
- 生成逻辑：基于“需求描述 + 上下文”调用接口，要求**仅输出 JSON**，并按 `FlowDelta/Core` 解析。

## 10. CLI 使用
```bash
Automation.exe aiflow compile --core <core.json> --out-dir <Config\\Work目录>
Automation.exe aiflow compile --spec <spec.json> --out-dir <Config\\Work目录>

Automation.exe aiflow verify --core <core.json>
Automation.exe aiflow verify --spec <spec.json>

Automation.exe aiflow delta-apply --base-core <core.json> --delta <delta.json> --out-core <core.json> [--diff <diff.json>] [--out-work <Config\\Work目录>] [--save-revision [note]]
Automation.exe aiflow diff --base-core <core.json> --target-core <core.json> [--out <diff.json>]
Automation.exe aiflow rollback --work-dir <Config\\Work目录> --revision <id>

Automation.exe aiflow simulate --core <core.json> --scenario <scenario.json> --out-trace <trace.json>
Automation.exe aiflow collab-verify --core <core.json> --contracts <contracts.json>
Automation.exe aiflow decompile --work-dir <Config\\Work目录> --out-core <core.json> [--out-spec <spec.json>]
```

## 11. 回滚说明
- 回滚点目录：`Config/Work_revisions/<revisionId>`
- 目前只快照 **Work 目录**，不包含 `value.json`/`DataStruct.json` 等其他配置文件。
- 为防止误删，落盘/回滚仅允许 `Config\\Work`，UI 只能选择 `Config` 目录。

## 12. 注意事项
- 校验未包含资源存在性（IO/变量/通讯对象）的全量检查，需结合业务配置补充。
- 模拟器为逻辑仿真，不接硬件，不代表真实执行结果。
- `Telemetry` 目前基于 `EngineSnapshot` 采样，粒度为步骤/指令级状态变化。

## 13. 扩展建议
- 接入流程编辑器 Diff 可视化（对比 JSON 或结构树）
- 协作协议库扩展（Event/Lock/多状态机）
- 运行期 Trace + 场景回放
