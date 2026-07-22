param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$buildOutputRoot = Join-Path $repoRoot "bin\$Configuration"
$isolatedRoot = Join-Path $env:TEMP (
    "automation-headless-regression-" + [Guid]::NewGuid().ToString("N"))
$automationPath = Join-Path $isolatedRoot "Automation.exe"
$automationConfigPath = "$automationPath.config"
$deviceSdkPath = Join-Path $isolatedRoot "Automation.DeviceSdk.dll"
$runtimeContractsPath = Join-Path $isolatedRoot "Automation.Runtime.Contracts.dll"
$netstandardFacadePath = Join-Path ${env:ProgramFiles(x86)} `
    "Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll"
$sourcePath = Join-Path $PSScriptRoot "Harnesses\HeadlessPlatformHostSmoke.cs"
$smokePath = Join-Path $isolatedRoot "HeadlessPlatformHostSmoke.exe"
$smokeConfigPath = "$smokePath.config"
$compilerPath = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

foreach ($requiredPath in @(
    (Join-Path $buildOutputRoot "Automation.exe"),
    (Join-Path $buildOutputRoot "Automation.exe.config"),
    (Join-Path $buildOutputRoot "Automation.DeviceSdk.dll"),
    (Join-Path $buildOutputRoot "Automation.Runtime.Contracts.dll"),
    $netstandardFacadePath,
    $sourcePath,
    $compilerPath))
{
    if (-not (Test-Path -LiteralPath $requiredPath))
    {
        throw "无编辑器宿主回归缺少文件：$requiredPath"
    }
}

New-Item -ItemType Directory -Path $isolatedRoot | Out-Null
try
{
    Get-ChildItem -LiteralPath $buildOutputRoot -File | Copy-Item -Destination $isolatedRoot -Force

    & $compilerPath /nologo /target:exe /out:$smokePath `
        /reference:$automationPath /reference:$deviceSdkPath /reference:$runtimeContractsPath `
        /reference:$netstandardFacadePath `
        /reference:System.dll /reference:System.Windows.Forms.dll `
        $sourcePath
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
    Copy-Item -LiteralPath $automationConfigPath -Destination $smokeConfigPath -Force
    & $smokePath
    exit $LASTEXITCODE
}
finally
{
    $resolvedIsolatedRoot = [IO.Path]::GetFullPath($isolatedRoot)
    $resolvedTempRoot = [IO.Path]::GetFullPath($env:TEMP)
    if ((Test-Path -LiteralPath $resolvedIsolatedRoot) -and
        $resolvedIsolatedRoot.StartsWith(
            $resolvedTempRoot,
            [StringComparison]::OrdinalIgnoreCase))
    {
        Remove-Item -LiteralPath $resolvedIsolatedRoot -Recurse -Force
    }
}
