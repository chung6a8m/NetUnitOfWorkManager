param(
    [string]$ConnectionString = $env:NETUOW_SQLSERVER_CONNECTION_STRING
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runningOnWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT

if (-not $runningOnWindows) {
    throw 'P13 production hardening verification must run on Windows because the gate executes .NET Framework 4.7.2 targets and SQL Server integration tests.'
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'Set NETUOW_SQLSERVER_CONNECTION_STRING or provide -ConnectionString before running the P13 production hardening gate.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testProject = 'tests/NetUnitOfWorkManager.Tests/NetUnitOfWorkManager.Tests.csproj'
$previousConnectionString = $env:NETUOW_SQLSERVER_CONNECTION_STRING

Push-Location $repoRoot
try {
    $env:NETUOW_SQLSERVER_CONNECTION_STRING = $ConnectionString

    Write-Host 'Restoring unit, contract and hardening tests...'
    dotnet restore $testProject
    if ($LASTEXITCODE -ne 0) { throw 'P13 test project restore failed.' }

    Write-Host 'Running net8.0 unit, contract and hardening tests...'
    dotnet test $testProject -c Release -f net8.0 --no-restore -p:CI=true
    if ($LASTEXITCODE -ne 0) { throw 'P13 net8.0 hardening tests failed.' }

    Write-Host 'Running net472 unit, contract and hardening tests...'
    dotnet test $testProject -c Release -f net472 --no-restore -p:CI=true
    if ($LASTEXITCODE -ne 0) { throw 'P13 net472 hardening tests failed.' }

    Write-Host 'Running full .NET Framework 4.7.2 compatibility and reference-sample verification...'
    & (Join-Path $PSScriptRoot 'verify-net472.ps1')

    Write-Host 'Running SQL Server, Dapper, RepoDb and suppression integration verification...'
    & (Join-Path $PSScriptRoot 'verify-sqlserver.ps1') -ConnectionString $ConnectionString

    Write-Host 'P13 production hardening verification completed successfully.'
}
finally {
    $env:NETUOW_SQLSERVER_CONNECTION_STRING = $previousConnectionString
    Pop-Location
}
