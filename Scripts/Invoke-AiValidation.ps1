[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = [IO.Path]::GetFullPath($ProjectPath)
$projectRoot = Split-Path -Parent $projectPath
$projectName = [IO.Path]::GetFileName($projectPath)
$isMachineApp = $projectName.Equals('MachineApp.csproj', [StringComparison]::OrdinalIgnoreCase)
$isAutomation = $projectName.Equals('Automation.csproj', [StringComparison]::OrdinalIgnoreCase)
$hmiRoot = Join-Path $projectRoot 'Hmi'
$stagingRoot = Join-Path $repoRoot 'artifacts\HmiValidation'
$protectedDebugFiles = @(
    (Join-Path $repoRoot 'bin\Debug\Automation.exe')
)
if ($isMachineApp) {
    $protectedDebugFiles += Join-Path $projectRoot 'bin\x64\Debug\MachineApp.exe'
}

function Invoke-DotNet {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE"
    }
}

function Get-FileFingerprint {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return '<missing>'
    }
    $item = Get-Item -LiteralPath $Path
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    return "$($item.Length)|$($item.LastWriteTimeUtc.Ticks)|$hash"
}

function Get-ProtectedDebugFingerprints {
    $fingerprints = @{}
    foreach ($path in $protectedDebugFiles) {
        $fingerprints[$path] = Get-FileFingerprint -Path $path
    }
    return $fingerprints
}

function Assert-ProtectedDebugFilesUnchanged {
    param([hashtable]$ExpectedFingerprints)

    foreach ($path in $protectedDebugFiles) {
        $actual = Get-FileFingerprint -Path $path
        if ($actual -ne $ExpectedFingerprints[$path]) {
            throw "HMI validation changed the current Debug program: $path"
        }
    }
}

function Assert-HmiProject {
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "HMI project does not exist: $projectPath"
    }
    if (-not (Test-Path -LiteralPath $hmiRoot -PathType Container)) {
        throw "HMI source directory does not exist: $hmiRoot"
    }

    if (-not $isMachineApp -and -not $isAutomation) {
        throw "Only Automation.csproj or MachineApp.csproj can be validated: $projectPath"
    }
}

function Assert-HmiProjectItems {
    $queryOutput = & dotnet msbuild $projectPath `
        '-getItem:Compile' `
        '-getItem:EmbeddedResource' `
        '-p:Configuration=Debug' `
        '-p:Platform=x64'
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to evaluate HMI project items, dotnet msbuild exited with code $LASTEXITCODE"
    }

    $query = ($queryOutput -join "`n") | ConvertFrom-Json
    $includedItems = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in @($query.Items.Compile) + @($query.Items.EmbeddedResource)) {
        if (-not [string]::IsNullOrWhiteSpace($item.FullPath)) {
            [void]$includedItems.Add([IO.Path]::GetFullPath($item.FullPath))
        }
    }

    $missingItems = New-Object System.Collections.Generic.List[string]
    foreach ($file in Get-ChildItem -LiteralPath $hmiRoot -Recurse -File) {
        if ($file.Extension -ne '.cs' -and $file.Extension -ne '.resx') {
            continue
        }
        if (-not $includedItems.Contains($file.FullName)) {
            $missingItems.Add($file.FullName)
        }
    }
    if ($missingItems.Count -gt 0) {
        throw "$projectName does not include these HMI source files:`r`n  - $($missingItems -join "`r`n  - ")"
    }
}

function Remove-StagingDirectory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($stagingRoot).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the staging root: $fullPath"
    }
    Remove-Item -LiteralPath $fullPath -Recurse -Force
}

function Remove-EmptyStagingRoot {
    if (-not (Test-Path -LiteralPath $stagingRoot)) {
        return
    }
    $remainingItems = @(Get-ChildItem -Force -LiteralPath $stagingRoot)
    if ($remainingItems.Count -eq 0) {
        Remove-Item -LiteralPath $stagingRoot -Force
    }
}

