# Local verification for P01 — Scaffold solution and compatibility floor.
# Run from any directory:
#   powershell -ExecutionPolicy Bypass -File .\scripts\vertify-p01.ps1
# or:
#   pwsh -File .\scripts\vertify-p01.ps1

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([Parameter(Mandatory = $true)][string]$Message)

    Write-Host ''
    Write-Host "==> $Message"
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()]$Actual,
        [AllowNull()]$Expected
    )

    if ($Actual -ne $Expected) {
        throw "Static check failed: $Name. Expected '$Expected', actual '$Actual'."
    }
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    Write-Step $Description
    Write-Host "dotnet $($Arguments -join ' ')"
    & dotnet @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $repoRoot 'NetUnitOfWorkManager.sln'
$coreProject = Join-Path $repoRoot 'src\NetUnitOfWorkManager\NetUnitOfWorkManager.csproj'
$testProject = Join-Path $repoRoot 'tests\NetUnitOfWorkManager.Tests\NetUnitOfWorkManager.Tests.csproj'
$sampleProject = Join-Path $repoRoot 'samples\NetUnitOfWorkManager.Sample.Net472\NetUnitOfWorkManager.Sample.Net472.csproj'
$directoryBuildProps = Join-Path $repoRoot 'Directory.Build.props'
$isWindowsHost = $env:OS -eq 'Windows_NT'

$requiredFiles = @(
    $solution,
    $coreProject,
    $testProject,
    $sampleProject,
    $directoryBuildProps
)

try {
    Write-Step 'Check prerequisites and required files'

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "The .NET SDK ('dotnet') was not found in PATH. Install a current .NET SDK before running P01 verification."
    }

    foreach ($file in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "Required P01 file is missing: $file"
        }
    }

    Invoke-DotNet 'Show .NET SDK version' @('--version')

    Write-Step 'Validate P01 project compatibility floor'

    [xml]$coreXml = Get-Content -LiteralPath $coreProject -Raw
    $coreProperties = $coreXml.Project.PropertyGroup | Select-Object -First 1
    Assert-Equal 'Core TargetFramework' $coreProperties.TargetFramework 'netstandard2.0'
    Assert-Equal 'Core Nullable' $coreProperties.Nullable 'enable'
    Assert-Equal 'Core ImplicitUsings' $coreProperties.ImplicitUsings 'disable'
    Assert-Equal 'Core GenerateDocumentationFile' $coreProperties.GenerateDocumentationFile 'true'
    Assert-Equal 'Core Deterministic' $coreProperties.Deterministic 'true'

    $corePackageReferences = @($coreXml.Project.ItemGroup.PackageReference | Where-Object { $null -ne $_ })
    if ($corePackageReferences.Count -ne 0) {
        $packages = ($corePackageReferences | ForEach-Object { $_.Include }) -join ', '
        throw "Core project must not have runtime PackageReference entries in P01. Found: $packages"
    }

    [xml]$testXml = Get-Content -LiteralPath $testProject -Raw
    $testProperties = $testXml.Project.PropertyGroup | Select-Object -First 1
    Assert-Equal 'Tests TargetFrameworks' $testProperties.TargetFrameworks 'net472;net8.0'
    Assert-Equal 'Tests IsPackable' $testProperties.IsPackable 'false'

    [xml]$sampleXml = Get-Content -LiteralPath $sampleProject -Raw
    $sampleProperties = $sampleXml.Project.PropertyGroup | Select-Object -First 1
    Assert-Equal 'Sample TargetFramework' $sampleProperties.TargetFramework 'net472'

    [xml]$propsXml = Get-Content -LiteralPath $directoryBuildProps -Raw
    $props = $propsXml.Project.PropertyGroup | Select-Object -First 1
    Assert-Equal 'Language version' $props.LangVersion '8.0'
    Assert-Equal 'Deterministic build default' $props.Deterministic 'true'

    $solutionText = Get-Content -LiteralPath $solution -Raw
    foreach ($projectPath in @(
        'src\NetUnitOfWorkManager\NetUnitOfWorkManager.csproj',
        'tests\NetUnitOfWorkManager.Tests\NetUnitOfWorkManager.Tests.csproj',
        'samples\NetUnitOfWorkManager.Sample.Net472\NetUnitOfWorkManager.Sample.Net472.csproj'
    )) {
        if (-not $solutionText.Contains($projectPath)) {
            throw "Solution does not contain required project: $projectPath"
        }
    }

    Write-Host 'Static compatibility checks passed.'

    Push-Location $repoRoot
    try {
        Invoke-DotNet 'Restore solution' @('restore', $solution)
        Invoke-DotNet 'Build core Release with CI warning policy' @('build', $coreProject, '-c', 'Release', '--no-restore', '-p:CI=true')
        Invoke-DotNet 'Run net8.0 tests' @('test', $testProject, '-c', 'Release', '-f', 'net8.0', '--no-restore', '-p:CI=true')

        if ($isWindowsHost) {
            Invoke-DotNet 'Build .NET Framework 4.7.2 sample' @('build', $sampleProject, '-c', 'Release', '--no-restore', '-p:CI=true')
            Invoke-DotNet 'Run net472 tests' @('test', $testProject, '-c', 'Release', '-f', 'net472', '--no-restore', '-p:CI=true')
        }
        else {
            Write-Warning 'net472 sample/test verification is skipped because this host is not Windows. Full P01 verification requires a Windows machine.'
        }
    }
    finally {
        Pop-Location
    }

    Write-Host ''
    if ($isWindowsHost) {
        Write-Host 'P01 local verification PASSED.'
    }
    else {
        Write-Host 'P01 cross-platform checks PASSED. Run this script on Windows to verify the net472 compatibility floor.'
    }

    exit 0
}
catch {
    Write-Host ''
    Write-Error "P01 verification FAILED: $($_.Exception.Message)"
    exit 1
}
