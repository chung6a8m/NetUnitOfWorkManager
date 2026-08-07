# Changelog

All notable changes to this project are documented in this file.

The project follows Semantic Versioning after the public package contract is established.

## [Unreleased]

### Added

- P10 prerelease closure verification that audits the produced nupkg/snupkg and consumes the exact prerelease package from a separate real `.NET Framework 4.7.2` application against SQL Server.
- Reflection-based public API guard against accidental `Async`, `Task`, `ValueTask`, `IAsyncEnumerable<T>`, or `IAsyncDisposable` lifecycle surface.
- CI evidence artifact for the P10 package-consumer closure gate.

### Changed

- Stable `1.0.0` now requires successful P10 technical verification and remains blocked from public publication until the D7 license decision is closed.

## [1.0.0-preview.1] - 2026-08-07

### Added

- `netstandard2.0` core package with a .NET Framework 4.7.2+ compatibility floor.
- Minimal synchronous Unit of Work contracts: `Begin`, `Complete`, `Rollback`, and `Dispose`.
- Provider-native `DbConnection`/`DbTransaction` session with transaction-bound `CreateCommand()`.
- Nested ambient scopes that share one physical transaction and enforce rollback-all semantics.
- Deterministic lifecycle, cleanup, failure, and ambient-state tests.
- Real .NET Framework 4.7.2 runtime compatibility probe and CI-ready verification script.
- SQL Server integration verification for ADO.NET, Dapper, and RepoDb without adding ORM/provider runtime dependencies to the core package.
- NuGet package metadata, portable symbol package generation, Source Link metadata, and SDK package validation.
- Cross-platform build/test/package CI plus Windows SQL Server integration verification.
- Public README, usage guide, compatibility contract, anti-pattern guide, and package release documentation.

### Design constraints

- Unit of Work transaction lifecycle remains synchronous by design.
- Nested scopes do not use savepoints or `RequiresNew`.
- Borrowed connection/transaction objects remain owned by the Unit of Work.
- Parallel database operations on the same Unit of Work connection/transaction are unsupported.
