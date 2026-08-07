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

## Nested scope contract

Nested scopes share one physical transaction. They do not create savepoints or `RequiresNew` transactions.

An inner rollback or abandoned scope marks the root transaction rollback-only. The physical transaction is committed or rolled back only after all active scopes settle.

## Semantic version strategy

The package follows Semantic Versioning for the public API and documented behavioral contract.

- Prerelease line: `1.0.0-preview.N`.
- First stable release: `1.0.0` after P10 verification succeeds.
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

## License gate

The package project intentionally does not declare NuGet license metadata yet because decision gate D7 has not been closed. This does not block package-quality implementation, but it blocks public stable NuGet publication.
