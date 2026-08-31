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

## 关联块清单

- `identify.warehouse-seek-and-allocate`
- `transfer.pipeline-station-triad`
- `vision.positioning-and-correct`
- `orchestration.shared-feeder-dispatch`
- `orchestration.single-robot-dispatch`
- `orchestration.cross-machine-relay`
- `transfer.machine-demand-signal`
- `monitoring.resource-watchdog`
- `reset.system-and-station-reinit`
- `variables.design`
- `data-struct.design`
- `custom-function.code-process-collaboration`
- `observability.design`

## 反模式

- **寻料流程缺防呆看门狗**：只建寻料分料不建独立防呆流程，探测失效撞料仓。看门狗是供料单元的一部分。
- **出料衔接无握手**：直接往下机台送，对方还没就绪。出料必须有要料检测+给料握手。
- **工艺工站顺序排错**：撕膜/清洁顺序按历史抄，与当前工艺要求不符。工站组合按框架变化点确认。
- **UI 入口长动作**：一键清料入口流程里写完整搬运逻辑。入口只置标记，动作归调度。

## 黄金样例

> 来源：`BC_V3.0.7.02.24_20240822最新(联丰)`。料仓式供料的落地参考。

**流程族清单**（29 个流程按单元归组）：复位、系统启动、系统IO监听、门禁监控、报警处理｜满料仓入料上料区、满料仓工作平台、满料仓分料作业、满料仓轴寻料防呆｜空料仓工作平台、空料仓出料下料区、空料仓取空料盘作业、空料仓下空寻料作业、空料仓满盘定位作业｜载具流水线工作缓冲区/工作平台、回流线接驳/输送/转运区｜机器人平台、机器人拍照取料/扫码纠偏/拍照贴合作业、机器人抛料｜进站读取RFID、出站写入RFID、出站前数据处理、视觉标定、Mark标定。注意满料仓/空料仓/回流线各区分区结构完全同构（各区都是九态展开）——同构复制+改名核对是这类设备的主体写法。

**满料仓入料上料区骨架**（9 步 = 九态标准展开）：进料 Ready→Running→Finish → 工作 Ready→Running→Finish → 出料 Ready→Running→Finish。与点胶机缓冲区的九态完全同构，只是"工作"内容变为满料仓准备——同构性本身就是框架证据：换设备不换骨架，换的是工作段内容。

**满料仓分料作业骨架**（5 步）：初始化（寻盘次数置位、阻挡缩回）→ 寻料（置寻盘标志→运动→信号捕获有盘信号；未捕获且未达上限则慢速步进重试）→ 空仓出口（达上限置空仓标志→停止轴运动）→ 分料（分料气缸伸出检测→夹爪配合作业）→ 交付。配套**满料仓轴寻料防呆**（1 步自启常驻）：寻盘标志=1 期间监视对射感应，异常立即停止轴运动。理由：寻料是"运动中探测"，探测失效的唯一防线就是独立的看门狗——它必须独立自启，不能写在寻料流程里陪葬。

## 幂等与甄别结论

框架只约束单元构成与衔接。旧项目固定 8 放料点位、(弃用)流程保留形态、空流程不吸收；单元划分、寻料防呆看门狗、屏蔽分流、UI 入口小流程化被保留。
