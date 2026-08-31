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

## 关联块清单

- `transfer.pipeline-station-triad`
- `monitoring.analog-threshold-guard`
- `dispensing.service-material-path`
- `identify.read-and-bind-carrier`
- `transaction.submit-trace-record`
- `orchestration.cross-machine-relay`
- `interlock.safety-gate-watchdog`
- `reset.system-and-station-reinit`
- `monitoring.system-service-listeners`
- `variables.design`
- `data-struct.design`
- `custom-function.code-process-collaboration`
- `observability.design`

## 反模式

- **MES 入站检查被跳过**：不等 MES 入站确认就清洁，产品漏检。入站检查是清洁前置。
- **功率监控缺席**：只建清洁作业不建功率阈值监控，清洁异常无人发现。功率监控是常驻单元。
- **暂停态不检测**：设备暂停后清洁机构状态失察，恢复运行带故障。暂停态离子/功率检测保留。
- **双模 IO 混用**：物理 IO 与通讯 IO 两套要料实现并存，改一处漏一处。运行前定一种。
- **喷枪维护混入生产**：维护流程和清洁作业互相等待死锁。维护独立触发并单向恢复。

## 黄金样例

> 来源：`玻璃plasma清洁0615(联丰)`。多暂存台清洁机的落地参考。

**流程族清单**（17 个流程按单元归组）：复位总流程、主流程启动、门禁检测、报警处理、轴精度（测试残留不吸收）｜清洁工站复位、出料工站复位、子机工站复位（复位分层到工站）｜WD清洁缓冲区、WD清洁工作台、WD清洁工作、清洁、清洁画圆参数｜WD取料｜WD出料暂存台、WD桥接暂存台、WD子机暂存台。

**多暂存台布局**：清洁缓冲区→清洁工作台→出料暂存台→桥接暂存台→子机暂存台。五个 WD 前缀区全部是九态同构展开，"工作"内容只有清洁工作台有实质工艺——其余区都是流转暂存。这正是框架变化点里"单清洁工位 vs 多暂存台接力"的多暂存形态：暂存台越多，九态复制的纪律越重要（同名不同前缀，一处漂移全线错位）。

**清洁作业与参数分离**：清洁（作业流程）与清洁画圆参数（独立参数流程）分开——画圆轨迹参数不写死在作业里，独立流程承载并允许按产品调整。作业流程调用参数、参数只管数值不管时序，改参数不用动作业结构。

**复位三层**：复位总流程 → 清洁工站/出料工站/子机工站复位各一份 → 各暂存台在对应工站复位内收敛。与双工位框架同样的分层原则：工站复位独立、总复位聚合，单工站维护不影响其他站。

## 幂等与甄别结论

框架只约束单元构成与衔接。旧项目固定画圆参数值、固定信号编号、断火测试具体阈值不吸收；单元划分、MES 入站前置、功率暂停态检测被保留。
