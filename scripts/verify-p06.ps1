param(
    [switch]$SkipNet472
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testProject = 'tests/NetUnitOfWorkManager.Tests/NetUnitOfWorkManager.Tests.csproj'
$coreProject = 'src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj'
$runningOnWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT

Push-Location $repoRoot
try {
    Write-Host 'Restoring solution...'
    dotnet restore .\NetUnitOfWorkManager.sln
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    Write-Host 'Building netstandard2.0 core in Release...'
    dotnet build $coreProject -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Core Release build failed.' }

    Write-Host 'Running P06 failure/cleanup matrix on net8.0...'
    dotnet test $testProject -c Release -f net8.0 --no-restore --filter FullyQualifiedName~FailureCleanupTests
    if ($LASTEXITCODE -ne 0) { throw 'P06 net8.0 failure/cleanup tests failed.' }

    Write-Host 'Running full unit suite on net8.0...'
    dotnet test $testProject -c Release -f net8.0 --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'net8.0 tests failed.' }

    if ($runningOnWindows -and -not $SkipNet472) {
        Write-Host 'Running P06 failure/cleanup matrix on net472...'
        dotnet test $testProject -c Release -f net472 --no-restore --filter FullyQualifiedName~FailureCleanupTests
        if ($LASTEXITCODE -ne 0) { throw 'P06 net472 failure/cleanup tests failed.' }

        Write-Host 'Running full unit suite on net472...'
        dotnet test $testProject -c Release -f net472 --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'net472 tests failed.' }
    }
    elseif (-not $runningOnWindows) {
        Write-Host 'Skipping net472 execution because this host is not Windows.'
    }
    else {
        Write-Host 'Skipping net472 execution by request.'
    }

    Write-Host 'P06 verification completed successfully.'
}
finally {
    Pop-Location
}
