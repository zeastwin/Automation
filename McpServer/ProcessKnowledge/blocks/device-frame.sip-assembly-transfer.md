# SIP 转移组装设备框架

## 设备画像

对 SIP 类载体执行"进站扫码→工位组装→组装拍摄→出料→测高→回流解绑"的转移组装单机。PLC 主控运动，PC 程序承担扫码身份、组装过程拍摄、电批/压力曲线、过站数据与抛料监控；存在副机（子机）进出料扩展。

## 功能单元构成

| 单元 | 职责 | 典型流程 | 引用功能块 |
|---|---|---|---|
| 进站扫码位 | 载体身份读取与过站校验 | 进站扫码位流程 | vision.plc-assisted-vision |
| 组装工位 | SIP/SBCM/Foam 等按机型组装 | 组装工位A/B、SBCM组装位 | communication.plc-pc-channel |
| 组装拍摄 | 组装过程拍摄、螺丝拍摄 | SBCM组装拍摄、SW/SE螺丝拍摄位 | monitoring.curve-video-collection |
| 电批曲线 | 电批锁付曲线绘制 | 电批1(SW)/电批2(SE)曲线绘制 | monitoring.curve-video-collection |
| 出料与测高 | 出料过站、出站测高 | 出料位、出站测高位 | communication.pdca-upload-retransmit |
| 回流解绑 | 载体身份解绑允许回流复用 | 回流解绑位 | identify.read-and-bind-carrier |
| 副机通道 | 子机进料/出料接力 | 副机进料位、副机出料位 | transfer.station-handoff |
| 抛料闭环 | 抛料常驻监控+定时落盘 | 抛料监控、每天定时存储抛料信息 | quality.reject-and-divert |
| 数据链路 | PDCA/Hive/心跳/点位备份 | 同采集代理框架 | communication.pdca-upload-retransmit、communication.heartbeat-and-connection、maintenance.plc-point-governance |

## 单元间衔接

身份流：进站扫码绑定 → 各工位凭身份联合查询（HSG-SIP 联合查询把两个载体身份关联校验）→ 出料过站 → 回流解绑后允许回流复用。物料流：工位循环独立推进，副机通道独立接力。数据流：拍摄/曲线按工位通道上传，抛料信息定时落盘汇总。

## 框架变化点

工位组成（最小机=扫码+出料+回流解绑；扩展机增加组装拍摄/电批曲线/测高/副机）；组装对象类型（SBCM/Foam/SIP）；有无出站测高；有无联合查询位。

## 搭建顺序

初始化 → 进站扫码位 → 出料位 → 回流解绑位 → 抛料监控 → 组装工位与拍摄 → 电批曲线 → 测高 → 副机通道 → 数据链路收尾。

## 完成证据

一个载体"扫码绑定→组装→出站→解绑"身份账目一致；联合查询两个身份都对得上才放行；解绑后的载体可被再次绑定；抛料监控与实物一致且定时记录可回查。

## 关联块清单

- `vision.plc-assisted-vision`
- `communication.plc-pc-channel`
- `monitoring.curve-video-collection`
- `communication.pdca-upload-retransmit`
- `identify.read-and-bind-carrier`
- `transfer.station-handoff`
- `quality.reject-and-divert`
- `communication.heartbeat-and-connection`
- `maintenance.plc-point-governance`
- `variables.design`
- `data-struct.design`
- `custom-function.code-process-collaboration`
- `observability.design`

## 反模式

- **解绑缺失回流死锁**：只有扫码绑定没有回流解绑位，载体回流后无法重新绑定。绑定与解绑成对。
- **联合查询被跳过**：两个关联载体的身份没有联合校验就组装，SIP 装错 SBCM。联合查询是组装前置。
- **PC 越权驱动**：PC 侧拍摄/曲线流程顺手控制 PLC 输出，主从架构被破坏。PC 只采集回传。
- **抛料只监控不落盘**：抛料计数有了但定时存储没建，追溯断链。监控与定时落盘成对。

## 黄金样例

> 来源：`js-JS_ICT_SIP-TO-HSG-S1_V3-0_20250425`。最小机形态（无组装拍摄/电批曲线扩展）的落地参考。

**流程族清单**（12 个流程）：初始化｜扫码位、出料位、回流解绑位（三个工位循环）｜PDCA上传系统、心跳包、1# Hive采集系统、版本自动变更流程、PLC点位比较备份、检测视觉Hash文件｜抛料监控（循环）｜测试流程（不吸收）。

**三工位最小闭环**：扫码位（进站绑定）→ 出料位（出料过站上传）→ 回流解绑位（身份解绑允许回流复用）。三个循环工位加上初始化就是最小机的全部业务——身份闭环"绑定→过站→解绑"由三个独立常驻循环承载，工位之间靠 PLC 侧物料流转衔接，PC 侧互不等待。

**治理单元的直接形态**：版本自动变更、PLC点位比较备份、检测视觉Hash文件——采集代理框架里的"工程治理"单元在这台最小机上以三个小流程出现。机器再小，治理三件不省：没有它们，谁改过点位/程序/视觉配置就无从对账。

**抛料监控**：常驻循环观测抛料口状态并联动计数（PLC 主控抛料，PC 只观测落盘）——与组装扩展机型的"抛料监控+定时落盘"同源，最小机保留监控、定时落盘按数据量取舍。

## 幂等与甄别结论

框架只约束身份账目与工位循环构成。固定工位数、固定站别 ID、电批通道固定地址不吸收；扫码-组装-出料-解绑身份闭环、联合查询防错配、抛料定时落盘、测高常驻被保留。
