[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$debugExe = Join-Path $repoRoot 'bin\Debug\Automation.exe'
$stagingRoot = Join-Path $repoRoot 'artifacts\HmiValidation'
$legacyIntermediateRoot = Join-Path $repoRoot 'Build\HmiValidation\obj'

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

function Assert-DebugUnchanged {
    param([string]$ExpectedFingerprint)

    $actual = Get-FileFingerprint -Path $debugExe
    if ($actual -ne $ExpectedFingerprint) {
        throw 'HMI validation changed bin\Debug\Automation.exe.'
    }
}

function Assert-HmiProjectItems {
    $projectPath = Join-Path $repoRoot 'Automation.csproj'
    $project = $null
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            $projectText = [IO.File]::ReadAllText($projectPath)
            $document = New-Object System.Xml.XmlDocument
            $document.LoadXml($projectText)
            $project = $document
            break
        }
        catch [System.Xml.XmlException] {
            if ($attempt -eq 5) {
                throw
            }
            Start-Sleep -Milliseconds 200
        }
        catch [System.IO.IOException] {
            if ($attempt -eq 5) {
                throw
            }
            Start-Sleep -Milliseconds 200
        }
    }

    $compilePatterns = @($project.SelectNodes("//*[local-name()='Compile']") | ForEach-Object {
        (($_.GetAttribute('Include') -replace '/', '\') -replace '\\+', '\').TrimStart('\')
    })
    $resourcePatterns = @($project.SelectNodes("//*[local-name()='EmbeddedResource']") | ForEach-Object {
        (($_.GetAttribute('Include') -replace '/', '\') -replace '\\+', '\').TrimStart('\')
    })

    $missingItems = New-Object System.Collections.Generic.List[string]
    $hmiRoot = Join-Path $repoRoot 'Hmi'
    foreach ($file in Get-ChildItem -LiteralPath $hmiRoot -Recurse -File) {
        $extension = $file.Extension.ToLowerInvariant()
        if ($extension -eq '.cs') {
            $itemType = 'Compile'
        }
        elseif ($extension -eq '.resx') {
            $itemType = 'EmbeddedResource'
        }
        else {
            continue
        }
        $patterns = if ($itemType -eq 'Compile') { $compilePatterns } else { $resourcePatterns }
        $relativePath = $file.FullName.Substring($repoRoot.Length).TrimStart('\')
        $included = $false
        foreach ($pattern in $patterns) {
            if ($relativePath -like $pattern) {
                $included = $true
                break
            }
        }
        if (-not $included) {
            $missingItems.Add("$itemType $relativePath")
        }
    }

    if ($missingItems.Count -gt 0) {
        $detail = $missingItems -join "`r`n  - "
        throw "Automation.csproj does not include these HMI project items:`r`n  - $detail"
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

function Remove-LegacyIntermediateDirectory {
    if (-not (Test-Path -LiteralPath $legacyIntermediateRoot)) {
        return
    }
    $fullPath = [IO.Path]::GetFullPath($legacyIntermediateRoot)
    $expectedPath = [IO.Path]::GetFullPath((Join-Path $repoRoot 'Build\HmiValidation\obj'))
    if (-not $fullPath.Equals($expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove an unexpected intermediate path: $fullPath"
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
$debugFingerprint = $null
$projectItemsVerified = $false
$compiled = $false
$validationError = $null
$cleanupErrors = New-Object System.Collections.Generic.List[string]
Push-Location $repoRoot
try {
    $debugFingerprint = Get-FileFingerprint -Path $debugExe
    Write-Host 'Stage 1/2: verify HMI files are included by Automation.csproj'
    Assert-HmiProjectItems
    $projectItemsVerified = $true
    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
    Remove-StagingDirectory -Path (Join-Path $stagingRoot 'current')
    Remove-StagingDirectory -Path (Join-Path $stagingRoot 'last-known-good')
    Remove-LegacyIntermediateDirectory
    $candidateName = 'candidate-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $PID
    $candidate = Join-Path $stagingRoot $candidateName
    $appDirectory = Join-Path $candidate 'App'
    $intermediateDirectory = Join-Path $candidate 'Obj'
    New-Item -ItemType Directory -Path $appDirectory -Force | Out-Null
    $appOut = $appDirectory.Replace('\', '/') + '/'
    $intermediateOut = $intermediateDirectory.Replace('\', '/') + '/'

    Write-Host "Stage 2/2: compile HMI and CustomFunc sources without running code -> $appDirectory"
    Invoke-DotNet -Arguments @(
        'build', 'Build\HmiValidation\HmiValidation.csproj', '-c', 'Debug',
        "/p:OutDir=$appOut",
        "/p:BaseIntermediateOutputPath=$intermediateOut",
        "/p:MSBuildProjectExtensionsPath=$intermediateOut"
    )
    $compiled = $true
    Assert-DebugUnchanged -ExpectedFingerprint $debugFingerprint
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
        Remove-LegacyIntermediateDirectory
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

$debugUnchanged = $debugFingerprint -ne $null -and (Get-FileFingerprint -Path $debugExe) -eq $debugFingerprint
$temporaryOutputsRemoved = ([string]::IsNullOrWhiteSpace($candidate) -or -not (Test-Path -LiteralPath $candidate)) `
    -and -not (Test-Path -LiteralPath $legacyIntermediateRoot)

if ($validationError -ne $null -or $cleanupErrors.Count -gt 0 -or -not $debugUnchanged -or -not $temporaryOutputsRemoved) {
    $messages = New-Object System.Collections.Generic.List[string]
    if ($validationError -ne $null) {
        $messages.Add($validationError.Message)
    }
    foreach ($cleanupError in $cleanupErrors) {
        $messages.Add("cleanup: $cleanupError")
    }
    if (-not $debugUnchanged) {
        $messages.Add('bin\Debug\Automation.exe changed during validation.')
    }
    if (-not $temporaryOutputsRemoved) {
        $messages.Add('Temporary validation outputs were not fully removed.')
    }
    [ordered]@{
        ok = $false
        type = 'hmi.compile_validation'
        errorCode = 'HMI_COMPILE_VALIDATION_FAILED'
        message = $messages -join ' '
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
    compileOnly = $true
    projectItemsVerified = $true
    compiled = $true
    candidateCodeExecuted = $false
    debugUnchanged = $true
    temporaryOutputsRemoved = $true
} | ConvertTo-Json -Compress
