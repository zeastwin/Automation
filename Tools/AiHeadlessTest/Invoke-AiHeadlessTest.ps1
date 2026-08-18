# ==========================================================================================
# AI Headless 回归测试客户端
# ==========================================================================================
# 用途：通过平台 Bridge 命名管道触发无人值守 AI 回归测试（/bridge/ai-test/* 端点），
#       封装 4 字节小端长度前缀 + UTF-8 JSON 帧协议，调用方无需了解管道细节。
#
# 前置条件：
#   1. Automation.exe 已正常启动（编辑器或 HMI 模式均可），AI 基础设施就绪；
#   2. Goose 运行环境与 Provider 凭据已配置（平台内 AI 助手可用）。
#
# 用法示例：
#   # 按语句文件跑完整测试（推荐，语句文件格式见 prompts-sample.json）
#   .\Invoke-AiHeadlessTest.ps1 -PromptsFile .\prompts-sample.json
#
#   # 直接传语句（单句或多句）
#   .\Invoke-AiHeadlessTest.ps1 -Prompts "新建一个搬运测试流程……不要自动启动。"
#
#   # 自定义单句超时（分钟，默认 15，范围 1..120）
#   .\Invoke-AiHeadlessTest.ps1 -PromptsFile .\prompts-sample.json -TurnTimeoutMinutes 20
#
#   # 仅查询当前测试状态（不启动）
#   .\Invoke-AiHeadlessTest.ps1 -StatusOnly
#
# 输出：
#   - 过程事件写入 D:\AutomationLogs\AIExecution\Analysis（ai_headless_test.* 事件）
#   - Markdown 报告写入 D:\AutomationLogs\AIExecution\HeadlessTests
#   - 脚本退出码：0 全部通过；1 启动失败或存在失败语句；2 参数错误
#
# 安全须知：
#   测试以 autoApprove=true 运行——预演会被自动确认并产生真实配置写入（新建/修改流程、
#   登记点位等）。语句内容必须与手工测试一样经过确认，请勿在产线运行期间随意触发。
# ==========================================================================================

[CmdletBinding()]
param(
    [string[]]$Prompts,
    [string]$PromptsFile,
    [int]$TurnTimeoutMinutes = 15,
    [switch]$StatusOnly,
    [int]$PollIntervalSeconds = 10
)

$ErrorActionPreference = 'Stop'
$PipeName = 'AutomationBridgePipe'

# ---------- Bridge 管道帧协议 ----------

