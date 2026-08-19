# BC 产线整机组合（上料→清洁→点胶→组装→复检→固化→回流）

## 可观察目标

一条 BC（玻璃基载具）产线由多台独立机台串联：玻璃上料机（满料仓供盘/撕膜/清洁/扫码）→ Plasma 清洁 → Primer 点胶 → 点胶/组装/复检一体机 → 隧道炉（盖板+治具回流）→ 拆盖板 → 静置中转 → 下料。每台机是独立流程族，机台间用要料/给料信号对等衔接；载具身份（RFID/条码）随物料全链流转。

## 适用边界

适用于 BC/HSG 类多机台玻璃载具产线的整机规划与逐机台搭建。单机台内部设计用各主题功能块；非载具类产线（如飞达供料）参考组合方法但构成不同。

## 当前事实与适配

需要：各机台的实际构成（用户现场可能只有其中几台）、机台间信号表（要料/给料/屏蔽）、载具身份载体、MES 交接点。历史项目固定机台数量与固定信号编号不得复用；机台数量与顺序以用户现场为准。

## 流程族构成与引用

| 类别 | 构成 | 引用规范 |
|---|---|---|
| 复位/启动 | 各机台复位总流程+工站复位分层、系统启动、按钮代理 | reset.system-and-station-reinit、monitoring.system-service-listeners |
| 上料供料 | 满料仓寻料分料、空料仓回收、WD 取放调度 | identify.warehouse-seek-and-allocate、orchestration.shared-feeder-dispatch |
| 工位缓冲 | 各工位缓冲区+作业台成对，九态状态机 | transfer.pipeline-station-triad |
| 工艺作业 | 撕膜/清洁/点胶/组装/复检作业流程 | vision.positioning-and-correct、dispensing.service-material-path |
| 机台衔接 | 跨机台接力、料位需求信号 | orchestration.cross-machine-relay、transfer.machine-demand-signal |
| 追溯 | 进出站 RFID/条码绑定、MES 过站 | identify.read-and-bind-carrier、transaction.submit-trace-record |
| 质量 | 复检 NG 抛料、空跑/GRR 验证 | quality.reject-and-divert、quality.dry-run-and-grr-pair |
| 载体回收 | 盖板回流线、治具回流线、空载具回流 | orchestration.replicated-station-fleet、transfer.flow-throttle-control |
| 常驻保障 | 门禁、资源监控、状态灯、模拟量 | interlock.safety-gate-watchdog、monitoring.resource-watchdog、monitoring.analog-threshold-guard |

## 参考阶段（搭建顺序）

1. 先规划整机：列出目标内的机台与流程类别清单、机台间握手信号表、载具流向图；与用户确认缺哪几台后再动手。
2. 每台机内部按"复位族+系统服务 → 常驻监控 → 缓冲/作业台骨架 → 工艺作业 → 维护（排胶擦胶等）→ 追溯进出站"顺序搭建。
3. 机台间接缝最后连：上游供料流程按下游要料信号选目标、给料握手清零；跨机台屏蔽标志过滤目标。
4. 点位命名沿用"工位+动作+相对位置"（取料位/放料位/安全位/过渡点/上方点+序号），同工位同风格。
5. 整机联调前逐流程核对 Readiness；空跑流程先于生产流程验证轨迹。

## 完成证据

目标内每台机的流程族可独立复位与启动；机台间握手信号每次物料交接后归零；载具身份从上料到下料全程可追溯；任一机台异常停机不破坏相邻机台的物料完整性。

## 失败、超时与恢复

单机台异常走本机报警处理与复位，不自动联动全线停机（除安全门禁）；跨机台物料滞留由要料超时提示；整机急停由各机台集中停止流程独立执行。

## 幂等与甄别结论

组合规范只约束类别构成与接缝契约，各流程内部幂等性由引用的功能块各自保证。旧项目按固定机台顺序硬编码的联锁、固定信号编号表未被保留；流程族分类、搭建顺序、握手接缝、载体回收配套、身份随料流转被保留。
