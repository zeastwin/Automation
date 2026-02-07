# SourceDLL 使用说明

- 本目录用于存放需要随程序输出的本地 DLL 与相关文件。
- 构建 `Automation.csproj` 后，会自动把本目录下所有文件复制到输出目录（如 `bin/Debug/`），并保留子目录结构。

## 典型放置方式

- `SourceDLL/LTDMC.dll` -> `bin/Debug/LTDMC.dll`
- `SourceDLL/Config/card_0.ini` -> `bin/Debug/Config/card_0.ini`

## 约束

- 仅放运行所需的外部文件，避免把无关大文件放入此目录。
- 如果文件名冲突，以构建时复制到输出目录的结果为准。
