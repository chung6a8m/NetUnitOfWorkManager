# Usage

## Create the manager

`UnitOfWorkManager` accepts a factory that returns a new provider-native `DbConnection` for each root Unit of Work:

```csharp
using Microsoft.Data.SqlClient;
using NetUnitOfWorkManager;

UnitOfWorkManager manager = new UnitOfWorkManager(
    () => new SqlConnection(connectionString));
```

The connection factory must return a connection that the Unit of Work is allowed to own, open, and dispose. Returning an already-open connection is also supported; the Unit of Work still owns and disposes that connection when the root finalizes.

## ADO.NET safe path

For raw ADO.NET, prefer `scope.Db.CreateCommand()`:

```csharp
using System.Data.Common;

using (IUnitOfWorkScope scope = manager.Begin())
{
    using (DbCommand command = scope.Db.CreateCommand())
    {
        command.CommandText = "UPDATE Orders SET Status = 'Posted' WHERE Id = 42";
        command.ExecuteNonQuery();
    }

    scope.Complete();
}
```

`CreateCommand()` creates the provider-native command from the root connection and assigns the current transaction before returning it.

The caller owns the command and should dispose it normally.

## Explicit rollback

```csharp
using (IUnitOfWorkScope scope = manager.Begin())
{
    using (DbCommand command = scope.Db.CreateCommand())
    {
        command.CommandText = "DELETE FROM Queue WHERE Id = 10";
        command.ExecuteNonQuery();
    }

    scope.Rollback();
}
```

After `Rollback()`, the scope is settled and its database session must not be used again.

## Nested service scopes

Nested `Begin()` calls reuse the same physical connection and transaction:

```csharp
using (IUnitOfWorkScope outer = manager.Begin())
{
    SaveHeader(outer);

    using (IUnitOfWorkScope inner = manager.Begin())
    {
        SaveLines(inner);
        inner.Complete();
    }

    outer.Complete();
}
```

The inner `Complete()` only settles the inner scope. It does not commit the physical transaction while the outer scope is still active.

If an inner scope calls `Rollback()` or is disposed without `Complete()`/`Rollback()`, the root becomes rollback-only. A later outer `Complete()` cannot turn that transaction back into a commit.

P13 hardening exercises this rule at 64 nested scopes. The bound is a deterministic regression check, not a throughput recommendation or a statement that applications should deliberately create deep nesting.

## Ambient suppression

`Suppress()` temporarily hides the current ambient Unit of Work in the current logical execution flow. Suppression is an ambient visibility primitive only: creating or disposing a suppression token does not open a connection, begin a transaction, commit, rollback, or dispose the hidden root.

The three intended forms are:

```text
Begin()                    -> root or nested scope in the current transaction
Suppress()                 -> no ambient Unit of Work
Suppress() + Begin()       -> independent root transaction
```

A `Begin()` inside a suppression region cannot see the outer root, so it asks the connection factory for a new connection and starts a different physical transaction. That independent root can also request a different isolation level:

```csharp
using System.Data;

using (IUnitOfWorkScope outer = manager.Begin(
    new UnitOfWorkOptions(IsolationLevel.Serializable)))
{
    IUnitOfWorkContext outerContext = manager.Current;

    using (manager.Suppress())
    {
        // No ambient root is visible here.
        // manager.HasCurrent == false

        using (IUnitOfWorkScope independent = manager.Begin(
            new UnitOfWorkOptions(IsolationLevel.ReadCommitted)))
        {
            // independent.Db owns a different connection and transaction.
            independent.Complete();
        }

        // Finalizing the independent root returns to the suppression boundary.
        // manager.HasCurrent == false
    }

    // Disposing the suppression token restores the exact outer root object.
    // ReferenceEquals(outerContext, manager.Current) == true
    outer.Complete();
}
```

Suppression boundaries are stack disciplined:

- nested suppression must be disposed in LIFO order;
- disposing a token twice after a successful dispose is an idempotent no-op;
- disposing an outer suppression token before an inner suppression token throws `UnitOfWorkStateException` without changing ambient state;
- after such an out-of-order failure, the same token can still be disposed successfully after inner boundaries have been unwound;
- disposing a suppression token while an independent root created inside it is still active throws `UnitOfWorkStateException` without orphaning that transaction.

Suppression and restoration flow across `await` through `AsyncLocal`. This does not make a connection or transaction safe for parallel operations: database use inside each Unit of Work remains sequential-only.

P13 stress coverage exercises 32 nested suppression boundaries and 200 repeated suppress/restore cycles. These are deterministic regression bounds, not recommended application nesting levels or performance benchmarks.

Do not use suppression for a transactional outbox, event row, audit row, or any other write that must commit atomically with the outer business transaction. `Suppress() + Begin()` creates an independent transaction, so the inner work can commit even if the outer transaction later rolls back.

### Failure recovery inside suppression

A begin/finalization failure inside a suppression region must not expose or corrupt the hidden outer root. The intended recovery sequence is:

```text
T1 active
  -> Suppress T1
      -> Begin T2
      -> T2 begin/commit/rollback/cleanup fails
      -> no ambient root is visible; suppression boundary remains active
  -> dispose suppression
  -> exact T1 is visible again
```

