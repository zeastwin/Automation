param(
    [ValidateSet("smoke", "gate", "formal24h", "formal72h")]
    [string]$Profile = "formal72h",
    [int]$SoakMinutes = 4320,
    [int]$JitterMinutes = 60,
    [int]$MemoryMinutes = 1440,
    [int]$HandleMinutes = 1440,
    [int]$SampleMs = 30000,
    [double]$MaxMemoryDriftRatio = 0.10,
    [double]$MaxMemorySlopeMbPerHour = 8.0,
    [int]$MaxHandleDelta = 300,
    [int]$MaxThreadDelta = 20,
    [int]$MaxGdiDelta = 50,
    [double]$MaxHandleSlopePerHour = 12.5,
    [double]$MaxThreadSlopePerHour = 0.84,
    [double]$MaxGdiSlopePerHour = 2.1,
    [double]$MinTrendHours = 0.5,
    [int]$MinCyclePerMinute = 1,
    [int]$MaxAlarmLatencyMs = 3000,
    [int]$JitterCooldownMs = 20,
    [int]$JitterMaxBurst = 3,
    [double]$JitterBurstProbability = 0.30,
    [Nullable[int]]$RandomSeed = $null
)

$ErrorActionPreference = "Stop"

$env:AUTOMATION_ENABLE_LONGRUN = "1"
$env:AUTOMATION_P2LR_PROFILE = $Profile
$env:AUTOMATION_P2LR_SOAK_MINUTES = $SoakMinutes.ToString()
$env:AUTOMATION_P2LR_JITTER_MINUTES = $JitterMinutes.ToString()
$env:AUTOMATION_P2LR_MEMORY_MINUTES = $MemoryMinutes.ToString()
$env:AUTOMATION_P2LR_HANDLE_MINUTES = $HandleMinutes.ToString()
$env:AUTOMATION_P2LR_SAMPLE_MS = $SampleMs.ToString()
$env:AUTOMATION_P2LR_MAX_MEMORY_DRIFT_RATIO = $MaxMemoryDriftRatio.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:AUTOMATION_P2LR_MAX_MEMORY_SLOPE_MB_PER_HOUR = $MaxMemorySlopeMbPerHour.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:AUTOMATION_P2LR_MAX_HANDLE_DELTA = $MaxHandleDelta.ToString()
$env:AUTOMATION_P2LR_MAX_THREAD_DELTA = $MaxThreadDelta.ToString()
$env:AUTOMATION_P2LR_MAX_GDI_DELTA = $MaxGdiDelta.ToString()
$env:AUTOMATION_P2LR_MAX_HANDLE_SLOPE_PER_HOUR = $MaxHandleSlopePerHour.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:AUTOMATION_P2LR_MAX_THREAD_SLOPE_PER_HOUR = $MaxThreadSlopePerHour.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:AUTOMATION_P2LR_MAX_GDI_SLOPE_PER_HOUR = $MaxGdiSlopePerHour.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:AUTOMATION_P2LR_MIN_TREND_HOURS = $MinTrendHours.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:AUTOMATION_P2LR_MIN_CYCLE_PER_MINUTE = $MinCyclePerMinute.ToString()
$env:AUTOMATION_P2LR_MAX_ALARM_LATENCY_MS = $MaxAlarmLatencyMs.ToString()
$env:AUTOMATION_P2LR_JITTER_COOLDOWN_MS = $JitterCooldownMs.ToString()
$env:AUTOMATION_P2LR_JITTER_MAX_BURST = $JitterMaxBurst.ToString()
$env:AUTOMATION_P2LR_JITTER_BURST_PROBABILITY = $JitterBurstProbability.ToString([System.Globalization.CultureInfo]::InvariantCulture)
if ($RandomSeed -ne $null) {
    $env:AUTOMATION_P2LR_RANDOM_SEED = $RandomSeed.Value.ToString()
} else {
    Remove-Item Env:AUTOMATION_P2LR_RANDOM_SEED -ErrorAction SilentlyContinue
}

Write-Host "开始执行 P2 长期压测（Category=LongRun）..."
Write-Host "Profile=$Profile SoakMinutes=$SoakMinutes JitterMinutes=$JitterMinutes MemoryMinutes=$MemoryMinutes HandleMinutes=$HandleMinutes SampleMs=$SampleMs"

dotnet test .\tests\Automation.P0Tests\Automation.P0Tests.csproj --filter "TestCategory=LongRun" -- NUnit.NumberOfTestWorkers=1
