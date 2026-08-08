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

## Beginning a competing transaction on the borrowed connection

Do not call `BeginTransaction()` on the borrowed connection. A Unit of Work already owns its provider transaction.

If work intentionally requires an independent Unit of Work, suppress the outer ambient root and explicitly call `Begin()` so the manager obtains a new connection:

```csharp
using (IUnitOfWorkScope outer = manager.Begin())
{
    using (manager.Suppress())
    {
        using (IUnitOfWorkScope independent = manager.Begin())
        {
            // Different connection and physical transaction.
            independent.Complete();
        }
    }

    outer.Complete();
}
```

This is not a savepoint and not an implicit `RequiresNew` mode. `Suppress()` alone does not begin a transaction; the independent transaction exists only because `Begin()` is called inside the suppression region.

## Using suppression for work that must be atomic with the outer transaction

Do not suppress the outer Unit of Work for transactional outbox rows, domain-event rows, audit rows, or any write that must commit atomically with the business change:

```csharp
using (IUnitOfWorkScope outer = manager.Begin())
{
    SaveBusinessData(outer);

    using (manager.Suppress())
    using (IUnitOfWorkScope independent = manager.Begin())
    {
        SaveTransactionalOutboxRow(independent); // Wrong atomicity boundary.
        independent.Complete();
    }

    outer.Rollback(); // The outbox row may already be committed.
}
```

Keep atomic work in the same root transaction. Use suppression only when an independent commit/rollback boundary is actually intended.

## Disposing suppression out of order

Suppression scopes are stack disciplined. Do not dispose an outer token before an inner token:

```csharp
IDisposable first = manager.Suppress();
IDisposable second = manager.Suppress();

first.Dispose(); // Wrong; throws UnitOfWorkStateException.
```

The exception does not mutate ambient state. Dispose `second` first, then `first`. A second dispose after a token was successfully disposed is an idempotent no-op.

## Disposing suppression while an independent root is active

Do not restore an outer ambient root while an independent root created inside the suppression boundary is still active:

```csharp
IDisposable suppression = manager.Suppress();
IUnitOfWorkScope independent = manager.Begin();

suppression.Dispose(); // Wrong; throws UnitOfWorkStateException.
```

Settle the independent scope first. Its finalization returns the manager to the suppressed state; only then dispose the suppression token to restore the previous outer ambient frame.

## Running database operations in parallel

Do not fan out work that shares one Unit of Work:

```csharp
Task first = SaveAAsync(scope);
Task second = SaveBAsync(scope);
await Task.WhenAll(first, second); // Unsupported.
```

One Unit of Work supports sequential database use. Await one operation before starting the next one.

`Suppress()` flowing across `await` does not change this rule. Ambient flow and database-operation concurrency are separate concerns.

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
