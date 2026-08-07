Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runningOnWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT

if (-not $runningOnWindows) {
    throw 'P07 verification must run on Windows because it executes .NET Framework 4.7.2-targeted binaries.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$coreProject = 'src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj'
$sampleProject = 'samples/NetUnitOfWorkManager.Sample.Net472/NetUnitOfWorkManager.Sample.Net472.csproj'
$testProject = 'tests/NetUnitOfWorkManager.Tests/NetUnitOfWorkManager.Tests.csproj'
$sampleExe = Join-Path $repoRoot 'samples/NetUnitOfWorkManager.Sample.Net472/bin/Release/net472/NetUnitOfWorkManager.Sample.Net472.exe'

Push-Location $repoRoot
try {
    Write-Host 'Restoring solution...'
    dotnet restore .\NetUnitOfWorkManager.sln
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    Write-Host 'Building netstandard2.0 core in Release...'
    dotnet build $coreProject -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Core Release build failed.' }

    Write-Host 'Building the .NET Framework 4.7.2 sample consumer...'
    dotnet build $sampleProject -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'net472 sample build failed.' }

    if (-not (Test-Path $sampleExe -PathType Leaf)) {
        throw "Expected net472 sample executable was not produced: $sampleExe"
    }

    Write-Host 'Executing the .NET Framework 4.7.2 sample runtime probe...'
    & $sampleExe
    if ($LASTEXITCODE -ne 0) { throw "net472 sample runtime probe failed with exit code $LASTEXITCODE." }

    Write-Host 'Running the full test suite on the net472 target...'
    dotnet test $testProject -c Release -f net472 --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'net472 test target failed.' }

    Write-Host 'P07 .NET Framework 4.7.2 runtime compatibility verification completed successfully.'
}
finally {
    Pop-Location
}
