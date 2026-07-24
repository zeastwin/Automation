---
name: automation-mcp-cli
description: 在 Cli 工具接入模式下经 shell 调用 Automation 平台工具时使用。提供 MCP CLI 的发现、Schema 读取、调用语法、参数 JSON 引用、返回结构与退出码事实。流程编写方法由 automation-process-authoring 承担，本 Skill 只承载调用机制。
---

# Automation MCP CLI 调用机制

## 命令

- 入口：环境变量 `AUTOMATION_MCP_CLI_PATH` 指向 Automation.McpServer.exe；当前工具 Profile 在环境变量 `AUTOMATION_MCP_PROFILE`。
- shell 是 Git Bash：一律以 `"$AUTOMATION_MCP_CLI_PATH"` 直接引用该变量（带引号，路径含反斜杠）。每次 shell 调用都是新进程，自定义 `export` 的别名不会保留，也不需要先 `echo` 解析成字面量再硬编码路径。cmd 的 `%AUTOMATION_MCP_CLI_PATH%` 会被 bash 按作业符解析（报 `fg: no job control`），PowerShell 的 `$env:AUTOMATION_MCP_CLI_PATH` 与 `&` 调用符是 bash 语法错误；两种写法在此环境都不可用。
- `"$AUTOMATION_MCP_CLI_PATH" cli list`：当前 Profile 开放的工具名与描述。
- `"$AUTOMATION_MCP_CLI_PATH" cli list --full`：附带每个工具的 inputSchema。
- `"$AUTOMATION_MCP_CLI_PATH" cli schema <name>`：单个工具的描述与 inputSchema。调用不熟悉的工具前先读取。`<name>` 是工具名；语义 kind（如 `variable.set`）不是工具，其字段在 `preview_change_set` Schema 的 `semanticOperation` oneOf 中。
- `"$AUTOMATION_MCP_CLI_PATH" cli call <name> --json '<argsJson>'`：调用工具并输出其 JSON 返回；`--json` 缺省为 `{}`。

## 参数 JSON 的 bash 引用

参数键与工具 inputSchema 一致（匹配不区分大小写）；可选参数省略即使用方法签名默认值。内联 JSON 用单引号整体包裹，内部双引号原样书写、不转义：

```bash
"$AUTOMATION_MCP_CLI_PATH" cli call get_proc_overview --json '{"procIndex":6}'
```

`preview_change_set` 等大体积参数写入临时文件后走 `--json-file`，避免命令行长度上限：

```bash
cat > /tmp/cs.json <<'EOF'
{"changeSet":{"title":"阶段说明","actions":[...]}}
EOF
"$AUTOMATION_MCP_CLI_PATH" cli call preview_change_set --json-file /tmp/cs.json
```

- 工具返回一律是 JSON：业务结果与业务错误（`ok:false` 及 `recovery`/`allowedTransitions`）都在 stdout 的 JSON 内，按其中的结构化字段恢复。
- 退出码：0=调用已执行；1=本地故障（如 Bridge 未运行，先确认平台编辑器已打开）；2=用法错误（未知工具、缺必填参数、JSON 无效），说明在 stderr。
- 返回体积较大时重定向到临时文件再分段读取，不要把超大 JSON 直接放入对话。

## 与 Tools 模式的关系

- 工具集合、Schema 与 Bridge 校验和 MCP HTTP 模式完全同源；只有调用通道不同。
- `preview_change_set` 返回 `previewId` 且 `confirmed=false` 时，前台仍会弹出确认窗；用户确认后仅以该 `previewId` 调用 `apply_change_set`。
- `cli call` 返回未开放工具时，用 `cli list` 核对当前 Profile 的可用集合，不要猜测工具名。
- 迁移/平台配置工具（`preview_motion_io_configuration` 等 8 个）只在 Editor Profile 且完全权限开启时开放：CLI 加 `--full-permission`，或环境变量 `AUTOMATION_MCP_FULL_PERMISSION=1`（由前台"完全权限"开关注入，仅 Editor 生效）。
