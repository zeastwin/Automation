# ICT/FCT 测试工站设备框架

## 设备画像

对 PCB/半成品执行电测（ICT/FCT/动静态测试）的工站机：流水线进出料，相机定位后压合测试，结果过站 MES 并上传数据链路；测试执行由测试仪器/PLC 主控，PC 程序承担定位视觉、过站事务、数据上传与报表呈现。 js 系 ICT 产线主力形态（Link、ACI、Clean-Flux、Dispense-Flux、ICT 上下料同族）。

## 功能单元构成

| 单元 | 职责 | 典型流程 | 引用功能块 |
|---|---|---|---|
| 流水线进出料 | 载具流入/流出、下流水线 | 流水线流程、下流水线 | transfer.station-handoff |
| 定位视觉 | 上/下压合前 CCD 定位、定位复检 | 上CCD定位、下CCD定位、下CCD复检、相机拍照 | vision.positioning-and-correct |
| 测试衔接 | 扫码身份、MES 查询资格、过站触发测试 | Carrier 扫码、MES查询、过站、动静态测试 | transaction.submit-trace-record、communication.plc-pc-channel |
| 极限防呆 | 测试前边界校验（防错位压合） | 极限防呆处理 | interlock.safety-gate-watchdog |
| 数据呈现 | 测试数据表格化、报表上传、集中复判 | 创建表格、数据处理、上传报表数据、集中复判 | custom-function.code-process-collaboration |
| 数据链路 | MES/PDCA 上传补传、Hive、心跳、点位治理 | PDCA数据上传/补传、Hive报警监控、心跳包、PLC修改记录 | communication.pdca-upload-retransmit、communication.hive-alarm-collection、communication.heartbeat-and-connection、maintenance.plc-point-governance |
| 系统服务 | 初始化、启动、复位、门禁、灯控、状态 | 初始化、启动流程、复位、系统门禁/灯控/状态 | reset.system-and-station-reinit、monitoring.system-service-listeners |
| 空跑配套 | 进料/拍照/出料三段空跑 | 空跑-进料、空跑-拍照、空跑-出料 | quality.dry-run-and-grr-pair |

## 单元间衔接

物料流：载具流入→扫码绑定→CCD 定位→压合测试（仪器/PLC 主控，PC 等结果）→下流水线。身份流：扫码绑定后凭身份 MES 查询测试资格，测试结果凭身份过站上传。数据流：测试结果→数据处理/表格→报表与 PDCA 上传（补传兜底）；测试失败集中复判。安全流：极限防呆在压合前拦截错位载具。

## 框架变化点

单工位 vs 双工位（A/B 对称）；测试主控方（仪器自带 vs PLC 交互）；附加工艺（Clean-Flux 清洗、Dispense-Flux 助焊剂点胶作为前置于测试的工艺段）；有无集中复判/AGV/回流。

## 搭建顺序

系统服务 → 流水线进出料 → 扫码与 MES 资格查询 → CCD 定位 → 极限防呆 → 测试衔接（过站触发+等结果）→ 数据处理与上传链路 → 空跑三段收尾。

## 完成证据

一个载具"流入→扫码→定位→测试→过站→流出"走通且身份账目一致；错位载具被极限防呆拦截；测试结果可回查到报表与 PDCA 记录；空跑三段旁路上报走通。

## 关联块清单

- `transfer.station-handoff`
- `vision.positioning-and-correct`
- `vision.plc-assisted-vision`
- `transaction.submit-trace-record`
- `communication.plc-pc-channel`
- `communication.pdca-upload-retransmit`
- `communication.hive-alarm-collection`
- `communication.heartbeat-and-connection`
- `maintenance.plc-point-governance`
- `interlock.safety-gate-watchdog`
- `custom-function.code-process-collaboration`
- `reset.system-and-station-reinit`
- `monitoring.system-service-listeners`
- `quality.dry-run-and-grr-pair`
- `variables.design`
- `data-struct.design`
- `observability.design`

## 反模式

- **测试结果不绑身份就上传**：测试数据找不到载体归属，追溯断链。扫码绑定先于测试。
- **压合前不做边界防呆**：错位载具直接压合，损坏探针与载具。极限防呆是压合前置。
- **PC 等结果变成 PC 控测试**：PC 侧开始编排测试时序，主从颠倒。测试由仪器/PLC 主控，PC 只交接结果。
- **空跑三段不全**：只空跑进料不空跑拍照/出料，链路只验证一半。三段空跑对应三段链路。
- **报表与过站混一条链**：报表上传阻塞过站上传（或相反），一条断全断。呈现类与事务类上传分链路。

## 黄金样例

> 来源：`js-JS_ICT_Link_V3-0_20250212`、`js-JS_ICT_ACI_20250408`。测试工站的落地参考。

**Link 形态流程族**（22 个流程）：复位、启动｜流水线流程、下流水线、空跑-进料/拍照/出料｜相机拍照、动静态测试、极限防呆处理、启动-直通｜数据处理、创建表格、MES是否开启｜系统灯控、系统状态、系统门禁、门禁弹框。三段空跑与三段主链（进料→拍照→测试/出料）一一对应——主链有什么段，空跑就有什么段，这是空跑完整性的直接判据。

**ACI 形态流程族**（20 个流程）：初始化、启动流程｜上CCD定位、下CCD定位、下CCD复检、过站、Carrier 扫码、MES查询、MES过站|PDCA上传预处理｜Hive报警监控、心跳包、PDCA数据上传、PDCA数据补传、保存PLC点位、PLC修改记录｜回流、集中复判、上传报表数据、AGV流程。ACI 比 Link 多出"定位→复检"双保险与完整治理单元——工位越复杂，视觉复检与点位治理越不能省。

**过站衔接骨架**：Carrier 扫码（绑定身份）→ MES查询（资格判定）→ CCD 定位（压合位置确认）→ 过站（触发测试并等结果）→ MES过站|PDCA上传预处理（结果凭身份上传）。每一步都以身份为键串起来——身份断在任一环，后面的上传全部无主。

## 幂等与甄别结论

框架只约束测试链路的单元构成与身份衔接。历史 NULL 流程、重复的空跑变体、写死的测试参数不吸收；三段空跑、定位复检双保险、极限防呆前置、上传/补传成对、集中复判结构被保留。
