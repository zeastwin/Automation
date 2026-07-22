param(
    [string]$Configuration = "Debug",
    [string]$AssemblyPath
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path $projectRoot "bin\$Configuration\Automation.exe"
}
$assemblyPath = [IO.Path]::GetFullPath($AssemblyPath)
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Automation.exe was not found. Build the project first: $assemblyPath"
}

$assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
$serviceType = $assembly.GetType("Automation.DataBreakpointService", $true)
$bindingFlags = [System.Reflection.BindingFlags]::Instance -bor
    [System.Reflection.BindingFlags]::Public -bor
    [System.Reflection.BindingFlags]::NonPublic

$platformRuntime = [Automation.PlatformRuntime]::new()
$store = $platformRuntime.Stores.Values
$proc = [Automation.Proc]::new()
$proc.head = [Automation.ProcHead]::new()
$proc.head.Id = [Guid]::NewGuid()
$proc.head.Name = "Breakpoint regression process"
$step = [Automation.Step]::new()
$step.Id = [Guid]::NewGuid()
$step.Name = "Breakpoint regression step"
$operation = [Automation.OperationType]::new()
$operation.Id = [Guid]::NewGuid()
$operation.Name = "Write regression variable"
$operation.OperaType = "Regression"
$step.Ops.Add($operation)
$proc.steps.Add($step)

$processes = [System.Collections.Generic.List[Automation.Proc]]::new()
$processes.Add($proc)
$context = [Automation.EngineContext]::new()
$context.Procs = $processes
$context.ValueStore = $store
$context.Maintenance = $platformRuntime.Maintenance
$context.Safety = $platformRuntime.Safety
$context.Readiness = $platformRuntime.Readiness
$context.Paths = $platformRuntime.Paths
$engine = [Automation.ProcessEngine]::new($context)
$service = [Activator]::CreateInstance(
    $serviceType,
    $bindingFlags,
    $null,
    [object[]]@($store, $engine),
    [Globalization.CultureInfo]::InvariantCulture)

try {
    $valueSinkProperty = $store.GetType().GetProperty("DataBreakpointSink", $bindingFlags)
    $engineSinkProperty = $engine.GetType().GetProperty("DataBreakpointSink", $bindingFlags)
    if ($null -ne $valueSinkProperty.GetValue($store, $null) -or
        $null -ne $engineSinkProperty.GetValue($engine, $null)) {
        throw "Breakpoint evaluators must be detached when no rules exist."
    }

    $created = $store.TrySetValue(
        0,
        "RegressionVariable",
        "double",
        "0",
        "Data breakpoint regression",
        "Regression setup",
        "public",
        $null)
    if (-not $created) {
        throw "Failed to create the regression variable."
    }
    $variable = $store.GetValuesSnapshot() | Where-Object Name -eq "RegressionVariable"
    if ($null -eq $variable) {
        throw "The regression variable was not found."
    }

    $addVariableArgs = [object[]]@($variable.Id, $proc.head.Id, $null)
    $added = $serviceType.GetMethod("TryAddVariableBreakpoint", $bindingFlags).Invoke(
        $service,
        $addVariableArgs)
    if (-not $added) {
        throw "Failed to add the variable breakpoint: $($addVariableArgs[2])"
    }
    if ($null -eq $valueSinkProperty.GetValue($store, $null)) {
        throw "The variable breakpoint evaluator was not attached."
    }

    $changed = $store.setValueByIndex(0, 1.0, "0-0-0")
    if (-not $changed) {
        throw "Failed to change the regression variable."
    }
    $rules = $serviceType.GetMethod("GetRulesSnapshot", $bindingFlags).Invoke($service, $null)
    $variableRule = @($rules)[0]
    if ($variableRule.HitCount -ne 1) {
        throw "Unexpected variable breakpoint hit count: $($variableRule.HitCount)"
    }
    if ($variableRule.LastHit.TriggerProcId -ne $proc.head.Id -or
        $variableRule.LastHit.TriggerStepId -ne $step.Id -or
        $variableRule.LastHit.TriggerOperationId -ne $operation.Id) {
        throw "The variable breakpoint did not freeze the expected stable IDs."
    }
    if ($variableRule.LastHit.OldValue -ne "0" -or $variableRule.LastHit.NewValue -ne "1") {
        throw "The variable breakpoint captured incorrect old/new values."
    }

    $setEnabledArgs = [object[]]@($variableRule.Id, $false)
    $null = $serviceType.GetMethod("SetEnabled", $bindingFlags).Invoke($service, $setEnabledArgs)
    $null = $store.setValueByIndex(0, 2.0, "0-0-0")
    $rules = $serviceType.GetMethod("GetRulesSnapshot", $bindingFlags).Invoke($service, $null)
    if (@($rules)[0].HitCount -ne 1) {
        throw "A disabled variable breakpoint was triggered."
    }
    if ($null -ne $valueSinkProperty.GetValue($store, $null)) {
        throw "The variable breakpoint evaluator remained attached after the last rule was disabled."
    }
    $setEnabledArgs[1] = $true
    $null = $serviceType.GetMethod("SetEnabled", $bindingFlags).Invoke($service, $setEnabledArgs)
    $null = $store.setValueByIndex(0, 3.0, "HMI code")
    $rules = $serviceType.GetMethod("GetRulesSnapshot", $bindingFlags).Invoke($service, $null)
    $variableRule = @($rules)[0]
    if ($variableRule.LastHit.TriggerDescription -ne "HMI code" -or
        $variableRule.LastHit.TriggerProcId -ne [Guid]::Empty) {
        throw "An external variable source was not preserved accurately."
    }

    $addStateArgs = [object[]]@(
        $proc.head.Id,
        [Automation.ProcRunState]::Running,
        $proc.head.Id,
        $null)
    $stateAdded = $serviceType.GetMethod("TryAddProcessStateBreakpoint", $bindingFlags).Invoke(
        $service,
        $addStateArgs)
    if (-not $stateAdded) {
        throw "Failed to add the process-state breakpoint: $($addStateArgs[3])"
    }
    if ($null -eq $engineSinkProperty.GetValue($engine, $null)) {
        throw "The process-state breakpoint evaluator was not attached."
    }

    $null = $engine.GetSnapshot(0)
    $engine.GetType().GetMethod("UpdateSnapshot", $bindingFlags).Invoke(
        $engine,
        [object[]]@(
            0,
            $proc.head.Id,
            $proc.head.Name,
            [Automation.ProcRunState]::Running,
            0,
            0,
            $false,
            $null,
            [Automation.ProcTerminationReason]::None))

    $rules = @($serviceType.GetMethod("GetRulesSnapshot", $bindingFlags).Invoke($service, $null))
    $stateRule = $rules | Where-Object { $_.Kind.ToString() -eq "ProcessStateChanged" }
    if ($null -eq $stateRule -or $stateRule.HitCount -ne 1) {
        throw "The process-state breakpoint did not observe the transition."
    }
    if ($stateRule.LastHit.TriggerOperationId -ne $operation.Id -or
        $stateRule.LastHit.PreviousState -ne [Automation.ProcRunState]::Ready -or
        $stateRule.LastHit.CurrentState -ne [Automation.ProcRunState]::Running) {
        throw "The process-state breakpoint captured incorrect location or state facts."
    }

    Write-Host "DataBreakpointRegression: PASS"
}
finally {
    if ($service -is [IDisposable]) {
        $service.Dispose()
    }
    $engine.Dispose()
}
