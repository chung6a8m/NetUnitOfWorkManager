# Anti-patterns

This library exposes provider-native database objects deliberately. That makes integration simple, but it also means consumers must respect ownership and transaction boundaries.

## Disposing the borrowed connection

Do not close or dispose `scope.Db.Connection`:

```csharp
using (IUnitOfWorkScope scope = manager.Begin())
{
    scope.Db.Connection.Dispose(); // Wrong.
    scope.Complete();
}
```

The root Unit of Work owns the connection lifecycle. Premature disposal can make final commit/rollback and nested scopes fail.

## Finalizing the borrowed transaction yourself

Do not call `Commit()`, `Rollback()`, or `Dispose()` on `scope.Db.Transaction`:

```csharp
scope.Db.Transaction.Commit(); // Wrong.
scope.Complete();
```

`Complete()` and `Rollback()` settle scopes; the root decides when the physical transaction can be finalized.

## Forgetting to pass the transaction to Dapper

This is unsafe:

```csharp
await scope.Db.Connection.ExecuteAsync(sql, args); // Wrong inside the UoW.
```

Use:

```csharp
await scope.Db.Connection.ExecuteAsync(
    sql,
    args,
    scope.Db.Transaction);
```

For raw ADO.NET, prefer `scope.Db.CreateCommand()` so transaction binding is automatic.

## Beginning a competing transaction

Do not call `BeginTransaction()` on the borrowed connection. A Unit of Work already owns its provider transaction.

If work requires an independent transaction, create a different `UnitOfWorkManager`/connection boundary explicitly rather than trying to nest `RequiresNew` semantics into v1.

## Running database operations in parallel

Do not fan out work that shares one Unit of Work:

```csharp
Task first = SaveAAsync(scope);
Task second = SaveBAsync(scope);
await Task.WhenAll(first, second); // Unsupported.
```

One Unit of Work supports sequential database use. Await one operation before starting the next one.

## Assuming nested scopes are savepoints

Nested scopes share the root transaction. They are coordination tokens, not savepoints.

If an inner scope rolls back or is abandoned, the root becomes rollback-only. Completing the outer scope afterward does not create a partial commit.

## Reusing a settled scope

After `Complete()`, `Rollback()`, or abandonment, do not access `scope.Db` again. The scope token has finished its lifecycle and session access fails fast.

## Calling Complete twice

Each scope can settle exactly once:

```csharp
scope.Complete();
scope.Complete(); // Wrong; throws.
```

The same rule applies to `Rollback()` after `Complete()` and `Complete()` after `Rollback()`.

## Retrying commit/rollback on the same root after provider failure

A commit failure can leave provider transaction outcome unknown. The library intentionally does not retry commit or automatically rollback after a commit failure.

Treat the failed Unit of Work as terminal. Resolve the application-level retry policy outside that root and start a new Unit of Work if a retry is safe.

## Changing database or connection configuration mid-scope

Do not change database, connection string, or other connection identity while a Unit of Work is active. The root transaction is tied to the provider connection it created.

## Wrapping the library with fake async lifecycle

Avoid application wrappers that implement `CompleteAsync()` by calling synchronous `Complete()` and returning a completed task. That advertises async transaction lifecycle that the underlying `netstandard2.0` contract does not provide.

Async command execution is supported when the database provider supports it; transaction lifecycle remains synchronous by design.
