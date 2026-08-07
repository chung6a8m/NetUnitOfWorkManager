# P10 prerelease verification and v1 closure

P10 turns the v1 release checklist into executable gates around the actual prerelease NuGet package.

The goal is not to publish stable `1.0.0` automatically. The goal is to prove that the prerelease artifact satisfies the v1 runtime, integration, public API, package, and documentation contracts before the D7 license decision allows a public stable release.

## One-command local verification

Run P10 on Windows with SQL Server available through `NETUOW_SQLSERVER_CONNECTION_STRING`:

```powershell
$env:NETUOW_SQLSERVER_CONNECTION_STRING = 'Server=(localdb)\MSSQLLocalDB;Initial Catalog=tempdb;Integrated Security=true;TrustServerCertificate=true'
pwsh -File .\scripts\verify-prerelease.ps1 -Version 1.0.0-preview.1
```

The verifier performs these gates in order:

1. run the modern `.NET 8.0` consumer/unit/contract tests;
2. run `scripts/verify-net472.ps1`, including the real `.NET Framework 4.7.2` runtime probe and the full `net472` test target;
3. run `scripts/verify-sqlserver.ps1`, covering SQL Server ADO.NET, Dapper, and RepoDb integration;
4. pack the requested prerelease version in Release configuration with package validation enabled;
5. audit the nupkg payload, runtime dependency budget, and symbol package;
6. restore, build, and execute a separate `.NET Framework 4.7.2` application from the produced local nupkg;
7. write verification evidence under `artifacts/prerelease`.

A missing SQL Server connection string is a failure. P10 does not silently downgrade SQL Server or `net472` verification.

## Actual-package application smoke test

`samples/NetUnitOfWorkManager.PrereleaseSmoke.Net472` is intentionally not a project-reference sample. It uses:

```xml
<PackageReference Include="NetUnitOfWorkManager" Version="$(NetUnitOfWorkManagerVersion)" />
```

`scripts/verify-prerelease-package.ps1` restores that application from a local feed containing the nupkg produced by the release pack step.

The application runs on `.NET Framework 4.7.2` and uses the framework `System.Data.SqlClient` provider against the configured SQL Server. It verifies:

- the prerelease assembly can be loaded by a real `net472` application;
- provider-native ADO.NET interop works through the package;
- nested scopes share the root database state;
- the root transaction commits successfully;
- ambient state is cleared so a follow-up root Unit of Work can start.

This closes the P10 requirement that the prerelease package be tried in at least one real `.NET Framework 4.7.2+` application rather than only through source/project references.

## Package asset audit

The main nupkg is allowed to expose only these intended payload files, in addition to NuGet metadata:

```text
README.md
lib/netstandard2.0/NetUnitOfWorkManager.dll
lib/netstandard2.0/NetUnitOfWorkManager.xml
```

The verifier also requires:

- a `.snupkg` for the same version;
- `lib/netstandard2.0/NetUnitOfWorkManager.pdb` inside the symbol package;
- no runtime NuGet `<dependency>` entries in the generated nuspec.

Unexpected public payload files fail P10.

## Public API closure

`PublicContractTests` reviews the exported assembly surface as an executable invariant. Public types must not introduce:

- methods ending in `Async`;
- `Task`, `ValueTask`, or `IAsyncEnumerable<T>` return/property types;
- `IAsyncDisposable` implementation.

This is in addition to the existing v1 contract tests for synchronous `Begin`, `Complete`, `Rollback`, and `Dispose` behavior.

## Release checklist mapping

| P10 checklist item | Executable evidence |
| --- | --- |
| `netstandard2.0` Release build clean | Windows/Linux build jobs and prerelease pack |
| `net472` unit/contract tests pass | `scripts/verify-net472.ps1` / CI `test-net472` |
| modern .NET consumer tests pass | `net8.0` test target |
| SQL Server integration pass | `scripts/verify-sqlserver.ps1` |
| Dapper integration pass | SQL Server integration test project |
| RepoDb integration pass | SQL Server integration test project; failure blocks P10 |
| nested transaction invariants pass | unit/contract test suite on both consumer targets |
| failure/cleanup matrix pass | deterministic failure/cleanup tests |
| package contains only intended public assets | `scripts/verify-prerelease-package.ps1` |
| public API reviewed for accidental async/fake-async surface | reflection-based public contract test |
| borrowed ownership documented | README and `docs/usage.md` |
| sequential-use/no-parallel contract documented | README, `docs/usage.md`, and `docs/compatibility.md` |
| prerelease package tested in a real `net472` app | package-reference smoke application |
| changelog and compatibility statement complete | `CHANGELOG.md` and `docs/compatibility.md` |

## CI closure gate

The `P10 prerelease package closure` job depends on the `pack` job. The `pack` job already depends on cross-platform builds, both consumer test targets, and SQL Server/Dapper/RepoDb verification.

CI uploads the produced prerelease nupkg/snupkg, downloads those exact artifacts into a Windows closure job, starts SQL Server LocalDB, and runs `scripts/verify-prerelease-package.ps1`.

The closure job publishes `p10-package-verification.txt` as CI evidence.

## Stable v1 gate

Passing P10 means the technical prerelease checklist is closed for the verified artifact.

Public stable `1.0.0` publication remains blocked until decision gate D7 selects a license and the package metadata is updated accordingly. P10 must not bypass that gate.
