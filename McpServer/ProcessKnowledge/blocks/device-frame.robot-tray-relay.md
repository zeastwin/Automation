# 机器人 Tray 接驳接力设备框架

## 设备画像

以 Bucket 机器人为核心的多目标上下料单机：机器人服务前/后工艺工站与 Tray 盘供料，配套接驳机/投料口与跨线下流线接力分发；Bucket 上执行点胶等工艺；Tray 盘满/空触发换盘。典型如 Crown 类组装接驳机。

## 功能单元构成

| 单元 | 职责 | 典型流程 | 引用功能块 |
|---|---|---|---|
| 机器人调度 | Bucket 机器人多目标取放总控 | Bucket机器人（循环） | orchestration.single-robot-dispatch |
| 工艺工站 | 前/后工站作业与状态推进 | 前工站、后工站 | transfer.pipeline-station-triad |
| Bucket 工艺 | 机器人平台上点胶与出料 | Bucket点胶（循环）、Bucket出料（循环） | dispensing.service-material-path |
| 接驳接力 | HSG 接驳机、投料口常驻接驳 | HSG接驳机（循环）、HSG投料口（循环） | orchestration.cross-machine-relay |
| 跨线下流线 | S2→S1 跨线体放行接力 | S2->S1下流线（循环） | orchestration.cross-machine-relay |
| Tray 盘供给 | 满料仓寻位、空料仓寻位、换盘 | 满料仓寻位、空料仓寻位、更换Tray盘 | identify.warehouse-seek-and-allocate |
| 折排线模组 | 左/右折排线执行与监控 | 右折排线模组、左折排线模组、前后折排监控 | transfer.station-handoff |
| 标定族 | Bucket 相机与点胶上相机标定 | Bucket相机标定、点胶上相机标定 | vision.positioning-and-correct |
| 系统服务 | 系统五件套+排胶+版本上报 | 系统状态/门禁/灯控/时间/防呆、系统排胶、上传软件版本号 | reset.system-and-station-reinit、monitoring.system-service-listeners |
| 空跑族 | 各循环流程成对空跑 | 前后工站/Bucket点胶/出料/接驳/投料口（空跑）、保存空跑数据 | quality.dry-run-and-grr-pair |

## 单元间衔接

物料流：Tray 盘（满料仓寻位供料）→ 机器人取料 → 前工站作业 → Bucket 点胶 → 后工站 → Bucket 出料 → HSG 接驳机/投料口 → S2→S1 下流线跨线分发。换盘流：Tray 盘空/满由寻位流程探测，更换 Tray 盘与机器人调度互斥（换盘时机器人不到该盘取料）。接驳流：接驳机/投料口/下流线三个常驻循环各自独立，按下游要料+屏蔽分发（对等接力，不设中央调度）。验证流：主链每个循环流程都有成对空跑版本。

## 框架变化点

工站数（前/后两站 vs 单站）；有无点胶工艺（Bucket 点胶段有无）；接驳拓扑（接驳机+投料口+下流线的组合取舍）；折排线模组有无及左右配置；Tray 盘仓数。

## 搭建顺序

系统五件套（含排胶、版本上报）→ 机器人调度骨架 → 前/后工站三件套 → Tray 盘寻位与换盘 → Bucket 点胶与出料 → 接驳机/投料口 → 跨线下流线 → 折排线模组与监控 → 标定族 → 空跑族收尾。

## 完成证据

一个工件从 Tray 取料到跨线分发走通且每次给料握手后信号清零；换盘期间机器人不动该盘（互斥成立）；空跑族与主链逐条对应；被屏蔽的下游目标绝不收料。

## 关联块清单

- `orchestration.single-robot-dispatch`
- `orchestration.cross-machine-relay`
- `orchestration.shared-feeder-dispatch`
- `transfer.pipeline-station-triad`
- `transfer.station-handoff`
- `identify.warehouse-seek-and-allocate`
- `dispensing.service-material-path`
- `vision.positioning-and-correct`
- `quality.dry-run-and-grr-pair`
- `reset.system-and-station-reinit`
- `monitoring.system-service-listeners`
- `variables.design`
- `data-struct.design`
- `custom-function.code-process-collaboration`
- `observability.design`

## 反模式

- **换盘与取料并发**：机器人还在空 Tray 上取料时开始换盘。换盘与该盘取料互斥。
- **接驳三循环互相等待**：接驳机/投料口/下流线两两握手成环，全卡死。三个常驻循环只对下游单向要料。
- **跨线接力不查屏蔽**：S2→S1 放行不看对线状态，下游堆料。放行前查要料+屏蔽。
- **空跑族缺项**：主链 8 个循环流程只配了 3 个空跑，验证覆盖不全。空跑族与主链一一对应。
- **标定只有一级**：Bucket 相机标定后点胶工位直接用，缺点胶上相机标定衔接。两级标定成对建。

## 黄金样例

> 来源：`js-Crown-S1_202500411`。机器人 Tray 接驳的落地参考。

**流程族清单**（46 个流程按单元归组）：复位、启动｜Bucket机器人（循环）、前工站、后工站、Bucket点胶（循环）、Bucket出料（循环）｜HSG接驳机（循环）、HSG投料口（循环）、S2->S1下流线（循环）｜更换Tray盘、满料仓寻位、空料仓寻位｜右折排线模组、左折排线模组、前后折排监控｜Bucket相机标定、点胶上相机标定｜系统状态/门禁/灯控/时间/防呆、系统排胶、门禁报警、上传软件版本号循环｜空跑族（前后工站/Bucket点胶/出料/接驳/投料口各自空跑+保存空跑数据）｜轴精度测试、动静态测试、验证扫码（测试残留不吸收）。六个主循环（机器人/点胶/出料/接驳/投料口/下流线）全部常驻循环化——多目标接力的主体就是这组循环，机器人调度是其中唯一的事件驱动环节。

**接驳拓扑骨架**：Bucket出料 → HSG接驳机（缓存衔接）→ HSG投料口（投料对接）→ S2->S1下流线（跨线分发）。三个接驳循环各自独立常驻、单向对下游要料——拓扑是链式的，但循环之间没有互相等待的环；任何一段下游屏蔽，上游循环持料等待并周期重查。

**空跑族覆盖证据**：主链六个循环流程每个都有空跑版本，外加"保存空跑数据"专门落盘——空跑覆盖到每一条常驻循环（不只是工艺段），这是"空跑三段对应主链三段"原则在多循环设备上的推广：主链有几个循环，空跑就有几个。

## 幂等与甄别结论

框架只约束多循环接力拓扑与换盘互斥。历史测试残留流程（轴精度/动静态/验证扫码）、空跑无效变体不吸收；主循环常驻化、接驳单向要料、换盘互斥、空跑族全覆盖、折排成对监控被保留。
