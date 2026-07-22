# Automation Bridge 源码导航

`Bridge/` 是本机 Named Pipe 边界，把 MCP/前台请求转换为平台读取、诊断、预演、确认和提交操作。

| 位置 | 主要入口 | 职责 |
| --- | --- | --- |
| 根目录 | `AutomationBridgeHost` | 管道监听、连接生命周期和请求分发宿主 |
| 根目录 | `AutomationBridgePreviewClient` | 前台预演确认状态传输 |
| `Service/` | `AutomationBridgeService` 及 partial | 路由、协议、资源投影、流程控制、ChangeSet 和诊断实现 |

`Service/` 内继续使用职责明确的 partial 文件，不再按每个 handler 或资源类型增加目录。主文件只保留组合状态。
