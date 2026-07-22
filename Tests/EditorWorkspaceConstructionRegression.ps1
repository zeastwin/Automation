param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $repoRoot "bin\$Configuration"
$automationPath = Join-Path $outputRoot "Automation.exe"
$automationConfigPath = "$automationPath.config"
$sourcePath = Join-Path $PSScriptRoot "FrmMainConstructorSmoke.cs"
$smokePath = Join-Path $outputRoot "FrmMainConstructorSmoke.exe"
$smokeConfigPath = "$smokePath.config"
$compilerPath = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

foreach ($requiredPath in @($automationPath, $automationConfigPath, $sourcePath, $compilerPath))
{
    if (-not (Test-Path -LiteralPath $requiredPath))
    {
        throw "工作区构造回归缺少文件：$requiredPath"
    }
}

& $compilerPath /nologo /target:exe /out:$smokePath `
    /reference:$automationPath /reference:System.dll /reference:System.Windows.Forms.dll `
    $sourcePath
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}
Copy-Item -LiteralPath $automationConfigPath -Destination $smokeConfigPath -Force
& $smokePath
exit $LASTEXITCODE
