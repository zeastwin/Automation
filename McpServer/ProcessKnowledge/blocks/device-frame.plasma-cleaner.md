# 等离子清洁机设备框架

## 设备画像

对载具/工件执行等离子清洁的单机：载具经缓存区进入清洁区，按画圆参数执行清洁轨迹，同时持续监控离子功率，进出站完成身份追溯与给料握手。

## 功能单元构成

| 单元 | 职责 | 典型流程 | 引用功能块 |
|---|---|---|---|
| 载具流转 | 缓存区→清洁区状态推进 | 载具缓存/清洁区流程 | transfer.pipeline-station-triad |
| 清洁作业 | 画圆参数、轨迹运动 | HSG 清洁作业流程 | motion（工站走点）+ orchestration.shared-feeder-dispatch（共享机构时） |
| 功率监控 | 离子功率定期检测、暂停态检测 | 功率检测流程 | monitoring.analog-threshold-guard |
| 喷枪维护 | 清洗喷枪 | 维护流程 | dispensing.service-material-path（结构同源维护） |
| 追溯事务 | RFID 进站、MES 入站检查/出站上传 | 进出站流程 | identify.read-and-bind-carrier、transaction.submit-trace-record |
| 给料衔接 | 与上下游的信号握手 | 给料信号清零流程 | orchestration.cross-machine-relay |
| 常驻保障 | 门禁、报警处理、复位、状态灯 | 常驻+复位族 | interlock.safety-gate-watchdog、reset.system-and-station-reinit、monitoring.system-service-listeners |

## 单元间衔接

物料流：上游→缓存区（要料）→清洁区（画圆作业）→出站（给料握手清零）。追溯流：进站 RFID 绑定，MES 入站检查通过后才清洁，出站上传。安全流：清洁期间功率/离子监控常驻，暂停态检测异常按安全停止处理。

## 框架变化点

单清洁工位 vs 多暂存台（出料/桥接/子机暂存台接力）；有无 MES 入站检查；画圆参数独立流程 vs 内联；物理 IO vs 通讯 IO 双模（运行前定一种）。

## 搭建顺序

复位族 → 门禁/功率监控常驻 → 缓存/清洁区三件套 → 清洁作业+画圆参数 → RFID/MES 进出站 → 给料握手 → 喷枪维护。

## 完成证据

一个载具周期"进站绑定→清洁→功率无越限→出站上传→给料清零"完整走通；暂停态离子检测能发现清洁机构异常。

## 幂等与甄别结论

框架只约束单元构成与衔接。旧项目固定画圆参数值、固定信号编号、断火测试具体阈值不吸收；单元划分、MES 入站前置、功率暂停态检测被保留。
