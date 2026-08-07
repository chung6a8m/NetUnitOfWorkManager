[CmdletBinding()]
param(
    [string] $Version = '1.0.0-preview.1',
    [string] $ArtifactsDirectory = 'artifacts/prerelease'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runningOnWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
if (-not $runningOnWindows) {
    throw 'P10 prerelease verification must run on Windows because net472 and SQL Server verification are release gates.'
}

if ([string]::IsNullOrWhiteSpace($Version) -or $Version -notmatch '-') {
    throw "P10 requires a prerelease semantic version such as 1.0.0-preview.1. Received: '$Version'."
}

if ([string]::IsNullOrWhiteSpace($env:NETUOW_SQLSERVER_CONNECTION_STRING)) {
    throw 'NETUOW_SQLSERVER_CONNECTION_STRING is required for the P10 SQL Server and package-consumer verification gates.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactRoot = if ([System.IO.Path]::IsPathRooted($ArtifactsDirectory)) {
    $ArtifactsDirectory
}
else {
    Join-Path $repoRoot $ArtifactsDirectory
}
$packageDirectory = Join-Path $artifactRoot 'packages'
$testProject = 'tests/NetUnitOfWorkManager.Tests/NetUnitOfWorkManager.Tests.csproj'
$coreProject = 'src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj'
$summaryPath = Join-Path $artifactRoot 'p10-verification-summary.txt'

if (Test-Path $artifactRoot) {
    Remove-Item $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

Push-Location $repoRoot
try {
    Write-Host 'P10 gate 1/6: modern .NET consumer tests...'
    dotnet restore $testProject
    if ($LASTEXITCODE -ne 0) {
        throw 'Modern .NET test restore failed.'
    }

    dotnet test $testProject -c Release -f net8.0 --no-restore -p:CI=true
    if ($LASTEXITCODE -ne 0) {
        throw 'Modern .NET consumer tests failed.'
    }

    Write-Host 'P10 gate 2/6: .NET Framework 4.7.2 runtime and contract verification...'
    & (Join-Path $PSScriptRoot 'verify-net472.ps1')

    Write-Host 'P10 gate 3/6: SQL Server, Dapper, and RepoDb integration verification...'
    & (Join-Path $PSScriptRoot 'verify-sqlserver.ps1')

    Write-Host "P10 gate 4/6: Release pack for $Version..."
    dotnet pack $coreProject -c Release -p:Version=$Version -p:CI=true -o $packageDirectory
    if ($LASTEXITCODE -ne 0) {
        throw 'Prerelease package creation failed.'
    }

    Write-Host 'P10 gate 5/6: package assets, dependency budget, symbols, and real net472 package consumption...'
    & (Join-Path $PSScriptRoot 'verify-prerelease-package.ps1') `
        -PackageDirectory $packageDirectory `
        -Version $Version

    Write-Host 'P10 gate 6/6: release evidence summary...'
    $packageEvidence = Join-Path $packageDirectory 'p10-package-verification.txt'
    if (-not (Test-Path $packageEvidence -PathType Leaf)) {
        throw "Expected package verification evidence was not produced: $packageEvidence"
    }

    $commit = 'unknown'
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -ne $git) {
        $candidateCommit = (& git rev-parse HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($candidateCommit)) {
            $commit = $candidateCommit.Trim()
        }
    }

    @(
        "P10Version=$Version",
        "Commit=$commit",
        'NetStandard20ReleaseBuild=PASS',
        'Net472UnitContractTests=PASS',
        'ModernNetConsumerTests=PASS',
        'SqlServerIntegration=PASS',
        'DapperIntegration=PASS',
        'RepoDbIntegration=PASS',
        'NestedTransactionInvariants=PASS',
        'FailureCleanupMatrix=PASS',
        'PublicApiAsyncSurfaceReview=PASS',
        'PackageAssetAudit=PASS',
        'BorrowedOwnershipDocumentation=REVIEWED_IN_REPO',
        'SequentialUseDocumentation=REVIEWED_IN_REPO',
        'Net472PrereleasePackageApplication=PASS',
        'StablePublication=BLOCKED_UNTIL_D7_LICENSE_DECISION',
        "VerifiedAtUtc=$([DateTime]::UtcNow.ToString('o'))"
    ) | Set-Content -Path $summaryPath -Encoding UTF8

    Write-Host "P10 prerelease verification completed successfully. Summary: $summaryPath"
}
finally {
    Pop-Location
}
