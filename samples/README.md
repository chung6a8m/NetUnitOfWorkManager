# NetUnitOfWorkManager reference samples

P12 keeps the reference samples source-linked to the core project. These projects are examples/integration references, not package-consumption smoke tests.

| Sample | Target | Database | Purpose |
| --- | --- | --- | --- |
| `NetUnitOfWorkManager.Sample.Net472` | `net472` | None | Provider-native ADO.NET/runtime probe, nested scopes, suppression, async ambient flow |
| `NetUnitOfWorkManager.Sample.Dapper.Net472` | `net472` | SQL Server | Dapper repository/service reference with explicit borrowed transaction and independent root |
| `NetUnitOfWorkManager.Sample.RepoDb.Net472` | `net472` | SQL Server | RepoDb DI/repository/service reference with nested rollback-only and independent root |
| `NetUnitOfWorkManager.PrereleaseSmoke.Net472` | `net472` | None | Release/package smoke infrastructure; intentionally outside the P12 ProjectReference policy |

## P12 ProjectReference policy

All three reference samples use:

```xml
<ProjectReference Include="../../src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj" />
```

They must not use:

```xml
<PackageReference Include="NetUnitOfWorkManager" ... />
```

The prerelease smoke application is different: it exists to verify a packed NuGet artifact during release verification and therefore remains package-based.

## SQL Server samples

Dapper and RepoDb samples read the same environment variable:

```text
NETUOW_SQLSERVER_CONNECTION_STRING
```

Both use `[dbo].[NetUnitOfWorkCounter]` as disposable sample data and verify the same key lifecycle semantics:

- root commit;
- rollback / rollback-only behavior;
- nested scope reuse of the current physical transaction;
- ORM operation receives the borrowed `Current.Db.Connection` and `Current.Db.Transaction`;
- `Suppress()` hides the outer root;
- `Suppress() + Begin()` creates an independent physical root;
- an independent inner commit survives a later outer rollback.

Repositories do not own the borrowed Unit of Work connection/transaction: they do not open, close, cache, commit, rollback, or dispose them.

## Verification

Windows `net472` compatibility and reference-project policy:

```powershell
pwsh -File .\scripts\verify-net472.ps1
```

Real SQL Server integration plus Dapper/RepoDb sample execution:

```powershell
$env:NETUOW_SQLSERVER_CONNECTION_STRING = '<connection string>'
pwsh -File .\scripts\verify-sqlserver.ps1
```

Every sample throws on an invariant failure so verification receives a non-zero process exit code.
