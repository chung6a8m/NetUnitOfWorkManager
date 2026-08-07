# Usage

## Create the manager

`UnitOfWorkManager` accepts a factory that returns a new provider-native `DbConnection` for each root Unit of Work:

```csharp
using Microsoft.Data.SqlClient;
using NetUnitOfWorkManager;

UnitOfWorkManager manager = new UnitOfWorkManager(
    () => new SqlConnection(connectionString));
```

The connection factory must return a connection that the Unit of Work is allowed to own, open, and dispose.

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

The integration tests verify synchronous and async Dapper commands and verify that rollback actually undoes inserted data.

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

Provider-specific RepoDb bootstrap/configuration remains the responsibility of the consuming application.

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

Do not retry `Complete()` or `Rollback()` on the same scope after a lifecycle failure. Create a new root Unit of Work for a new attempt.