Do not retry `Complete()` or `Rollback()` on the failed T2 scope. Let the failure propagate, dispose the suppression token through normal `using`/`finally` cleanup, and create a new root for any later retry policy.

Nested scopes created under T2 reuse T2. An inner rollback under T2 marks only T2 rollback-only; it does not mark the hidden T1 rollback-only.

## Isolation level

`UnitOfWorkOptions` can request an isolation level for the root transaction:

```csharp
using System.Data;

using (IUnitOfWorkScope scope = manager.Begin(
    new UnitOfWorkOptions(IsolationLevel.Serializable)))
{
    // Work inside a Serializable transaction.
    scope.Complete();
}
```

Nested scopes must use a compatible option set. A nested request with a different isolation level throws `UnitOfWorkStateException` rather than silently changing the root transaction.

A root created by `Begin()` inside a suppression region is not nested with the hidden outer root and may use a different isolation level.

## Async provider commands inside a synchronous Unit of Work

The Unit of Work lifecycle is synchronous, but database commands may be asynchronous:

```csharp
using (IUnitOfWorkScope scope = manager.Begin())
{
    using (DbCommand command = scope.Db.CreateCommand())
    {
        command.CommandText = "UPDATE WorkItems SET Processed = 1 WHERE Id = 7";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    scope.Complete();
}
```

Do not start several commands in parallel on the same Unit of Work. Await each operation before starting the next one.

The ambient root, nested-root reuse, suppression state, and restoration after suppression are expected to survive normal `await` continuations. A child task created while suppression is active observes the suppressed ambient state inherited from that logical execution context. These ambient guarantees do not change the sequential database-use boundary.

## Dapper

Dapper must receive both the borrowed connection and transaction:

```csharp
using Dapper;

using (IUnitOfWorkScope scope = manager.Begin())
{
    int affectedRows = await scope.Db.Connection.ExecuteAsync(
        "INSERT INTO AuditLog(Message) VALUES (@Message)",
        new { Message = "posted" },
        scope.Db.Transaction);

    string message = await scope.Db.Connection.QuerySingleAsync<string>(
        "SELECT Message FROM AuditLog WHERE Message = @Message",
        new { Message = "posted" },
        scope.Db.Transaction);

    scope.Complete();
}
```

The integration tests verify synchronous and async Dapper commands, rollback behavior, and independent-transaction behavior under suppression.

## RepoDb

RepoDb can use the provider-native borrowed objects directly. The SQL Server integration tests use the typed `SqlConnection` and `SqlTransaction` returned by the session:

```csharp
using Microsoft.Data.SqlClient;
using RepoDb;

using (IUnitOfWorkScope scope = manager.Begin())
{
    SqlConnection connection = (SqlConnection)scope.Db.Connection;
    SqlTransaction transaction = (SqlTransaction)scope.Db.Transaction;

    object identity = connection.Insert(
        "Orders",
        new { Code = "SO-001", Status = "Draft" },
        transaction: transaction);

    scope.Complete();
}
```

Provider-specific RepoDb bootstrap/configuration remains the responsibility of the consuming application. P13 SQL Server tests also verify that a RepoDb write committed by an independent T2 survives a later T1 rollback.

## Borrowed ownership

The Unit of Work owns `scope.Db.Connection` and `scope.Db.Transaction`.

While the scope is active, consumers may use those objects for provider/ORM interop, but must not:

- call `Close()` or `Dispose()` on the connection;
- call `Commit()`, `Rollback()`, or `Dispose()` on the transaction;
- begin another transaction on the same connection;
- change the connection string or database;
- retain either object after the Unit of Work finalizes.

After a scope is settled or the root is finalized, session access fails fast with `UnitOfWorkStateException`.

## Exception handling

Use normal `using` semantics. A scope that leaves the block without being completed is treated as abandoned and makes the root rollback-only:

```csharp
using (IUnitOfWorkScope scope = manager.Begin())
{
    PerformWork(scope);

    // If PerformWork throws, Dispose() settles this scope as abandoned.
    scope.Complete();
}
```

The same normal `using` cleanup restores ambient state when code inside a suppression region throws:

```csharp
using (IUnitOfWorkScope outer = manager.Begin())
{
    try
    {
        using (manager.Suppress())
        {
            ThrowingOperation();
        }
    }
    catch
    {
        // The outer Unit of Work is visible again here.
        throw;
    }
}
```

Do not retry `Complete()` or `Rollback()` on the same scope after a lifecycle failure. Create a new root Unit of Work for a new attempt. P13 hardening verifies that begin, commit, rollback, and cleanup failures clear the failed root so a subsequent fresh `Begin()` cannot accidentally reuse stale ambient state.

## Run the production hardening gate

On Windows, with `NETUOW_SQLSERVER_CONNECTION_STRING` set to a disposable SQL Server database, run:

```powershell
pwsh -File .\scripts\verify-hardening.ps1
```

The command runs both consumer targets, the full `net472` compatibility/reference-sample verifier, and SQL Server ADO.NET/Dapper/RepoDb integration including suppression independence. Missing SQL Server configuration is treated as a hard failure rather than a skipped verification.
