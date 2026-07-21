param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\bin\Debug\Automation.exe')
)

$ErrorActionPreference = 'Stop'
$AssemblyPath = [IO.Path]::GetFullPath($AssemblyPath)
if (-not (Test-Path -LiteralPath $AssemblyPath)) {
    throw "未找到待验证程序集：$AssemblyPath"
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$formType = $assembly.GetType('Automation.FrmDataGrid', $true)
$operationType = $assembly.GetType('Automation.HomeRun', $true)
$flags = [Reflection.BindingFlags]::Instance -bor
    [Reflection.BindingFlags]::Public -bor
    [Reflection.BindingFlags]::NonPublic
$pendingField = $formType.GetField('operationSelectionRefreshPending', $flags)
$selectedRowField = $formType.GetField('iSelectedRow', $flags)
if ($null -eq $pendingField -or $null -eq $selectedRowField) {
    throw '指令选择合并刷新契约缺失。'
}

$form = [Activator]::CreateInstance($formType)
$grid = $formType.GetField('dataGridView1', $flags).GetValue($form)
$operations = [Collections.ArrayList]::new()
$null = $operations.Add([Activator]::CreateInstance($operationType))
$null = $operations.Add([Activator]::CreateInstance($operationType))

try {
    $form.ShowInTaskbar = $false
    $form.Size = [Drawing.Size]::new(640, 320)
    $grid.DataSource = $operations
    $form.Show()
    [Windows.Forms.Application]::DoEvents()

    $grid.SelectSingle(0)
    $grid.SelectSingle(1)
    if (-not $pendingField.GetValue($form)) {
        throw '同一消息周期内的选择变化未进入合并刷新队列。'
    }

    [Windows.Forms.Application]::DoEvents()
    if ($pendingField.GetValue($form)) {
        throw '合并刷新完成后仍残留待处理状态。'
    }
    if ($selectedRowField.GetValue($form) -ne 1) {
        throw "合并刷新未采用最终选中行：$($selectedRowField.GetValue($form))。"
    }

    Write-Host '指令选择合并刷新回归通过。'
}
finally {
    $form.Close()
    $form.Dispose()
}
