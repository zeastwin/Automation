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

$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$comboType = $assembly.GetType('Automation.InspectorComboBox', $true)
$flags = [Reflection.BindingFlags]::Instance -bor
    [Reflection.BindingFlags]::Public -bor
    [Reflection.BindingFlags]::NonPublic
$dropDownField = $comboType.GetField('activeStandardValueDropDown', $flags)
if ($null -eq $dropDownField) {
    throw '检查器普通下拉弹层契约缺失。'
}

$form = New-Object Windows.Forms.Form
$combo = [Activator]::CreateInstance($comboType, $true)
$state = [pscustomobject]@{ Committed = 0; Closed = 0 }
$committed = [EventHandler]{ param($sender, $eventArgs) $state.Committed++ }
$closed = [EventHandler]{ param($sender, $eventArgs) $state.Closed++ }

try {
    $form.ShowInTaskbar = $false
    $form.Size = New-Object Drawing.Size(320, 100)
    $combo.Location = New-Object Drawing.Point(20, 20)
    $combo.Size = New-Object Drawing.Size(260, 24)
    $combo.DropDownStyle = [Windows.Forms.ComboBoxStyle]::DropDownList
    $null = $combo.Items.Add('自定义提示信息')
    $null = $combo.Items.Add('变量类型')
    $null = $combo.Items.Add('报警信息库')
    $combo.SelectedIndex = 0
    $combo.add_SelectionChangeCommitted($committed)
    $combo.add_DropDownClosed($closed)
    $form.Controls.Add($combo)
    $form.Show()
    [Windows.Forms.Application]::DoEvents()

    $combo.DroppedDown = $true
    [Windows.Forms.Application]::DoEvents()
    $dropDown = $dropDownField.GetValue($combo)
    if ($null -eq $dropDown -or -not $dropDown.Visible -or $combo.DroppedDown) {
        throw '普通枚举未由即时弹层接管。'
    }

    $list = $dropDown.Items[0].Control
    $list.SelectedIndex = 1
    $keyMethod = $list.GetType().GetMethod('OnKeyDown', $flags)
    $keyMethod.Invoke(
        $list,
        @([Windows.Forms.KeyEventArgs]::new([Windows.Forms.Keys]::Enter)))
    [Windows.Forms.Application]::DoEvents()
    if ($combo.SelectedIndex -ne 1 -or $state.Committed -ne 1 -or $state.Closed -ne 1) {
        throw "普通下拉选择事件异常：index=$($combo.SelectedIndex)，committed=$($state.Committed)，closed=$($state.Closed)。"
    }

    Write-Host '检查器普通下拉即时展开回归通过。'
}
finally {
    $combo.remove_SelectionChangeCommitted($committed)
    $combo.remove_DropDownClosed($closed)
    $form.Close()
    $combo.Dispose()
    $form.Dispose()
}
