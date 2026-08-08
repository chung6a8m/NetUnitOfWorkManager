# Changelog

All notable changes to this project are documented in this file.

The project follows Semantic Versioning after the public package contract is established.

## [Unreleased]

### Added

- P11 ambient suppression through public `IDisposable Suppress()`, which temporarily hides the current ambient Unit of Work without owning database lifecycle.
- Immutable ambient frame stack with LIFO suppression restore, deterministic misuse detection, manager-instance isolation, and logical async-flow support.
- Explicit `Suppress() + Begin()` path for an independent root connection/transaction, including support for an isolation level different from the hidden outer root.
- Suppression behavior tests covering hide/restore, nested suppression, double/out-of-order dispose, independent-root commit/rollback/failure, manager isolation, async flow, and zero database lifecycle calls from suppression itself.
- P10 prerelease closure verification that audits the produced nupkg/snupkg and consumes the exact prerelease package from a separate real `.NET Framework 4.7.2` application against SQL Server.
- Reflection-based public API guard against accidental `Async`, `Task`, `ValueTask`, `IAsyncEnumerable<T>`, or `IAsyncDisposable` lifecycle surface.
- CI evidence artifact for the P10 package-consumer closure gate.

### Changed

- Ambient storage now uses immutable frames so root finalization inside a suppression region returns to the suppression boundary instead of erasing the hidden parent ambient state.
- Documentation now distinguishes `Begin()` (root/nested), `Suppress()` (no visible ambient Unit of Work), and `Suppress() + Begin()` (independent root), including the warning not to use suppression for writes that require atomic commit with the outer transaction.
- Stable `1.0.0` is intentionally deferred until P11-P13 are complete and the P14 release gates, including any remaining repository license decision, are satisfied.

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
