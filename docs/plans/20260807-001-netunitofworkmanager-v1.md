# NetUnitOfWorkManager v1 implementation plan

**Goal:** Xây một Unit of Work manager target `netstandard2.0`, chạy thực tế trên .NET Framework 4.7.2+, có nested ambient scopes và transaction correctness rõ ràng nhưng không mang theo async lifecycle/wrapper complexity của `UnitOfWorkManager` hiện đại.

**Architecture:** Một `UnitOfWorkManager` instance giữ ambient root bằng `AsyncLocal`. Root internal sở hữu provider-native `DbConnection` và `DbTransaction`; mỗi `Begin()` trả scope token riêng. Lifecycle transaction là synchronous. Database commands có thể sync hoặc async tùy provider; ADO.NET safe path đi qua `UnitOfWorkDbSession.CreateCommand()` để auto-bind transaction.

**Primary TFM:** `netstandard2.0`.

**Minimum runtime promise:** .NET Framework 4.7.2+.

## Global constraints

- Core v1 không target `net8.0` riêng.
- Core v1 không có `BeginAsync`, `CompleteAsync`, `RollbackAsync`, `DisposeAsync`.
- Core v1 không reference `Microsoft.Bcl.AsyncInterfaces` chỉ để có `IAsyncDisposable`.
- Core v1 không dùng reflection để gọi provider-specific async transaction lifecycle.
- Core v1 không chứa repository factory/cache.
- Core v1 không full-wrap `DbConnection`, `DbCommand`, `DbDataReader`, `DbTransaction`.
- Nested semantics là rollback-all; không savepoint và không `RequiresNew`.
- Một Unit of Work chỉ hỗ trợ sequential database use; parallel operations trên cùng connection/transaction là unsupported.
- Core runtime dependency budget: không có ORM/runtime NuGet dependency ngoài platform assemblies cần cho `netstandard2.0`.
- Markdown theo quy ước repo; C# build phải warnings-as-errors trong CI.

---

## P01 — Scaffold solution and compatibility floor

### Deliverable

Có solution/project structure build được trên SDK hiện hành, core target duy nhất `netstandard2.0`, test consumer chạy được trên `net472` và modern .NET.

### Files

Create:

```text
NetUnitOfWorkManager.sln
src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj
tests/NetUnitOfWorkManager.Tests/NetUnitOfWorkManager.Tests.csproj
samples/NetUnitOfWorkManager.Sample.Net472/NetUnitOfWorkManager.Sample.Net472.csproj
Directory.Build.props
```

### Required project settings

Core:

```xml
<TargetFramework>netstandard2.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>disable</ImplicitUsings>
<GenerateDocumentationFile>true</GenerateDocumentationFile>
<Deterministic>true</Deterministic>
```

Tests:

```xml
<TargetFrameworks>net472;net8.0</TargetFrameworks>
<IsPackable>false</IsPackable>
```

### Acceptance criteria

- `dotnet restore` thành công.
- `dotnet build src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj -c Release` thành công.
- `dotnet test ... -f net8.0` chạy được trên mọi CI OS hỗ trợ.
- `dotnet test ... -f net472` chạy trên Windows.
- Project core không có runtime package dependency được thêm chỉ để backport modern async APIs.

---

## P02 — Define minimal public contracts

### Deliverable

Khóa public API baseline nhỏ, truthful và đủ cho ADO.NET/Dapper/RepoDb integration.

### Files

Create:

```text
src/NetUnitOfWorkManager/IUnitOfWorkManager.cs
src/NetUnitOfWorkManager/IUnitOfWorkContext.cs
src/NetUnitOfWorkManager/IUnitOfWorkScope.cs
src/NetUnitOfWorkManager/UnitOfWorkOptions.cs
src/NetUnitOfWorkManager/UnitOfWorkDbSession.cs
src/NetUnitOfWorkManager/UnitOfWorkStateException.cs
```

### API baseline

```csharp
public interface IUnitOfWorkManager
{
    bool HasCurrent { get; }
    IUnitOfWorkContext Current { get; }
    IUnitOfWorkScope Begin(UnitOfWorkOptions options = null);
}

public interface IUnitOfWorkContext
{
    UnitOfWorkDbSession Db { get; }
    bool IsRollbackRequested { get; }
}

public interface IUnitOfWorkScope : IUnitOfWorkContext, IDisposable
{
    void Complete();
    void Rollback();
}
```

