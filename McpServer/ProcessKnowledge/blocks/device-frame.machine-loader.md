# 上料机设备框架

## 设备画像

把料仓/来料线的原料（玻璃、HSG、料盘）整备后供给下游机台的单机：满料仓寻料分料供盘，空料仓回收，经撕膜/扫码/清洁/整形等工艺工站处理后，按下游要料选择目标对等接力送出。

## 功能单元构成

| 单元 | 职责 | 典型流程 | 引用功能块 |
|---|---|---|---|
| 满料仓供料 | 寻料、分料、防呆 | 满料仓分料流程+防呆看门狗 | identify.warehouse-seek-and-allocate |
| 空料仓回收 | 空盘回收、下空寻料 | 空料仓流程组 | identify.warehouse-seek-and-allocate |
| 工艺工站 | 撕膜/扫码/清洁/整形 | 各工站作业流程 | transfer.pipeline-station-triad、vision.positioning-and-correct |
| 搬运调度 | WD/机器人取放、补料 | 调度流程+取放作业流程 | orchestration.shared-feeder-dispatch、orchestration.single-robot-dispatch |
| 出料衔接 | 选目标、给料握手 | 供料流水线/下机台送料流程 | orchestration.cross-machine-relay |
| 辅助入口 | 一键清料、移出空盘 | UI 小流程入口 | custom-function.code-process-collaboration |
| 常驻保障 | 缺料报警、磁盘、门禁、复位 | 常驻监控+复位族 | monitoring.resource-watchdog、reset.system-and-station-reinit |

## 单元间衔接

物料流：来料→满料仓（寻料分料）→工艺工站（缓冲三件套推进）→搬运调度→出料衔接（按下游要料+屏蔽分流）。调度流：共享搬运机构由调度流程互斥批准各工位。信息流：满料感应控制要料电平，穴位占用委托代码计算顺延。

## 框架变化点

料仓式（Tray 盘）vs 流水线式（载具直接流入）；工艺工站按机型取舍（撕膜/清洁/扫码/整形的组合）；单搬运臂 vs 多工位流水线；有无跨机台分发（单下游直供 vs 多下游选目标）。

## 搭建顺序

复位族 → 常驻监控 → 满料仓寻料分料（含防呆看门狗）→ 工艺工站三件套 → 搬运调度 → 出料衔接握手 → UI 小流程入口。

## 完成证据

从满料仓到下游收到料的完整链路走通且每次给料握手后信号清零；空仓停轴不撞料；被屏蔽目标绝不收料。

## 幂等与甄别结论

框架只约束单元构成与衔接。旧项目固定 8 放料点位、(弃用)流程保留形态、空流程不吸收；单元划分、寻料防呆看门狗、屏蔽分流、UI 入口小流程化被保留。
