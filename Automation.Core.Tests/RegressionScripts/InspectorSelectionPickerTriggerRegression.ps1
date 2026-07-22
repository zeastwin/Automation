param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\..\bin\Debug\Automation.exe')
)

$ErrorActionPreference = 'Stop'
$AssemblyPath = [IO.Path]::GetFullPath($AssemblyPath)
if (-not (Test-Path -LiteralPath $AssemblyPath)) {
    throw "未找到待验证程序集：$AssemblyPath"
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class InspectorPickerNativeTest
{
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter);
}
'@

$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$comboType = $assembly.GetType('Automation.InspectorComboBox', $true)
$flags = [Reflection.BindingFlags]::Instance -bor
    [Reflection.BindingFlags]::Public -bor
    [Reflection.BindingFlags]::NonPublic
$usePickerProperty = $comboType.GetProperty('UseSelectionPicker', $flags)
$pickerEvent = $comboType.GetEvent('SelectionPickerRequested', $flags)
if ($null -eq $usePickerProperty -or $null -eq $pickerEvent) {
    throw '检查器选择器触发契约缺失。'
}

$form = New-Object Windows.Forms.Form
$combo = [Activator]::CreateInstance($comboType, $true)
$state = [pscustomobject]@{ Count = 0 }
$handler = [EventHandler]{
    param($sender, $eventArgs)
    $state.Count++
}

try {
    $form.ShowInTaskbar = $false
    $form.Size = New-Object Drawing.Size(320, 100)
    $combo.Location = New-Object Drawing.Point(20, 20)
    $combo.Size = New-Object Drawing.Size(260, 30)
    $combo.DropDownStyle = [Windows.Forms.ComboBoxStyle]::DropDownList
    $null = $combo.Items.Add('测试项')
    $form.Controls.Add($combo)
    $null = $form.Handle
    $null = $combo.Handle

    $usePickerProperty.SetValue($combo, $true, $null)
    $pickerEvent.GetAddMethod($true).Invoke($combo, @($handler))

    $mousePosition = [IntPtr]((15 -shl 16) -bor 20)
    $null = [InspectorPickerNativeTest]::SendMessage(
        $combo.Handle,
        0x0201,
        [IntPtr]::Zero,
        $mousePosition)
    [Windows.Forms.Application]::DoEvents()
    if ($state.Count -ne 1 -or $combo.DroppedDown) {
        throw "鼠标触发未接管原生下拉：count=$($state.Count)，droppedDown=$($combo.DroppedDown)。"
    }

    $null = [InspectorPickerNativeTest]::SendMessage(
        $combo.Handle,
        0x0100,
        [IntPtr][int][Windows.Forms.Keys]::F4,
        [IntPtr]::Zero)
    [Windows.Forms.Application]::DoEvents()
    if ($state.Count -ne 2 -or $combo.DroppedDown) {
        throw "键盘触发未接管原生下拉：count=$($state.Count)，droppedDown=$($combo.DroppedDown)。"
    }

    Write-Host '检查器整表选择器触发回归通过。'
}
finally {
    $pickerEvent.GetRemoveMethod($true).Invoke($combo, @($handler))
    $combo.Dispose()
    $form.Dispose()
}