`UnitOfWorkOptions` chỉ chứa `IsolationLevel?` ở v1.

### Tests

Add API shape tests xác nhận:

- không có lifecycle method suffix `Async`;
- scope implement `IDisposable`, không yêu cầu `IAsyncDisposable`;
- options equality dùng giá trị `IsolationLevel`, không dựa reference identity;
- public API không expose `ClearCurrent()`.

### Acceptance criteria

Public contracts compile trên cả `net472` và `net8.0` consumer tests.

---

## P03 — Implement synchronous root lifecycle

### Deliverable

Root internal sở hữu connection/transaction và có finalization deterministic.

### Files

Create:

```text
src/NetUnitOfWorkManager/Internal/RootUnitOfWork.cs
src/NetUnitOfWorkManager/Internal/UnitOfWorkLifecycleState.cs
src/NetUnitOfWorkManager/Internal/ResourceCleanup.cs
```

### Required behavior

Root lifecycle:

```text
Active -> Finalizing -> Disposed
                   \-> Faulted
```

Root constructor/creation path phải:

1. nhận provider-native `DbConnection`;
2. open connection synchronously nếu chưa open;
3. begin provider-native transaction;
4. chỉ được publish vào ambient state sau khi initialization thành công.

Finalization phải:

- commit khi mọi scope complete và không rollback-only;
- rollback khi có rollback/abandon;
- không retry commit/rollback sau provider failure;
- luôn cố dispose transaction và connection;
- không giữ lifecycle lock trong lúc gọi provider I/O/lifecycle methods;
- giữ được primary failure và cleanup failure khi cả hai cùng xảy ra.

### Tests

Add deterministic fake `DbConnection`/`DbTransaction` test doubles để xác nhận:

- begin opens exactly one connection;
- begin starts exactly one transaction;
- commit exactly once;
- rollback exactly once;
- commit failure marks root faulted;
- rollback failure marks root faulted;
- transaction dispose failure không ngăn connection dispose attempt;
- connection dispose failure được surfaced;
- begin failure disposes connection.

### Acceptance criteria

Không có `TaskCompletionSource`, initialization cancellation state hoặc async lifecycle code trong root.

---

## P04 — Implement ambient manager and nested scope semantics

### Deliverable

Nested services share một physical transaction nhưng nhận scope token riêng.

### Files

Create:

```text
src/NetUnitOfWorkManager/UnitOfWorkManager.cs
src/NetUnitOfWorkManager/Internal/UnitOfWorkScope.cs
src/NetUnitOfWorkManager/Internal/UnitOfWorkScopeState.cs
```

### Manager constructor

Baseline:

```csharp
public UnitOfWorkManager(Func<DbConnection> connectionFactory)
```

Không thêm public transaction factory ở v1.

### Required behavior

- `_current` là instance `AsyncLocal`, không static.
- Root begin mới chỉ publish ambient sau khi open/begin transaction thành công.
- Nested begin reuse root hiện tại.
- Mỗi nested begin tăng active scope count và trả `UnitOfWorkScope` mới.
- Nested options khác isolation level phải throw `UnitOfWorkStateException`.
- `Complete()` settle scope một lần.
- `Rollback()` settle scope một lần và set root rollback-only.
- `Dispose()` của scope chưa settle tương đương abandon và set rollback-only.
- Finalize physical transaction chỉ khi scope count về 0.
- Ambient clear trong root finalization `finally`.
- Scope đã settle không được tiếp tục dùng `Db`.

### Tests

Required tests:

```text
Nested_Begin_Returns_Different_Scope_Objects
Nested_Begin_Reuses_One_Physical_Transaction
Inner_Complete_Does_Not_Commit_Physical_Transaction
Outer_Complete_Commits_After_Inner_Complete
Inner_Rollback_Forces_Final_Rollback
Inner_Abandon_Forces_Final_Rollback
Inner_Dispose_Does_Not_Dispose_Root_Resources
Double_Complete_Throws
Rollback_After_Complete_Throws
Complete_After_Rollback_Throws
Nested_Different_IsolationLevel_Throws
Two_Manager_Instances_Do_Not_Share_Ambient_Root
Ambient_Is_Cleared_After_Commit
Ambient_Is_Cleared_After_Rollback
Ambient_Is_Cleared_After_Finalization_Failure
```

