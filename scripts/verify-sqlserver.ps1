param(
    [string]$ConnectionString = $env:NETUOW_SQLSERVER_CONNECTION_STRING
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runningOnWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT

if (-not $runningOnWindows) {
    throw 'P12 SQL Server integration verification must run on Windows because the integration projects target .NET Framework 4.7.2.'
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'Provide -ConnectionString or set NETUOW_SQLSERVER_CONNECTION_STRING before running P12 SQL Server verification.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$coreProject = 'src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj'
$integrationProject = 'tests/NetUnitOfWorkManager.SqlServer.Tests/NetUnitOfWorkManager.SqlServer.Tests.csproj'
$dapperSampleProject = 'samples/NetUnitOfWorkManager.Sample.Dapper.Net472/NetUnitOfWorkManager.Sample.Dapper.Net472.csproj'
$repoDbSampleProject = 'samples/NetUnitOfWorkManager.Sample.RepoDb.Net472/NetUnitOfWorkManager.Sample.RepoDb.Net472.csproj'
$previousConnectionString = $env:NETUOW_SQLSERVER_CONNECTION_STRING

Push-Location $repoRoot
try {
    $env:NETUOW_SQLSERVER_CONNECTION_STRING = $ConnectionString

    Write-Host 'Restoring SQL Server integration test project...'
    dotnet restore $integrationProject
    if ($LASTEXITCODE -ne 0) { throw 'SQL Server integration project restore failed.' }

    Write-Host 'Restoring Dapper net472 console sample...'
    dotnet restore $dapperSampleProject
    if ($LASTEXITCODE -ne 0) { throw 'Dapper net472 console sample restore failed.' }

    Write-Host 'Restoring RepoDb net472 console sample...'
    dotnet restore $repoDbSampleProject
    if ($LASTEXITCODE -ne 0) { throw 'RepoDb net472 console sample restore failed.' }

    Write-Host 'Building netstandard2.0 core in Release...'
    dotnet build $coreProject -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Core Release build failed.' }

    Write-Host 'Building SQL Server integration tests for net472...'
    dotnet build $integrationProject -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'SQL Server integration test build failed.' }

    Write-Host 'Building Dapper console sample for net472...'
    dotnet build $dapperSampleProject -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Dapper net472 console sample build failed.' }

    Write-Host 'Building RepoDb console sample for net472...'
    dotnet build $repoDbSampleProject -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'RepoDb net472 console sample build failed.' }

    Write-Host 'Verifying that core has no ORM or DI package references...'
    [xml]$coreXml = Get-Content -LiteralPath $coreProject -Raw
    $forbiddenPackages = @(
        'Dapper',
        'RepoDb',
        'RepoDb.SqlServer',
        'Microsoft.Extensions.DependencyInjection',
        'Microsoft.Extensions.DependencyInjection.Abstractions'
    )
    $packageReferences = @($coreXml.SelectNodes('/Project/ItemGroup/PackageReference'))

    foreach ($packageReference in $packageReferences) {
        $include = $packageReference.GetAttribute('Include')
        if ($forbiddenPackages -contains $include) {
            throw "Core project must not reference integration package '$include'."
        }
    }

    Write-Host 'Running SQL Server, Dapper and RepoDb integration tests on net472...'
    dotnet test $integrationProject -c Release -f net472 --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'P12 SQL Server integration tests failed.' }

    Write-Host 'Running Dapper net472 console sample against SQL Server...'
    dotnet run --project $dapperSampleProject -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Dapper net472 console sample failed.' }

    Write-Host 'Running RepoDb net472 console sample against SQL Server...'
    dotnet run --project $repoDbSampleProject -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'RepoDb net472 console sample failed.' }

    Write-Host 'P12 SQL Server, Dapper and RepoDb integration verification completed successfully.'
}
finally {
    $env:NETUOW_SQLSERVER_CONNECTION_STRING = $previousConnectionString
    Pop-Location
}
