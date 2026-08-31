# PLC 主控-PC 采集代理设备框架

## 设备画像

工艺动作由 PLC/机器人主控执行，PC（NsDemo 程序）不控制轴与机构，只承担"信息代理"职责：与 PLC 按通道交互数据、视觉辅助判定、曲线/视频采集、MES/PDCA/Hive 数据链路、心跳监测。流程群里几乎全是常驻循环通道，没有工艺动作流程。

## 功能单元构成

| 单元 | 职责 | 典型流程 | 引用功能块 |
|---|---|---|---|
| 初始化与连接 | 建立通讯对象、加载配置 | 初始化流程 | communication.plc-pc-channel |
| PLC-PC 通道群 | 按业务名一条通道一个流程，循环自启 | PLC-PC(进站/出站/下料/过站/扫码/其它) | communication.plc-pc-channel |
| 视觉辅助族 | 扫码/拍照定位/复检/测量/点检/标定，结果回传 PLC | 扫码A/B、定位、复检、标定 | vision.plc-assisted-vision |
| 数据采集族 | 压力/保压曲线、电批曲线、视频录制/截图 | 按工位+保压点复制通道 | monitoring.curve-video-collection |
| 数据上传链路 | PDCA 上传+漏传补传、MES 查询过站 | PDCA 上传系统、MES过站 | communication.pdca-upload-retransmit |
| 链路保障 | PLC/MES/PDCA 心跳、Hive 采集与报警 | 心跳包×N、Hive采集系统 | communication.heartbeat-and-connection、communication.hive-alarm-collection |
| 工程治理 | PLC 点位备份/修改记录、监控设备更改、版本变更 | 只读+记录小流程族 | maintenance.plc-point-governance |

## 单元间衔接

通道群各自常驻循环、彼此独立，通过共享变量与通讯对象交换数据；心跳独立于业务通道持续探测，断链只报警不替通道重试；上传链路本地留痕，补传流程单独常驻，不混入业务通道。视觉结果写给 PLC 后即完成本流程职责，不等待 PLC 工艺结果。

## 框架变化点

通道数量与命名（按工位/保压点/相机拆分粒度）；双工位 A/B 对称复制；有无视觉、曲线、视频采集；有无回流扫码；MES 本地直连 vs Hive 代理；有无称重/温度等模拟量读取。

## 搭建顺序

初始化与连接 → 通道群骨架（先建 1 条验证节拍，再按对称复制）→ 心跳与 Hive 保障 → 视觉辅助族 → 曲线/视频采集 → PDCA/MES 上传与补传 → 点位治理收尾。

## 完成证据

每条通道循环独立运行互不阻塞；断链恢复后通道自动续跑；补传不产生重复上传；视觉结果与 PLC 侧确认对得上；心跳丢失有报警且不影响通道数据一致性。

## 关联块清单

- `communication.plc-pc-channel`
- `communication.heartbeat-and-connection`
- `communication.hive-alarm-collection`
- `communication.pdca-upload-retransmit`
- `monitoring.curve-video-collection`
- `vision.plc-assisted-vision`
- `maintenance.plc-point-governance`
- `variables.design`
- `data-struct.design`
- `custom-function.code-process-collaboration`
- `observability.design`

## 反模式

- **通道建一条抄一条**：第一通道没验证节拍就逐份复制，错误翻倍。先建一条验证再对称复制。
- **心跳缺位**：通道群全建了但心跳没建，断链时数据停更无人知。心跳先于通道收尾建立。
- **补传流程省略**：只建上传不建补传，断链期间数据静默丢失。上传与补传成对。
- **PC 抢主控职责**：视觉代理流程顺手做工艺判断甚至动作。PC 只回传结果，工艺归 PLC。

## 黄金样例

> 来源：`NA-20250411`。视觉辅助型采集代理的落地参考。

**流程族清单**（22 个流程按单元归组）：初始化、主流程、PLC-PC交互｜扫码A、排线定位A、压块定位A、复检A、点检A、A相机自动标定｜扫码B、排线定位B、压块定位B、复检B、点检B、B相机自动标定｜读取设备温度｜PDCA数据上传、PDCA数据补传、HIVE采集、心跳、PLC数据监控｜空跑数据读取。A/B 两套视觉任务完全对称（扫码→定位→复检→点检→标定各 5 个流程成对），通道与数据链路共用——"视觉辅助族按工位对称复制、数据链路全机一套"的结构在这里最清晰。

**单视觉任务骨架**（以排线定位A为例）：等 PLC 触发 → 取像 → 解算偏差 → 结果写入约定数据区并置完成标志。四个环节无一处驱动机构——PLC 拿偏差去动轴，PC 写完就结束本次任务。这就是"PC 不抢主控职责"的流程级落实。

**数据链路三件套的分工**：PDCA上传+补传（业务数据，留痕异步）、HIVE采集（报警旁路）、心跳（链路存活）。三者互不依赖、任一故障不影响另外两条——删掉任何一条，剩下两条照常工作，这就是链路分离的可测试证据。

## 幂等与甄别结论

框架只约束"通道分流程+常驻循环+链路分离"的构成。历史固定信号编号、通道逐份复制中的复制差异、A/B 工位复制未改名的残留不吸收；通道按业务拆分、心跳/采集/上传链路分离、点位治理小流程族、PDCA 上传补传成对被保留。A/B 或 1/2 复制第二份时必须核对变量前缀与通道地址，不复用第一份标识符。