### Acceptance criteria

Không cần reserved/canceled-before-activation scope states vì `Begin()` không await.

---

## P05 — Implement provider-native database session

### Deliverable

ADO.NET code có safe path transaction-bound mà không cần wrapper hierarchy.

### Files

Implement/modify:

```text
src/NetUnitOfWorkManager/UnitOfWorkDbSession.cs
src/NetUnitOfWorkManager/Internal/RootUnitOfWork.cs
```

### Required behavior

`UnitOfWorkDbSession` expose borrowed:

```csharp
DbConnection Connection
DbTransaction Transaction
DbCommand CreateCommand()
```

`CreateCommand()` phải:

```csharp
var command = connection.CreateCommand();
command.Transaction = transaction;
return command;
```

Không wrap command.

`Connection` và `Transaction` getters phải fail-fast bằng `UnitOfWorkStateException` nếu root đã finalized.

### Tests

Required tests:

```text
CreateCommand_Returns_Provider_Native_Command
CreateCommand_Binds_Current_Transaction
CreateCommand_Uses_The_Root_Connection
Db_After_Scope_Settled_Throws
Db_After_Root_Finalized_Throws
IsRollbackRequested_Becomes_True_After_Inner_Rollback
```

### Documentation requirement

README/sample phải ghi rõ borrowed ownership rule:

- không dispose/close connection;
- không dispose transaction;
- không begin competing transaction;
- không change database/connection string trong active UoW.

---

## P06 — Harden failure and cleanup behavior

### Deliverable

Failure paths không leak ambient state hoặc database resources.

### Test matrix

Implement controlled failures cho từng bước:

1. connection factory throws;
2. connection open throws;
3. begin transaction throws;
4. commit throws;
5. rollback throws;
6. transaction dispose throws;
7. connection dispose throws;
8. commit + transaction dispose both throw;
9. rollback + connection dispose both throw.

### Required behavior

- Không ambient root nếu initialization chưa thành công.
- Không tự rollback sau commit failure vì transaction outcome có thể unknown.
- Cleanup vẫn attempt cả transaction và connection.
- Root không quay về `Active` sau finalization failure.
- Caller không thể `Begin()` nested vào root đã faulted/finalizing.

### Acceptance criteria

Failure tests deterministic, không dùng `Task.Delay` để tạo timing window.

---

## P07 — Prove .NET Framework 4.7.2 runtime compatibility

### Deliverable

Có verification chạy thật trên .NET Framework 4.7.2, không chỉ compile `netstandard2.0`.

### Files

Create:

```text
scripts/verify-net472.ps1
samples/NetUnitOfWorkManager.Sample.Net472/Program.cs
samples/NetUnitOfWorkManager.Sample.Net472/README.md
```

### Verification script

Script phải thực hiện tối thiểu:

```text
restore
build core Release
build net472 sample
test net472 test target
```

Không silently skip `net472` khi chạy trên Windows development machine có targeting pack phù hợp.

### Runtime scenarios

Sample/test phải chạy:

- single scope commit;
- explicit rollback;
- nested complete;
- inner rollback -> outer rollback;
- async command execution bên trong synchronous UoW scope nếu provider hỗ trợ.

### Acceptance criteria

Stable release bị block nếu `net472` consumer test không chạy được trên Windows CI.

---

## P08 — Add SQL Server, Dapper and RepoDb integration verification

### Deliverable

Chứng minh interop path hoạt động với provider/ORM thực tế mà không thêm dependency vào core.

### Files

Create:

```text
tests/NetUnitOfWorkManager.SqlServer.Tests/NetUnitOfWorkManager.SqlServer.Tests.csproj
tests/NetUnitOfWorkManager.SqlServer.Tests/SqlServerFixture.cs
tests/NetUnitOfWorkManager.SqlServer.Tests/AdoNetIntegrationTests.cs
tests/NetUnitOfWorkManager.SqlServer.Tests/DapperIntegrationTests.cs
tests/NetUnitOfWorkManager.SqlServer.Tests/RepoDbIntegrationTests.cs
scripts/verify-sqlserver.ps1
```

### Target

SQL Server integration project ưu tiên chạy trên `net472` để kiểm chứng runtime chính.

### Required scenarios

