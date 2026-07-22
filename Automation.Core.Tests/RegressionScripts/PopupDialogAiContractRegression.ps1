param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\..\bin\Debug\Automation.exe')
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-NativeFieldRejected {
    param(
        [string]$OperaType,
        [string]$FieldName,
        [Automation.AiOperationCompileContext]$Context
    )

    $fields = [System.Collections.Generic.Dictionary[string, object]]::new()
    $fields.Add($FieldName, '#FFFFFF')
    try {
        [Automation.StructuredOperationCompiler]::Compile(
            $OperaType,
            $fields,
            $Context) | Out-Null
    }
    catch {
        $message = $_.Exception.ToString()
        Assert-True ($message.Contains($FieldName) -and $message.Contains('不是允许字段')) `
            "旧字段[$FieldName]虽被拒绝，但错误契约不明确：$message"
        return
    }
    throw "原生弹框编译器仍接受旧字段：$FieldName"
}

$resolvedAssembly = [IO.Path]::GetFullPath($AssemblyPath)
Assert-True (Test-Path -LiteralPath $resolvedAssembly) "找不到待测程序集：$resolvedAssembly"
[Reflection.Assembly]::LoadFrom($resolvedAssembly) | Out-Null

$popup = [Automation.PopupDialog]::new()
$popupType = $popup.GetType()
foreach ($fieldName in @('PopupBackColor', 'PopupFontColor')) {
    Assert-True ($null -eq $popupType.GetProperty($fieldName)) "弹框模型仍公开旧字段：$fieldName"
}

$nativeContract = [Automation.StructuredOperationCompiler]::BuildContract($popup.OperaType)
$nativeFields = $nativeContract['fields']
$nativeMcpContract = [Automation.StructuredOperationCompiler]::BuildCompactContracts(
    [string[]]@($popup.OperaType))
$nativeMcpJson = $nativeMcpContract.ToString()
foreach ($fieldName in @('PopupBackColor', 'PopupFontColor')) {
    Assert-True ($null -eq $nativeFields.Property($fieldName)) "AI原生弹框Schema仍公开旧字段：$fieldName"
    Assert-True (-not $nativeMcpJson.Contains($fieldName)) "AI原生弹框批量契约仍公开旧字段：$fieldName"
}

$semanticContracts = [Automation.AiOperationCompilerRegistry]::BuildContracts(
    [string[]]@('popup.message', 'popup.variable'))
$semanticJson = $semanticContracts.ToString()
foreach ($fieldName in @('PopupBackColor', 'PopupFontColor')) {
    Assert-True (-not $semanticJson.Contains($fieldName)) "AI语义弹框Schema仍公开旧字段：$fieldName"
}

$variables = [System.Collections.Generic.Dictionary[string, Automation.DicValue]]::new()
$context = [Automation.AiOperationCompileContext]::new(
    0,
    $variables,
    [Automation.AiResourceSnapshot]::new())
Assert-NativeFieldRejected $popup.OperaType 'PopupBackColor' $context
Assert-NativeFieldRejected $popup.OperaType 'PopupFontColor' $context

Write-Host '弹框AI契约回归通过。'
