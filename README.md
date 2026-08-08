# NetUnitOfWorkManager

`NetUnitOfWorkManager` is a small provider-native Unit of Work library for `netstandard2.0`, designed with a minimum runtime promise of **.NET Framework 4.7.2+**.

The library intentionally keeps transaction lifecycle synchronous (`Begin`, `Complete`, `Rollback`, `Dispose`) while allowing provider commands, Dapper, and other database APIs to use their native async command methods inside an active Unit of Work.

## Status

The project is on the `1.0.0-preview.1` package line. P10 added an executable prerelease closure gate that tests the produced nupkg in a real `.NET Framework 4.7.2` application after all unit, compatibility, SQL Server, Dapper, and RepoDb gates pass.

P11 adds ambient suppression as part of the planned v1 public contract. Stable `1.0.0` is intentionally deferred to P14, after P11-P13 are completed and remaining release blockers such as the repository license decision are closed.

See [P10 prerelease verification](docs/prerelease-verification.md) and the [post-v1 development plan](docs/plans/20260808-001-netunitofworkmanager-post-v1.md).

## Compatibility

- Package target: `netstandard2.0` only.
- Minimum supported legacy runtime: .NET Framework 4.7.2+.
- Modern .NET consumers are supported through `netstandard2.0` compatibility.
- Unit of Work lifecycle is synchronous by design.
- Ambient state flows through logical async execution by `AsyncLocal`.
- Database use inside one Unit of Work must be sequential; parallel operations on the same connection/transaction are unsupported.

See [Compatibility](docs/compatibility.md) for the release contract.

## Basic usage

Create a manager with a provider-native `DbConnection` factory:

```csharp
using System.Data.Common;
using NetUnitOfWorkManager;

UnitOfWorkManager manager = new UnitOfWorkManager(
    () => CreateProviderConnection());

using (IUnitOfWorkScope scope = manager.Begin())
{
    using (DbCommand command = scope.Db.CreateCommand())
    {
        command.CommandText = "UPDATE Accounts SET Balance = Balance - 10 WHERE Id = 1";
        command.ExecuteNonQuery();
    }

    scope.Complete();
}
```

`scope.Db.CreateCommand()` returns the provider-native command and automatically binds it to the Unit of Work transaction.

## Dapper

Dapper should receive both borrowed objects explicitly:

```csharp
using (IUnitOfWorkScope scope = manager.Begin())
{
    await scope.Db.Connection.ExecuteAsync(
        "INSERT INTO AuditLog(Message) VALUES (@Message)",
        new { Message = "saved" },
        scope.Db.Transaction);

    scope.Complete();
}
```

Async database commands do not require an async Unit of Work lifecycle. `Complete()` remains synchronous and owns transaction finalization.

## Nested scopes

Nested `Begin()` calls reuse the same physical connection and transaction but return independent scope tokens:

```csharp
using (IUnitOfWorkScope outer = manager.Begin())
{
    using (IUnitOfWorkScope inner = manager.Begin())
    {
        // Work on the same physical transaction.
        inner.Complete();
    }

    outer.Complete();
}
```

An inner `Rollback()` or an abandoned inner scope marks the root Unit of Work rollback-only. The physical transaction is finalized only after all scopes settle.

## Ambient suppression

`Suppress()` temporarily hides the current ambient Unit of Work without committing, rolling back, or disposing it. Disposing the suppression token restores the exact previous ambient state.

The three intended forms are:

```text
Begin()                    -> root or nested scope in the current transaction
Suppress()                 -> no ambient Unit of Work
Suppress() + Begin()       -> independent root transaction
```

Example:

```csharp
using (IUnitOfWorkScope outer = manager.Begin())
{
    IUnitOfWorkContext outerContext = manager.Current;

    using (manager.Suppress())
    {
        // The outer root is hidden here.
        // manager.HasCurrent == false

        using (IUnitOfWorkScope independent = manager.Begin())
        {
            // Uses a different connection and physical transaction.
            independent.Complete();
        }

        // The suppression boundary is still active here.
        // manager.HasCurrent == false
    }

    // The exact outer root is visible again.
    // ReferenceEquals(outerContext, manager.Current) == true
    outer.Complete();
}
```

Nested suppression restores in LIFO order. A suppression token cannot be disposed while an independent root created inside that suppression boundary is still active; misuse throws `UnitOfWorkStateException` without mutating ambient state.

Suppression flows across `await` with `AsyncLocal`, but database operations on a given Unit of Work must still be sequential.

Do **not** use suppression for transactional outbox/event rows or any write that must commit atomically with the outer business transaction. An independent root can commit even if the outer transaction later rolls back.

## Borrowed ownership contract

`scope.Db.Connection` and `scope.Db.Transaction` are borrowed provider-native objects. The Unit of Work owns their lifecycle.

Do not:

- close or dispose the borrowed connection;
- commit, rollback, or dispose the borrowed transaction;
- start a competing transaction on the same connection;
- change the database or connection string while the Unit of Work is active;
- run parallel operations against the same Unit of Work connection/transaction.

Commands returned by `scope.Db.CreateCommand()` are still caller-owned and should be disposed normally.

## Package quality

CI verifies:

- `netstandard2.0` Release builds on Windows and Linux;
- `net8.0` unit/contract tests;
- real `net472` tests on Windows;
- SQL Server + Dapper + RepoDb integration on Windows;
- NuGet packing with package validation enabled;
- package contents include only the intended public payload;
- compiler warnings fail CI for source projects;
- the exact prerelease nupkg is restored, built, and executed by a separate real `net472` package consumer against SQL Server.

The package produces portable symbols (`.snupkg`) and Source Link metadata using the .NET SDK build tooling.

## Documentation

- [Usage](docs/usage.md)
- [Compatibility and versioning](docs/compatibility.md)
- [P10 prerelease verification](docs/prerelease-verification.md)
- [Anti-patterns](docs/anti-patterns.md)
- [Architecture](docs/netunitofworkmanager-design.md)
- [Feature scope](docs/feature-scope.md)
- [Decisions](docs/decisions.md)
- [Changelog](CHANGELOG.md)

## Core design boundaries

The v1 core intentionally does not provide:

- `BeginAsync`, `CompleteAsync`, `RollbackAsync`, or `DisposeAsync`;
- repository factories/caches;
- savepoints or `RequiresNew` nested transactions;
- wrappers around the complete ADO.NET object model;
- ORM runtime dependencies.

Ambient suppression is deliberately smaller than `RequiresNew`: `Suppress()` only controls ambient visibility. An independent transaction exists only when application code explicitly calls `Begin()` inside the suppression region.

These boundaries keep the package truthful to the APIs available on `netstandard2.0` and .NET Framework 4.7.2+.