ADO.NET:

- command tạo từ `Db.CreateCommand()` rollback đúng;
- async command của `SqlCommand` chạy trong synchronous UoW scope;
- isolation level được áp dụng.

Dapper:

```text
scope.Db.Connection + scope.Db.Transaction
```

- Execute/Query trong transaction;
- rollback thực sự hoàn tác dữ liệu;
- async Dapper command không yêu cầu async UoW lifecycle.

RepoDb:

- sử dụng provider-native connection/transaction;
- insert/update trong transaction;
- rollback thực sự hoàn tác dữ liệu.

### Acceptance criteria

Core project file không reference Dapper hoặc RepoDb sau khi integration tests được thêm.

---

## P09 — Package quality, CI and public documentation

### Deliverable

Package có release contract rõ ràng và CI cưỡng chế compatibility floor.

### Files

Create/modify:

```text
README.md
.github/workflows/ci.yml
src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj
docs/compatibility.md
docs/usage.md
docs/anti-patterns.md
CHANGELOG.md
```

### CI jobs

Minimum:

1. Build core `netstandard2.0` on Windows.
2. Build core `netstandard2.0` on Linux.
3. Run `net8.0` test target.
4. Run `net472` test target on Windows.
5. Run SQL Server integration verification on Windows environment prepared for it.
6. Pack NuGet package.
7. Validate package contents contain `lib/netstandard2.0/NetUnitOfWorkManager.dll`.
8. Fail on compiler warnings for source projects.

### Package metadata

Before prerelease publish, add:

- `PackageId`;
- semantic version strategy;
- authors;
- description;
- repository URL;
- package tags;
- XML docs;
- Source Link;
- symbols package;
- license metadata after D7 is decided.

### API compatibility

- Enable package validation where applicable.
- Sau stable v1 đầu tiên, dùng stable package đó làm API compatibility baseline cho các release tiếp theo.

---

## P10 — Prerelease verification and v1 closure

### Deliverable

Một prerelease package được kiểm tra trong ứng dụng .NET Framework thật trước stable v1.

### Release checklist

- [ ] `netstandard2.0` Release build clean.
- [ ] `net472` unit/contract tests pass.
- [ ] modern .NET consumer tests pass.
- [ ] SQL Server integration pass.
- [ ] Dapper integration pass.
- [ ] RepoDb integration pass hoặc được ghi rõ chưa support nếu test chưa đạt.
- [ ] nested transaction invariants pass.
- [ ] failure/cleanup matrix pass.
- [ ] package contains only intended public assets.
- [ ] public API reviewed for accidental async/fake-async surface.
- [ ] docs explain borrowed connection/transaction ownership.
- [ ] docs explain sequential-use/no-parallel contract.
- [ ] prerelease package được thử trong ít nhất một application .NET Framework 4.7.2+ thực tế.
- [ ] changelog và compatibility statement hoàn tất.

---

## Decision gate D7 — License before public NuGet stable release

Điểm này không block implementation core, nhưng block public stable publication.

### 1. MIT — Recommended for a small general-purpose library

Cho phép reuse rộng, ít friction.

### 2. Apache-2.0

Có patent grant rõ hơn nhưng license text/notice dài hơn.

### 3. Private/internal distribution only

Không publish public NuGet cho tới khi có quyết định license sau.

Nếu không có chỉ định trước giai đoạn publish, plan mặc định **không tự tạo license** và dừng ở prerelease/internal package.

---

## Explicitly deferred after v1

Không triển khai trong plan này:

```text
BeginAsync / async commit / async rollback
IAsyncDisposable
savepoints
RequiresNew
ambient suppression
System.Transactions integration
automatic retry
transaction timeout abstraction
read-only abstraction
repository cache/factory
operation lease / reader wrapper
parallel operation serialization
provider-specific async lifecycle adapters
net8.0 optimized TFM
```

Mỗi mục deferred chỉ được mở work package mới khi có use case đo được và không làm suy yếu `netstandard2.0` contract.

## Recommended execution order

```text
P01 -> P02 -> P03 -> P04 -> P05 -> P06 -> P07 -> P08 -> P09 -> P10
```

P01-P06 tạo core đúng semantics.

P07 chứng minh runtime target.

P08 chứng minh ecosystem integration.

P09-P10 mới khóa package/release contract.
