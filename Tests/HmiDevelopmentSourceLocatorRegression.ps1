param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\bin\Debug\Automation.exe')
)

$ErrorActionPreference = 'Stop'

function Assert-Equal {
    param($Actual, $Expected, [string]$Message)
    if ($Actual -ne $Expected) { throw "$Message 实际=$Actual，期望=$Expected" }
}

$resolvedAssembly = [IO.Path]::GetFullPath($AssemblyPath)
if (-not [IO.File]::Exists($resolvedAssembly)) { throw "找不到待测程序集：$resolvedAssembly" }
$assembly = [Reflection.Assembly]::LoadFrom($resolvedAssembly)
$locatorType = $assembly.GetType('Automation.HmiDevelopmentSourceLocator', $true)
$resolveMethod = $locatorType.GetMethods([Reflection.BindingFlags]'Static,NonPublic') |
    Where-Object { $_.Name -eq 'TryResolve' -and $_.GetParameters().Count -eq 4 } |
    Select-Object -First 1

function Resolve-HmiSource {
    param([string]$StartDirectory, [string]$HostExecutable)
    $arguments = @($StartDirectory, $HostExecutable, $null, $null)
    $ok = $resolveMethod.Invoke($null, $arguments)
    if (-not $ok) { throw "HMI 源码解析失败：$($arguments[3])" }
    return $arguments[2]
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('automation-hmi-locator-' + [Guid]::NewGuid().ToString('N'))
try {
    $platformRoot = Join-Path $testRoot 'Automation'
    $deviceRoot = Join-Path $testRoot 'DeviceProject'
    $startDirectory = Join-Path $platformRoot 'bin\Debug'
    [IO.Directory]::CreateDirectory((Join-Path $platformRoot 'Hmi')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $deviceRoot 'Hmi')) | Out-Null
    [IO.Directory]::CreateDirectory($startDirectory) | Out-Null
    [IO.File]::WriteAllText((Join-Path $platformRoot 'Automation.csproj'), '<Project />')
    [IO.File]::WriteAllText((Join-Path $deviceRoot 'MachineApp.csproj'), '<Project />')

    $platform = Resolve-HmiSource $startDirectory (Join-Path $startDirectory 'Automation.exe')
    Assert-Equal $platform.ProjectKind 'platform' 'Automation 宿主必须解析为平台工程。'
    Assert-Equal $platform.SourceDirectory ([IO.Path]::GetFullPath((Join-Path $platformRoot 'Hmi'))) '平台 HMI 路径错误。'
    Assert-Equal $platform.ProjectRoot ([IO.Path]::GetFullPath($platformRoot)) '平台 Skill 工作根目录错误。'

    $device = Resolve-HmiSource $startDirectory (Join-Path $startDirectory 'MachineApp.exe')
    Assert-Equal $device.ProjectKind 'device' 'MachineApp 宿主必须解析为设备工程。'
    Assert-Equal $device.SourceDirectory ([IO.Path]::GetFullPath((Join-Path $deviceRoot 'Hmi'))) '设备 HMI 必须解析到同级 DeviceProject/Hmi。'
    Assert-Equal $device.ProjectRoot ([IO.Path]::GetFullPath($deviceRoot)) '设备 Skill 工作根目录错误。'

    $publishedRoot = Join-Path $testRoot 'PublishedMachine'
    [IO.Directory]::CreateDirectory((Join-Path $publishedRoot 'Hmi')) | Out-Null
    $published = Resolve-HmiSource $publishedRoot (Join-Path $publishedRoot 'MachineApp.exe')
    Assert-Equal $published.ProjectKind 'device' 'MachineApp 发布包必须保持设备工程身份。'
    Assert-Equal $published.SourceDirectory ([IO.Path]::GetFullPath((Join-Path $publishedRoot 'Hmi'))) '发布包 HMI 路径错误。'
    Assert-Equal $published.IsPublishedLayout $true '发布包应标记为发布布局。'
    Assert-Equal $published.SkillRootDirectory ([IO.Path]::GetFullPath((Join-Path $publishedRoot '.agents\skills'))) '发布包 Skill 根目录错误。'
}
finally {
    if ([IO.Directory]::Exists($testRoot)) {
        [IO.Directory]::Delete($testRoot, $true)
    }
}

Write-Output 'HMI 工程身份回归通过：平台、DeviceProject 和 MachineApp 发布包边界互不混用。'
