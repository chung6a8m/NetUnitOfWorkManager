[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,

    [string] $Version = '1.0.0-preview.1'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runningOnWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
if (-not $runningOnWindows) {
    throw 'P10 prerelease package verification must run on Windows because it executes a .NET Framework 4.7.2 application.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedPackageDirectory = (Resolve-Path $PackageDirectory).Path
$packagePath = Join-Path $resolvedPackageDirectory "NetUnitOfWorkManager.$Version.nupkg"
$symbolPackagePath = Join-Path $resolvedPackageDirectory "NetUnitOfWorkManager.$Version.snupkg"
$smokeProject = Join-Path $repoRoot 'samples/NetUnitOfWorkManager.PrereleaseSmoke.Net472/NetUnitOfWorkManager.PrereleaseSmoke.Net472.csproj'
$smokeOutputDirectory = Join-Path $repoRoot 'samples/NetUnitOfWorkManager.PrereleaseSmoke.Net472/bin/Release/net472'
$smokeExe = Join-Path $smokeOutputDirectory 'NetUnitOfWorkManager.PrereleaseSmoke.Net472.exe'
$localFeed = Join-Path $resolvedPackageDirectory 'local-feed'
$evidencePath = Join-Path $resolvedPackageDirectory 'p10-package-verification.txt'

if (-not (Test-Path $packagePath -PathType Leaf)) {
    throw "Prerelease package was not found: $packagePath"
}

if (-not (Test-Path $symbolPackagePath -PathType Leaf)) {
    throw "Prerelease symbol package was not found: $symbolPackagePath"
}

if ([string]::IsNullOrWhiteSpace($env:NETUOW_SQLSERVER_CONNECTION_STRING)) {
    throw 'NETUOW_SQLSERVER_CONNECTION_STRING is required to execute the real net472 prerelease package smoke application.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

$archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $fileEntries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
    $entryNames = @($fileEntries | ForEach-Object { $_.FullName.Replace('\', '/') })

    $expectedPayload = @(
        'README.md',
        'lib/netstandard2.0/NetUnitOfWorkManager.dll',
        'lib/netstandard2.0/NetUnitOfWorkManager.xml'
    )

    $publicPayload = @(
        $entryNames |
            Where-Object {
                $_ -ne '[Content_Types].xml' -and
                $_ -ne '.signature.p7s' -and
                $_ -notlike '_rels/*' -and
                $_ -notlike 'package/*' -and
                $_ -notlike '*.nuspec'
            } |
            Sort-Object -Unique
    )

    $missingPayload = @($expectedPayload | Where-Object { $_ -notin $publicPayload })
    $unexpectedPayload = @($publicPayload | Where-Object { $_ -notin $expectedPayload })

    if ($missingPayload.Count -gt 0) {
        throw "Package is missing intended public assets: $($missingPayload -join ', ')"
    }

    if ($unexpectedPayload.Count -gt 0) {
        throw "Package contains unintended public assets: $($unexpectedPayload -join ', ')"
    }

    $nuspecEntries = @($fileEntries | Where-Object { $_.FullName -like '*.nuspec' })
    if ($nuspecEntries.Count -ne 1) {
        throw "Expected exactly one .nuspec entry, found $($nuspecEntries.Count)."
    }

    $reader = New-Object System.IO.StreamReader($nuspecEntries[0].Open())
    try {
        [xml] $nuspec = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }

    $runtimeDependencies = @($nuspec.SelectNodes("//*[local-name()='dependency']"))
    if ($runtimeDependencies.Count -ne 0) {
        $dependencyIds = @($runtimeDependencies | ForEach-Object { $_.GetAttribute('id') })
        throw "Core package unexpectedly contains runtime NuGet dependencies: $($dependencyIds -join ', ')"
    }
}
finally {
    $archive.Dispose()
}

$symbolArchive = [System.IO.Compression.ZipFile]::OpenRead($symbolPackagePath)
try {
    $symbolEntryNames = @(
        $symbolArchive.Entries |
            Where-Object { -not [string]::IsNullOrEmpty($_.Name) } |
            ForEach-Object { $_.FullName.Replace('\', '/') }
    )

    if ('lib/netstandard2.0/NetUnitOfWorkManager.pdb' -notin $symbolEntryNames) {
        throw 'Symbol package does not contain lib/netstandard2.0/NetUnitOfWorkManager.pdb.'
    }
}
finally {
    $symbolArchive.Dispose()
}

New-Item -ItemType Directory -Path $localFeed -Force | Out-Null
Copy-Item $packagePath -Destination $localFeed -Force

$smokeProjectDirectory = Split-Path $smokeProject -Parent
Remove-Item (Join-Path $smokeProjectDirectory 'bin') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $smokeProjectDirectory 'obj') -Recurse -Force -ErrorAction SilentlyContinue

Push-Location $repoRoot
try {
    Write-Host "Restoring the net472 package smoke application from local feed: $localFeed"
    dotnet restore $smokeProject --source $localFeed -p:NetUnitOfWorkManagerVersion=$Version
    if ($LASTEXITCODE -ne 0) {
        throw 'Prerelease package smoke restore failed.'
    }

    Write-Host 'Building the net472 package smoke application...'
    dotnet build $smokeProject -c Release --no-restore -p:NetUnitOfWorkManagerVersion=$Version -p:CI=true
    if ($LASTEXITCODE -ne 0) {
        throw 'Prerelease package smoke build failed.'
    }

    if (-not (Test-Path $smokeExe -PathType Leaf)) {
        throw "Expected net472 prerelease smoke executable was not produced: $smokeExe"
    }

    Write-Host 'Executing the real .NET Framework 4.7.2 prerelease package smoke application...'
    & $smokeExe
    if ($LASTEXITCODE -ne 0) {
        throw "Prerelease package smoke application failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$packageHash = (Get-FileHash $packagePath -Algorithm SHA256).Hash
$verifiedAtUtc = [DateTime]::UtcNow.ToString('o')

@(
    "Version=$Version",
    "Package=$packagePath",
    "PackageSha256=$packageHash",
    "PublicPayload=$($expectedPayload -join ';')",
    'RuntimeDependencies=none',
    'Net472PackageConsumer=PASS',
    'SqlServerProviderSmoke=PASS',
    "VerifiedAtUtc=$verifiedAtUtc"
) | Set-Content -Path $evidencePath -Encoding UTF8

Write-Host "P10 prerelease package verification completed successfully. Evidence: $evidencePath"
