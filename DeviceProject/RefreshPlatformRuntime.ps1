param(
    [string]$SourceRuntime = "",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($SourceRuntime)) {
    $SourceRuntime = Join-Path (Split-Path -Parent $projectRoot) ("bin\" + $Configuration)
}

$source = (Resolve-Path -LiteralPath $SourceRuntime).Path
$destination = Join-Path $projectRoot "Platform"
if (-not (Test-Path -LiteralPath (Join-Path $source "Automation.exe"))) {
    throw "平台运行包缺少 Automation.exe：$source"
}
if (-not (Test-Path -LiteralPath (Join-Path $source "PlatformRuntimeManifest.json"))) {
    throw "平台运行包缺少 PlatformRuntimeManifest.json：$source"
}
if ([string]::Equals($source, $destination, [StringComparison]::OrdinalIgnoreCase)) {
    throw "源运行包不能与目标目录相同。"
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null
Get-ChildItem -LiteralPath $destination -Force | Where-Object {
    $_.Name -ne ".gitignore" -and $_.Name -ne "README.md"
} | Remove-Item -Recurse -Force

$sourcePrefix = $source.TrimEnd('\') + '\'
Get-ChildItem -LiteralPath $source -File -Recurse | Where-Object {
    $_.Extension -ne ".pdb" -and
    $_.FullName -notlike ($sourcePrefix + "Config\*") -and
    $_.FullName -notmatch "[\\/]Logs[\\/]"
} | ForEach-Object {
    $relativePath = $_.FullName.Substring($sourcePrefix.Length)
    $targetPath = Join-Path $destination $relativePath
    $targetDirectory = Split-Path -Parent $targetPath
    New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $targetPath -Force
}

Write-Host "平台运行包已刷新：$destination"
