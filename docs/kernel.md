# 内核调度与执行模型

本文档描述调度与执行器的核心结构、状态机、线程模型、事件流，以及告警语义与常见故障恢复策略。调度统一通过 KernelScheduler 驱动，流程启动/停止不再走旧的数组直控路径。

## 状态机

运行态采用 `ProcRunState` / `ProcessState` 对应关系：

- `Stopped`：已停止或未启动
- `Running`：正常运行
- `Paused`：暂停或断点等待
- `SingleStep`：单步模式
- `Alarming`：报警处理中

常见转换：

- Start：`Stopped -> Running`
- Pause：`Running/Alarming -> Paused`
- Resume：`Paused/SingleStep -> Running`
- Step：`Paused/SingleStep -> SingleStep`
- Alarm：`Running -> Alarming -> (恢复为上一次状态或停止)`
- Stop：任意状态 -> `Stopped`

断点通过 `isBreakpoint` 标记，触发后进入 `Paused`（若非单步模式）。

## 线程模型

- 每个流程由一个 `ProcessRunner` 承载，持有独立 `Thread`。
- `ProcessRunner` 调用 `DataRun.RunProc` 执行步骤与操作。
- UI 线程仅负责展示与交互，不直接操纵内核运行线程。
- 流程执行线程内禁止直接访问 WinForms 控件。

## 同步控制（三事件）

执行同步使用 `RunnerSyncController` 统一管理：

- `EnterBreakpoint()`：进入断点，阻塞执行线程
- `Continue()`：继续执行
- `StepOnce()`：单步执行一次
- `ForceWakeForStop()`：停止时强制唤醒所有等待点
- `WaitForContinue()`：执行线程等待继续信号

所有 `m_evtRun/m_evtTik/m_evtTok` 的 Set/Reset/WaitOne 组合已集中收敛。

## 事件流

内核侧：

- `KernelScheduler.StatusChanged`：状态变化通知
- `KernelScheduler.Faulted`：报警/故障通知
- `DataRun.ProcTextChanged`：流程树状态文本更新
- `DataRun.PauseTextChanged`：暂停按钮文字更新
- `DataRun.AlarmDialogRequested`：告警弹窗请求

UI 侧（`FrmMain`）：

- 订阅上述事件并在 UI 线程更新控件
- 弹窗由 UI 线程创建，结果回传内核

## 告警语义保持说明

告警行为保持现状：

- `报警停止`：UI 确认后 `isThStop = true`，流程停止
- `报警忽略`：不弹窗、不阻塞、继续执行
- `自动处理`：不弹窗、不阻塞，直接跳转 `Goto1`
- `弹框确定`：UI 确认后跳转 `Goto1`
- `弹框确定与否`：确认跳转 `Goto1`，否则跳转 `Goto2`
- `弹框确定与否与取消`：分别跳转 `Goto1/Goto2/Goto3`
- 执行异常：UI 提示后停止

所有需要操作员交互的告警均在 UI 线程显示，但流程线程同步等待选择结果，阻塞语义不变。

## WaitProc 等待策略

`WaitProc` 通过 `KernelScheduler` 内部状态信号等待目标流程：

- `运行`：等待目标进程 `RunningEvent`
- `停止`：等待目标进程 `StoppedEvent`
- 额外监听当前流程的 `StoppedEvent`，以响应 Stop
- 每 100ms 重新评估目标（兼容 `ProcValue` 动态变化）
- 超时触发 `isAlarm = true`，`alarmMsg = "等待超时"`

## 常见故障与恢复策略

- **等待超时**：确认目标流程名称/变量解析正确，检查目标流程是否已启动或已停止
- **停在断点**：检查步骤断点设置，使用继续/单步恢复
- **告警弹窗无响应**：确认 UI 线程未阻塞，查看 `FrmInfo` 日志与状态事件
- **找不到流程**：检查流程名称/变量值是否存在于流程列表
- **执行异常**：查看 `alarmMsg` 与日志，修复后 Stop/Start 重新运行
- **外设/IO 异常**：核对 IO 映射、卡状态与通信配置

## 兼容与约束

- `ProcHandles` 仍作为运行时状态视图供 UI 查询，但不再由 UI/外部直接创建或驱动线程
- 所有启动/停止/暂停/单步均通过 `KernelScheduler/ProcessRunner` 统一入口
