# P12 Samples & Integration Reference Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver readable `net472` ADO.NET, Dapper, and RepoDb reference samples using the public Unit of Work contract, nested scopes, and ambient suppression.

**Architecture:** Keep the provider-native runtime probe in-process and database-free. Dapper and RepoDb remain SQL Server consumer samples that borrow `Current.Db.Connection` and `Current.Db.Transaction`, never own them, and use `Suppress() + Begin()` for an independent physical root transaction.

**Tech Stack:** C#, .NET Framework 4.7.2, .NET Standard 2.0 core, ADO.NET, Dapper 2.1.79, RepoDb, Microsoft.Extensions.DependencyInjection 8.0.1, SQL Server, PowerShell.

## Global Constraints

- All P12 samples use `ProjectReference` to `../../src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj`.
- SQL Server samples use `NETUOW_SQLSERVER_CONNECTION_STRING`.
- Repository code passes both borrowed connection and transaction explicitly to ORM operations.
- Samples throw on invariant failure so the process exits non-zero.
- No `Task.Delay`, parallel database use, or runtime ORM dependency in core.

---

### Task 1: Tighten net472 verification gate

**Files:**
- Modify: `scripts/verify-net472.ps1`

- [x] Require provider-native, Dapper, and RepoDb sample projects to build on Windows `net472`.
- [ ] Verify all P12 sample projects use the required core `ProjectReference` and do not package-reference `NetUnitOfWorkManager`.

### Task 2: Add Dapper SQL Server reference sample

**Files:**
- Create: `samples/NetUnitOfWorkManager.Sample.Dapper.Net472/*`

- [ ] Add `net472` console project with Dapper `2.1.79`, DI `8.0.1`, and core `ProjectReference`.
- [ ] Add database, model, repository, service, runner, and README.
- [ ] Demonstrate commit, explicit rollback, nested transaction reuse, suppression, independent inner commit, and outer rollback.
- [ ] Assert the independent inner commit survives the outer rollback.

### Task 3: Extend RepoDb sample with suppression

**Files:**
- Modify: `samples/NetUnitOfWorkManager.Sample.RepoDb.Net472/Services/CounterApplicationService.cs`
- Modify: `samples/NetUnitOfWorkManager.Sample.RepoDb.Net472/SampleRunner.cs`
- Modify: `samples/NetUnitOfWorkManager.Sample.RepoDb.Net472/README.md`

- [ ] Add suppression/independent-root scenario using the existing repository and DI structure.
- [ ] Assert the independent commit survives outer rollback.

### Task 4: Extend provider-native runtime probe

**Files:**
- Modify: `samples/NetUnitOfWorkManager.Sample.Net472/Program.cs`
- Modify: `samples/NetUnitOfWorkManager.Sample.Net472/README.md`

- [ ] Add hide/restore, nested suppression LIFO, independent fake root, and async-flow scenarios.
- [ ] Keep the probe SQL Server-free.

### Task 5: Wire solution, docs, and SQL Server verification

**Files:**
- Create: `samples/README.md`
- Modify: `NetUnitOfWorkManager.sln`
- Modify: `scripts/verify-sqlserver.ps1`

- [ ] Add Dapper sample to the solution.
- [ ] Restore/build/run both Dapper and RepoDb samples in SQL Server verification.
- [ ] Add sample index explaining prerequisites and ownership rules.

### Task 6: Verification

- [ ] Run/observe Windows `net472` verification.
- [ ] Run/observe SQL Server verification.
- [ ] Confirm core still has no Dapper, RepoDb, or DI package references.
