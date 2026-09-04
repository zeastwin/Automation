# SourceDLL 使用说明

- 本目录用于存放需要随程序输出的本地 DLL 与相关文件。
- 构建 `Automation.csproj` 后，会自动把本目录下所有文件复制到输出目录（如 `bin/Debug/`），并保留子目录结构。

## 典型放置方式

- `SourceDLL/LTDMC.dll` -> `bin/Debug/LTDMC.dll`
- `SourceDLL/IMC100API.dll` -> `bin/Debug/IMC100API.dll`（汇川机器人 x64 3.14.2.0）
- `Assets/MotionControl/LeiSai/card_0.ini` -> `bin/Debug/Assets/MotionControl/LeiSai/card_0.ini`；首次初始化时再复制到 `PlatformRuntime.Paths.ConfigPath/card_0.ini`

## 约束

- 仅放运行所需的外部文件，避免把无关大文件放入此目录。
- 如果文件名冲突，以构建时复制到输出目录的结果为准。

## 3.0 基线

- `LTDMC.dll` 直接取自 3.0 `bin/AnyCPU/LTDMC.dll`，版本 `2.4.0.5`，SHA-256：`89264EE83A682CB85EABAE21F210A19EDD88FEBCB04B5CE0BBF06439CAB9A321`。
- `IMC100API.dll` 直接取自 3.0 `bin/AnyCPU/IMC100API.dll`，版本 `3.14.2.0`，SHA-256：`289D772D5197854797C49E51FE18402C927ADA9B185F64CC0C1A916BEAA73A28`。
- `card_0.ini` 直接取自 3.0 `bin/AnyCPU/Config/card_0.ini`，SHA-256：`97CFE875412AFA7E8F5A2D3BDFEAB5B0D25BECDA3EF58C49338FC13A4BD65A7B`。
