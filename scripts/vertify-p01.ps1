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

function Get-RequiredXmlText {
    param(
        [Parameter(Mandatory = $true)][xml]$Xml,
        [Parameter(Mandatory = $true)][string]$XPath,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $node = $Xml.SelectSingleNode($XPath)
    if ($null -eq $node) {
        throw "Static check failed: required XML node '$Name' was not found at XPath '$XPath'."
    }

    return $node.InnerText
}

function Assert-SingleProjectReference {
    param(
        [Parameter(Mandatory = $true)][xml]$Xml,
        [Parameter(Mandatory = $true)][string]$ProjectFile,
        [Parameter(Mandatory = $true)][string]$ExpectedProjectFile,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $references = @($Xml.SelectNodes('/Project/ItemGroup/ProjectReference'))
    if ($references.Count -ne 1) {
        throw "Static check failed: $Name must contain exactly one ProjectReference. Found $($references.Count)."
    }

    $include = $references[0].GetAttribute('Include')
    if ([string]::IsNullOrWhiteSpace($include)) {
        throw "Static check failed: $Name ProjectReference is missing the Include attribute."
    }

    $projectDirectory = Split-Path -Parent $ProjectFile
    $actualProjectFile = [System.IO.Path]::GetFullPath((Join-Path $projectDirectory $include))
    $expectedProjectFile = [System.IO.Path]::GetFullPath($ExpectedProjectFile)

    if (-not [System.StringComparer]::OrdinalIgnoreCase.Equals($actualProjectFile, $expectedProjectFile)) {
        throw "Static check failed: $Name ProjectReference. Expected '$expectedProjectFile', actual '$actualProjectFile'."
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
    Assert-Equal 'Core TargetFramework' (Get-RequiredXmlText $coreXml '/Project/PropertyGroup/TargetFramework' 'Core TargetFramework') 'netstandard2.0'
    Assert-Equal 'Core Nullable' (Get-RequiredXmlText $coreXml '/Project/PropertyGroup/Nullable' 'Core Nullable') 'enable'
    Assert-Equal 'Core ImplicitUsings' (Get-RequiredXmlText $coreXml '/Project/PropertyGroup/ImplicitUsings' 'Core ImplicitUsings') 'disable'
    Assert-Equal 'Core GenerateDocumentationFile' (Get-RequiredXmlText $coreXml '/Project/PropertyGroup/GenerateDocumentationFile' 'Core GenerateDocumentationFile') 'true'
    Assert-Equal 'Core Deterministic' (Get-RequiredXmlText $coreXml '/Project/PropertyGroup/Deterministic' 'Core Deterministic') 'true'

    $corePackageReferences = @($coreXml.SelectNodes('/Project/ItemGroup/PackageReference'))
    if ($corePackageReferences.Count -ne 0) {
        $packages = ($corePackageReferences | ForEach-Object { $_.GetAttribute('Include') }) -join ', '
        throw "Core project must not have runtime PackageReference entries in P01. Found: $packages"
    }

    [xml]$testXml = Get-Content -LiteralPath $testProject -Raw
    Assert-Equal 'Tests TargetFrameworks' (Get-RequiredXmlText $testXml '/Project/PropertyGroup/TargetFrameworks' 'Tests TargetFrameworks') 'net472;net8.0'
    Assert-Equal 'Tests OutputType' (Get-RequiredXmlText $testXml '/Project/PropertyGroup/OutputType' 'Tests OutputType') 'Exe'
    Assert-Equal 'Tests IsPackable' (Get-RequiredXmlText $testXml '/Project/PropertyGroup/IsPackable' 'Tests IsPackable') 'false'
    Assert-SingleProjectReference $testXml $testProject $coreProject 'Tests project'

    [xml]$sampleXml = Get-Content -LiteralPath $sampleProject -Raw
    Assert-Equal 'Sample TargetFramework' (Get-RequiredXmlText $sampleXml '/Project/PropertyGroup/TargetFramework' 'Sample TargetFramework') 'net472'
    Assert-SingleProjectReference $sampleXml $sampleProject $coreProject 'Sample project'

    [xml]$propsXml = Get-Content -LiteralPath $directoryBuildProps -Raw
    Assert-Equal 'Language version' (Get-RequiredXmlText $propsXml '/Project/PropertyGroup/LangVersion' 'Language version') '8.0'
    Assert-Equal 'Deterministic build default' (Get-RequiredXmlText $propsXml '/Project/PropertyGroup/Deterministic' 'Deterministic build default') 'true'

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
