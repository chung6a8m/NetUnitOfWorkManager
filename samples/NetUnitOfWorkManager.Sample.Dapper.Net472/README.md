# Dapper reference sample (`net472`)

This console sample shows how a .NET Framework 4.7.2 application can use NetUnitOfWorkManager with Dapper and SQL Server while keeping transaction lifecycle ownership in the Unit of Work layer.

## Requirements

Set:

```text
NETUOW_SQLSERVER_CONNECTION_STRING
```

The application creates/reuses `[dbo].[NetUnitOfWorkCounter]` and resets its rows between scenarios.

## Reference model

The project references the source core directly:

```xml
<ProjectReference Include="../../src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj" />
```

Dapper is a consumer dependency only. It is not referenced by the core package.

`DapperCounterRepository` borrows the ambient database session:

```csharp
UnitOfWorkDbSession db = unitOfWorkManager.Current.Db;

db.Connection.Execute(
    sql,
    args,
    transaction: db.Transaction);
```

The repository does not open, close, cache, commit, rollback, or dispose `db.Connection` / `db.Transaction`.

## Scenarios

The runner verifies:

1. a single Unit of Work commits;
2. explicit rollback removes its write;
3. nested scopes reuse the same physical connection and transaction;
4. `Suppress()` hides the outer root;
5. `Suppress() + Begin()` creates a different physical root transaction with a different isolation level;
6. the independent inner transaction commits, suppression returns to the hidden state, and the exact outer root is restored;
7. the outer root rolls back while the independent inner commit remains durable.

The final suppression scenario therefore demonstrates the important distinction:

```text
Begin()                    -> current root or nested scope
Suppress()                 -> no ambient Unit of Work
Suppress() + Begin()       -> independent root transaction
```

Do not use suppression for work that must commit atomically with the outer business transaction, such as an outbox/event row belonging to the same atomic operation.

## Run

```powershell
$env:NETUOW_SQLSERVER_CONNECTION_STRING = '<connection string>'
dotnet run --project .\samples\NetUnitOfWorkManager.Sample.Dapper.Net472\NetUnitOfWorkManager.Sample.Dapper.Net472.csproj -c Release
```

Any failed invariant throws and causes a non-zero process exit code.
