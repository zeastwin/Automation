# 运动控制源码导航

`MotionControl/` 对接运动控制卡并向平台运行时提供统一运动能力。该层不显示窗体和弹框；安全判断由平台安全协调器与调用方共同执行。

| 目录 | 主要入口 | 职责 |
| --- | --- | --- |
| `Core/` | `IMotionRuntime`、`MotionCtrl`、`AxisStatusCache` | 运行契约、运动协调与轴状态缓存 |
| `Drivers/` | `LTDMC`、`LeiSei3000` | 雷赛控制卡 SDK 适配 |

新增控制卡实现进入 `Drivers/`；跨控制卡一致的运行语义进入 `Core/`。
