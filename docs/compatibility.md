# Compatibility and release contract

## Target framework

`NetUnitOfWorkManager` v1 ships one implementation assembly:

```text
lib/netstandard2.0/NetUnitOfWorkManager.dll
```

The package does not multi-target modern .NET separately in v1.

## Runtime floor

The minimum supported legacy runtime is **.NET Framework 4.7.2+**.

This is a runtime promise, not only a compile-time statement. Windows CI runs the `net472` consumer/test target and the runtime compatibility probe from `scripts/verify-net472.ps1`.

Modern .NET compatibility is exercised through the `net8.0` test target. Other runtimes are supported only to the extent that they correctly consume `netstandard2.0`; they are not individually certified unless CI explicitly tests them.

## Database provider contract

The core package depends only on platform ADO.NET contracts. It does not take runtime dependencies on SQL Server, Dapper, or RepoDb.

SQL Server, Dapper, and RepoDb interoperability is verified in the separate `NetUnitOfWorkManager.SqlServer.Tests` project. Those packages are test dependencies only.

## Synchronous lifecycle contract

Unit of Work lifecycle is deliberately synchronous:

```text
Begin()
Complete()
Rollback()
Dispose()
```

There is no v1 promise for `BeginAsync`, `CompleteAsync`, `RollbackAsync`, or `DisposeAsync`.

Database commands may still use provider-native async APIs such as `DbCommand.ExecuteNonQueryAsync()` or Dapper `ExecuteAsync()` while the Unit of Work itself retains synchronous transaction lifecycle.

## Sequential-use contract

One Unit of Work owns one provider connection and transaction. Database operations within that Unit of Work must be sequential.

Parallel operations on the same borrowed connection/transaction are unsupported even if the calling runtime or provider exposes async APIs.

P13 async-flow hardening uses `await` continuations and child logical flows only to verify ambient propagation. It does not run concurrent database commands on one Unit of Work.

## Nested scope contract

Nested scopes share one physical transaction. They do not create savepoints or `RequiresNew` transactions.

An inner rollback or abandoned scope marks the root transaction rollback-only. The physical transaction is committed or rolled back only after all active scopes settle.

P13 exercises deterministic 64-level nesting to verify that deep completion finalizes the physical transaction exactly once and that an inner rollback or abandon still produces one final rollback.

## Ambient suppression contract

`Suppress()` controls ambient visibility only. It does not open a connection, begin a transaction, commit, rollback, or dispose the hidden root.

A `Begin()` inside a suppression region creates a separate root Unit of Work with its own provider connection and transaction. Nested scopes created under that independent root reuse the independent transaction; they cannot see or mark the hidden outer root rollback-only.

Suppression boundaries are LIFO, flow with `AsyncLocal`, and are isolated per `UnitOfWorkManager` instance. Failed out-of-order disposal, begin failures, independent-root finalization failures, and cleanup failures must not corrupt the hidden outer ambient state.

P13 hardening exercises 32 nested suppression boundaries and 200 repeated suppress/restore cycles as deterministic regression bounds. Real SQL Server integration additionally verifies that an independent commit survives outer rollback, an independent rollback does not prevent outer commit, and the independent transaction can use a different connection and isolation level.

## P13 production hardening gate

The Windows hardening entry point is:

```powershell
pwsh -File .\scripts\verify-hardening.ps1
```

The gate runs:

- `net8.0` unit, contract, and hardening tests;
- `net472` unit, contract, and hardening tests;
- the full `verify-net472.ps1` runtime/reference-sample verification;
- the full `verify-sqlserver.ps1` ADO.NET/Dapper/RepoDb integration verification, including suppression independence.

`NETUOW_SQLSERVER_CONNECTION_STRING` is mandatory for this gate. A missing SQL Server connection string is a hard failure rather than a skip. CI runs the hardening verifier on Windows before package creation can proceed.

The loop/depth values in P13 are regression bounds, not throughput benchmarks.

## Semantic version strategy

The package follows Semantic Versioning for the public API and documented behavioral contract.

- Prerelease line: `1.0.0-preview.N`.
- First stable release: `1.0.0` in P14 after P11-P13 hardening and the existing P10 package closure pass, with D7 license decision closed.
- Patch releases (`1.0.x`) are reserved for compatible fixes.
- Minor releases (`1.x.0`) may add backward-compatible API or behavior.
- Breaking public API or documented contract changes require a major version increment after stable v1.

The project file defaults to `1.0.0-preview.1` during the current prerelease phase. Release automation may override `VersionSuffix` for subsequent previews without changing the compatibility contract.

## Package validation and API baseline

`EnablePackageValidation` is enabled in the package project, so `dotnet pack` runs the SDK package validators.

There is intentionally no `PackageValidationBaselineVersion` before the first stable package exists. After `1.0.0` is published, every later stable/prerelease line that is expected to remain compatible with v1 must validate against the latest applicable stable package, starting with:

```xml
<PackageValidationBaselineVersion>1.0.0</PackageValidationBaselineVersion>
```

When a later major version intentionally breaks compatibility, its release process must establish the new stable baseline after that major version is published.

## Source Link and symbols

Release packs enable repository metadata, portable PDBs, and `.snupkg` symbol packages. Builds use .NET 8 SDK or newer CI tooling, where Source Link build support is part of the SDK.

## P10 prerelease closure contract

A prerelease is not considered technically closed from source-project tests alone. P10 verifies the produced NuGet artifact after all lower-level CI gates have passed.

The closure gate requires:

- the main nupkg to expose only `README.md`, `lib/netstandard2.0/NetUnitOfWorkManager.dll`, and `lib/netstandard2.0/NetUnitOfWorkManager.xml` as public payload files;
- the generated nuspec to contain no runtime NuGet dependencies;
- the symbol package to contain `lib/netstandard2.0/NetUnitOfWorkManager.pdb`;
- a separate `.NET Framework 4.7.2` application to restore the exact produced nupkg through `PackageReference` and execute successfully against SQL Server;
- exported public API tests to reject accidental `Async`, `Task`, `ValueTask`, `IAsyncEnumerable<T>`, or `IAsyncDisposable` lifecycle surface.

See `docs/prerelease-verification.md` for the checklist-to-evidence mapping and local verification command.

## License gate

The package project intentionally does not declare NuGet license metadata yet because decision gate D7 has not been closed. This does not block package-quality or P10/P13 technical verification, but it blocks public stable NuGet publication.