function Send-BridgeRequest {
    param([string]$Path, [object]$Body)

    $envelope = @{
        requestId = [guid]::NewGuid().ToString('N')
        method    = 'POST'
        path      = $Path
        bodyJson  = ($Body | ConvertTo-Json -Depth 8 -Compress)
    } | ConvertTo-Json -Compress

    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(
        '.', $PipeName, [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(30000)
        $payload = [System.Text.Encoding]::UTF8.GetBytes($envelope)

        $lengthPrefix = [BitConverter]::GetBytes([int]$payload.Length)
        $pipe.Write($lengthPrefix, 0, 4)
        $pipe.Write($payload, 0, $payload.Length)
        $pipe.Flush()

        $header = New-Object byte[] 4
        $read = 0
        while ($read -lt 4) {
            $n = $pipe.Read($header, $read, 4 - $read)
            if ($n -le 0) { throw "Bridge 连接提前关闭。" }
            $read += $n
        }
        $length = [BitConverter]::ToInt32($header, 0)
        if ($length -lt 0) { throw "Bridge 响应长度非法。" }

        $buffer = New-Object byte[] $length
        $read = 0
        while ($read -lt $length) {
            $n = $pipe.Read($buffer, $read, $length - $read)
            if ($n -le 0) { throw "Bridge 连接提前关闭。" }
            $read += $n
        }

        $response = [System.Text.Encoding]::UTF8.GetString($buffer) | ConvertFrom-Json
        $statusCode = if ($null -ne $response.statusCode) { [int]$response.statusCode }
                      elseif ($null -ne $response.StatusCode) { [int]$response.StatusCode }
                      else { 500 }
        $bodyJson = if ($null -ne $response.bodyJson) { $response.bodyJson }
                    elseif ($null -ne $response.BodyJson) { $response.BodyJson }
                    else { '' }
        $body = $null
        if ($bodyJson) { $body = $bodyJson | ConvertFrom-Json }

        return @{ StatusCode = $statusCode; Body = $body }
    }
    finally {
        $pipe.Dispose()
    }
}

function Read-PromptsFromFile {
    param([string]$Path)
    if (-not (Test-Path $Path)) { throw "语句文件不存在：$Path" }
    $root = (Get-Content $Path -Raw -Encoding UTF8 | ConvertFrom-Json)
    # @() 包裹防止管道把单元素数组折叠成标量，导致 prompts 序列化成字符串。
    $items = @(@($root.prompts) | Where-Object { $_ })
    if ($items.Count -lt 1) { throw "语句文件中没有有效语句：$Path" }
    return $items
}

# ---------- 主流程 ----------

try {
    if ($StatusOnly) {
        $result = Send-BridgeRequest -Path '/bridge/ai-test/status' -Body @{}
        if ($result.StatusCode -ne 200) {
            Write-Host ("查询失败 [{0}]：{1}" -f $result.StatusCode, ($result.Body | ConvertTo-Json -Depth 6)) -ForegroundColor Red
            exit 1
        }
        $result.Body.data | ConvertTo-Json -Depth 6
        exit 0
    }

    # 解析语句来源
    if ($Prompts) {
        $promptList = @(@($Prompts) | Where-Object { $_ })
    }
    elseif ($PromptsFile) {
        # @() 包裹函数返回值：单元素数组经函数返回管道会折叠成标量。
        $promptList = @(Read-PromptsFromFile -Path $PromptsFile)
    }
    else {
        Write-Host "参数错误：必须提供 -Prompts 或 -PromptsFile 之一（或使用 -StatusOnly）。" -ForegroundColor Red
        exit 2
    }
    if ($promptList.Count -lt 1) { Write-Host "参数错误：语句列表为空。" -ForegroundColor Red; exit 2 }

    Write-Host ("触发 headless 测试：{0} 句，单句超时 {1} 分钟..." -f $promptList.Count, $TurnTimeoutMinutes)
    $start = Send-BridgeRequest -Path '/bridge/ai-test/start' -Body @{
        autoApprove         = $true
        prompts             = $promptList
        turnTimeoutMinutes  = $TurnTimeoutMinutes
    }
    if ($start.StatusCode -ne 200) {
        $msg = if ($start.Body -and $start.Body.message) { $start.Body.message } else { ($start.Body | ConvertTo-Json -Depth 4) }
        Write-Host ("启动失败 [{0}] {1}：{2}" -f $start.StatusCode, $start.Body.code, $msg) -ForegroundColor Red
        exit 1
    }
    Write-Host ("已启动 runId={0}" -f $start.Body.data.runId) -ForegroundColor Green

    # 轮询直到完成
    while ($true) {
        Start-Sleep -Seconds $PollIntervalSeconds
        $status = Send-BridgeRequest -Path '/bridge/ai-test/status' -Body @{}
        if ($status.StatusCode -ne 200) {
            Write-Host ("状态查询失败 [{0}]，继续轮询..." -f $status.StatusCode) -ForegroundColor Yellow
            continue
        }
        $s = $status.Body.data
        $stamp = Get-Date -Format 'HH:mm:ss'
        if ($s.running) {
            Write-Host ("[{0}] 进行中：{1}/{2} 句（当前第 {3} 句）..." -f $stamp, $s.completedPrompts, $s.totalPrompts, ($s.currentPromptIndex + 1))
            continue
        }
        if ([string]::IsNullOrEmpty($s.runId)) {
            Write-Host "测试尚未开始或状态不可用，继续轮询..." -ForegroundColor Yellow
            continue
        }

        # 结束：输出摘要
        $color = if ($s.failedCount -eq 0) { 'Green' } else { 'Red' }
        Write-Host ""
        Write-Host ("========== 测试完成 ==========") -ForegroundColor $color
        Write-Host ("通过 {0}，失败 {1}（共 {2} 句）" -f $s.passedCount, $s.failedCount, $s.totalPrompts)
        if ($s.failedCount -gt 0 -and $s.lastPromptFailure) {
            Write-Host ("最近失败原因：{0}" -f $s.lastPromptFailure) -ForegroundColor Red
        }
        if ($s.lastError) {
            Write-Host ("运行错误：{0}" -f $s.lastError) -ForegroundColor Red
        }
        if ($s.reportPath -and (Test-Path $s.reportPath)) {
            Write-Host ("报告：{0}" -f $s.reportPath) -ForegroundColor Cyan
        }
        if ($s.failedCount -gt 0) { exit 1 } else { exit 0 }
    }
}
catch {
    Write-Host ("执行异常：{0}" -f $_.Exception.Message) -ForegroundColor Red
    Write-Host "请确认 Automation.exe 已启动且 AI 基础设施就绪（命名管道：$PipeName）。"
    exit 1
}
