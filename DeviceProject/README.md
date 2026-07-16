# MachineApp 设备工程

这是可直接打开和生成的设备工程。HMI 保留首页、调试、报警历史和产能四个页面入口；首页保留左侧状态栏，其余内容区域保持空白，供设备项目按需设计。工程同时包含设备自定义函数入口和独立配置目录。

平台、流程引擎、通讯与运动控制由二进制运行包提供，不把平台源码带入设备工程。设备工程只编译自身 HMI 和自定义函数，因此界面代码写错不会污染平台源码。

## 开发方式

1. 平台仓库内开发时，先生成 `Automation.csproj`，工程默认读取 `..\bin\Debug`。
2. 独立交付时，把平台运行包放入工程的 `Platform\`，或者构建时传入 `PlatformRuntimeDir`。
3. 设备代码只通过 `IAutomationPlatform` 使用 `platform.Values`、`platform.Processes` 等公开入口。
4. `Config\` 属于设备工程；平台运行包不会覆盖设备配置。
5. 自定义函数统一在 `Hmi\DeviceCustomFunctions.cs` 注册，不引用 `SF`、平台窗体、Store 或运动控制内部对象。

## 构建

```powershell
dotnet build MachineApp.csproj -c Debug -p:Platform=x64
```

平台仓库内刷新独立运行包：

```powershell
.\RefreshPlatformRuntime.ps1 -Configuration Debug
```

构建目标会校验运行包清单并复制平台运行文件，但排除平台 PDB、平台 Config 和任何 Logs。设备工程自己的 PDB 保留用于调试 HMI。构建不会启动平台、流程或硬件，也不会留下临时验证工程。

生成结果位于 `bin\x64\Debug\`，入口是 `MachineApp.exe`。设备配置和流程位于同目录的 `Config\`，日志仍统一写入 `D:\AutomationLogs\`。