$candidate = $null
$debugFingerprints = $null
$projectItemsVerified = $false
$compiled = $false
$validationError = $null
$cleanupErrors = New-Object System.Collections.Generic.List[string]
Push-Location $repoRoot
try {
    $debugFingerprints = Get-ProtectedDebugFingerprints
    Assert-HmiProject
    Write-Host "Stage 1/2: verify HMI files belong to $projectPath"
    Assert-HmiProjectItems
    $projectItemsVerified = $true

    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
    $candidateName = 'candidate-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $PID
    $candidate = Join-Path $stagingRoot $candidateName
    $appDirectory = Join-Path $candidate 'App'
    New-Item -ItemType Directory -Path $appDirectory -Force | Out-Null
    $appOut = $appDirectory.Replace('\', '/') + '/'

    $currentBuildOutput = if ($isAutomation) {
        Join-Path $repoRoot 'bin\Debug'
    }
    else {
        Join-Path $projectRoot 'bin\x64\Debug'
    }
    if (-not (Test-Path -LiteralPath $currentBuildOutput -PathType Container)) {
        throw "Current Debug output does not exist: $currentBuildOutput"
    }
    Get-ChildItem -LiteralPath $currentBuildOutput -File |
        Copy-Item -Destination $appDirectory -Force

    Write-Host "Stage 2/2: compile the current HMI host without running code -> $appDirectory"
    $buildArguments = @(
        'build', $projectPath, '-c', 'Debug', '--no-restore', '-p:Platform=x64',
        '/p:BuildProjectReferences=false',
        "/p:OutDir=$appOut"
    )
    Invoke-DotNet -Arguments $buildArguments
    $compiled = $true
    Assert-ProtectedDebugFilesUnchanged -ExpectedFingerprints $debugFingerprints
}
catch {
    $validationError = $_.Exception
}
finally {
    Pop-Location
    try {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            Remove-StagingDirectory -Path $candidate
        }
    }
    catch {
        $cleanupErrors.Add($_.Exception.Message)
    }
    try {
        Remove-EmptyStagingRoot
    }
    catch {
        $cleanupErrors.Add($_.Exception.Message)
    }
}

$debugUnchanged = $debugFingerprints -ne $null
if ($debugUnchanged) {
    foreach ($path in $protectedDebugFiles) {
        if ((Get-FileFingerprint -Path $path) -ne $debugFingerprints[$path]) {
            $debugUnchanged = $false
            break
        }
    }
}
$temporaryOutputsRemoved = [string]::IsNullOrWhiteSpace($candidate) -or -not (Test-Path -LiteralPath $candidate)

if ($validationError -ne $null -or $cleanupErrors.Count -gt 0 -or -not $debugUnchanged -or -not $temporaryOutputsRemoved) {
    $messages = New-Object System.Collections.Generic.List[string]
    if ($validationError -ne $null) {
        $messages.Add($validationError.Message)
    }
    foreach ($cleanupError in $cleanupErrors) {
        $messages.Add("cleanup: $cleanupError")
    }
    if (-not $debugUnchanged) {
        $messages.Add('A protected Debug executable changed during validation.')
    }
    if (-not $temporaryOutputsRemoved) {
        $messages.Add('Temporary validation outputs were not fully removed.')
    }
    [ordered]@{
        ok = $false
        type = 'hmi.compile_validation'
        errorCode = 'HMI_COMPILE_VALIDATION_FAILED'
        message = $messages -join ' '
        project = $projectPath
        sourceDirectory = $hmiRoot
        compileOnly = $true
        projectItemsVerified = $projectItemsVerified
        compiled = $compiled
        candidateCodeExecuted = $false
        debugUnchanged = $debugUnchanged
        temporaryOutputsRemoved = $temporaryOutputsRemoved
    } | ConvertTo-Json -Compress
    exit 1
}

[ordered]@{
    ok = $true
    type = 'hmi.compile_validation'
    project = $projectPath
    sourceDirectory = $hmiRoot
    compileOnly = $true
    projectItemsVerified = $true
    compiled = $true
    candidateCodeExecuted = $false
    debugUnchanged = $true
    temporaryOutputsRemoved = $true
} | ConvertTo-Json -Compress
