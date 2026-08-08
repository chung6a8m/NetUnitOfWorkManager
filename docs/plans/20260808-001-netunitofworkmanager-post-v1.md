# NetUnitOfWorkManager post-v1 development plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan phase-by-phase. Mỗi phase phải được review và verification độc lập trước khi chuyển sang phase kế tiếp.

**Goal:** Tiếp tục roadmap sau P10 nhưng chưa phát hành stable v1 ngay; bổ sung Ambient suppression, hoàn thiện reference samples và production hardening trước khi phát hành `1.0.0`, sau đó mới khóa compatibility baseline của v1.

**Architecture:** Core vẫn là package `netstandard2.0` nhỏ, provider-native và synchronous lifecycle. Ambient suppression được thêm như một primitive điều khiển visibility của ambient Unit of Work, không phải transaction primitive: `Suppress()` không tự tạo transaction, không commit/rollback/dispose root hiện tại và không thay thế `RequiresNew`. Reference samples tiếp tục dùng `ProjectReference`; package-reference smoke test chỉ được dùng ở release gate để kiểm chứng đúng artifact NuGet.

**Tech Stack:** C# / .NET Standard 2.0, .NET Framework 4.7.2, .NET 8 consumer tests, ADO.NET, SQL Server, Dapper, RepoDb, xUnit, PowerShell, GitHub Actions, NuGet package validation.

## Starting point

Plan này tiếp nối `docs/plans/20260807-001-netunitofworkmanager-v1.md` sau khi P01-P10 đã tạo được baseline kỹ thuật cho v1:

- core target duy nhất `netstandard2.0`;
- minimum runtime promise là .NET Framework 4.7.2+;
- synchronous Unit of Work lifecycle;
- nested scopes dùng rollback-all semantics;
- provider-native `DbConnection` / `DbTransaction` / `DbCommand`;
- SQL Server, Dapper và RepoDb integration verification;
- prerelease package closure và real `net472` package smoke test;
- stable `1.0.0` chưa được phát hành.

P11-P13 cố ý chạy **trước** stable release để Ambient suppression trở thành một phần của public v1 contract ngay từ đầu.

## Global constraints

- Core tiếp tục target duy nhất `netstandard2.0` trong toàn bộ P11-P15.
- Minimum runtime promise tiếp tục là .NET Framework 4.7.2+.
- Không thêm `BeginAsync`, `CompleteAsync`, `RollbackAsync`, `DisposeAsync` hoặc `IAsyncDisposable`.
- Không thêm runtime ORM dependency vào core.
- Không thêm savepoint, `RequiresNew`, ambient suppression bằng `System.Transactions`, automatic retry hoặc transaction wrapper hierarchy.
- Nested `Begin()` ngoài suppression vẫn reuse cùng physical root transaction và giữ rollback-all semantics.
- `Suppress()` chỉ điều khiển ambient visibility; bản thân `Suppress()` không open connection, không begin transaction, không commit, không rollback và không dispose root resource.
- `Suppress()` + `Begin()` là cách explicit để tạo một independent root transaction trong suppression region.
- Database use trên cùng Unit of Work vẫn là sequential-only; plan này không thêm parallel-operation serialization.
- Ambient state tiếp tục là per `UnitOfWorkManager` instance; suppress một manager không được ảnh hưởng manager khác.
- P12 reference samples bắt buộc dùng `ProjectReference` tới core.
- Package-reference smoke application là release verification infrastructure của P14, không phải P12 reference sample.
- Compiler warnings của source projects tiếp tục là errors trong CI.
- Không thay đổi historical P01-P10 semantics trừ phần ambient storage cần thiết để support suppression một cách stack-safe.

---

## P11 — Ambient suppression

### Deliverable

Thêm public `Suppress()` API cho phép tạm thời ẩn ambient Unit of Work hiện tại trong logical execution flow, sau đó restore chính xác ambient state trước suppression.

### Public API baseline

Modify:

```text
src/NetUnitOfWorkManager/IUnitOfWorkManager.cs
src/NetUnitOfWorkManager/UnitOfWorkManager.cs
```

Public contract mới:

```csharp
public interface IUnitOfWorkManager
{
    bool HasCurrent { get; }
    IUnitOfWorkContext Current { get; }
    IUnitOfWorkScope Begin(UnitOfWorkOptions? options = null);
    IDisposable Suppress();
}
```

Không thêm public `IUnitOfWorkSuppressionScope` ở v1. `IDisposable` là đủ vì suppression token chỉ quản lý ambient frame, không sở hữu database resource.

### Internal design

Create:

```text
src/NetUnitOfWorkManager/Internal/AmbientUnitOfWorkFrame.cs
src/NetUnitOfWorkManager/Internal/UnitOfWorkSuppression.cs
```

Modify:

```text
src/NetUnitOfWorkManager/UnitOfWorkManager.cs
```

`UnitOfWorkManager` chuyển từ:

```csharp
AsyncLocal<RootUnitOfWork?>
```

sang một `AsyncLocal<AmbientUnitOfWorkFrame?>` theo manager instance.

Mỗi ambient frame phải đủ thông tin để biểu diễn:

```text
current root, nếu có
suppression boundary identity
parent frame cần restore
```

Frame phải được xem là immutable theo logical flow: khi root được publish/finalize hoặc suppression được push/pop, manager gán một frame mới vào `AsyncLocal` thay vì mutate shared frame object. Mục tiêu là tránh child execution context vô tình sửa ambient state của parent qua shared mutable holder.

Một suppression boundary phải có identity riêng, ví dụ monotonic `long` id theo manager. Exact storage là internal implementation detail, nhưng behavior phải hỗ trợ stack discipline sau:

```text
Root T1
  -> Suppress S1: no current root
      -> Suppress S2: no current root
          -> dispose S2: restore S1
      -> dispose S1: restore T1
```

### Required behavior

1. `Suppress()` khi có root T1 làm `HasCurrent == false` trong suppression region và `Current` throw `UnitOfWorkStateException`.
2. `Suppress()` không settle hoặc thay đổi rollback state của T1.
3. Dispose suppression token hợp lệ restore đúng T1 object, không tạo root mới.
4. `Suppress()` khi không có ambient root vẫn hợp lệ và giữ `HasCurrent == false`.
5. Nested suppression phải restore theo LIFO.
6. Dispose suppression token lần hai sau một dispose hợp lệ là idempotent no-op.
7. Dispose suppression token sai thứ tự phải throw `UnitOfWorkStateException` và không mutate ambient state.
8. Nếu suppression region đang có independent root active, dispose suppression trước khi root đó finalize phải throw `UnitOfWorkStateException` và không làm orphan transaction.
9. `Begin()` trong suppression region không thấy outer root và tạo independent physical root T2 bằng connection factory.
10. T2 có thể dùng `IsolationLevel` khác T1 vì T1 đang bị suppress, không phải nested root.
11. Sau T2 commit/rollback/finalization failure, suppression boundary vẫn tồn tại với no current root; chỉ dispose suppression token mới restore T1.
12. Exception từ code bên trong `using (manager.Suppress())` không được làm mất outer ambient khi suppression token được dispose trong `finally`/`using` cleanup.
13. Suppress manager A không làm thay đổi `HasCurrent`/`Current` của manager B.
14. Ambient suppression phải flow qua `await` theo semantics của `AsyncLocal`; database operations vẫn phải sequential.
15. Không public API nào expose raw ambient frame, suppression id, parent frame hoặc ambient reset method.

### Implementation ordering

- [ ] Add failing public-contract test xác nhận `IUnitOfWorkManager.Suppress()` tồn tại và return đúng `IDisposable`.
- [ ] Add failing suppression behavior tests cho hide/restore, nested suppression, independent root và misuse cases.
- [ ] Add internal ambient frame abstraction và suppression token tối thiểu để tests pass.
- [ ] Refactor `BeginRoot()` và root finalization để root bên trong suppression được clear về suppression frame thay vì xóa luôn outer parent.
- [ ] Add async-flow tests trên test project multi-target.
- [ ] Update documentation và examples.
- [ ] Run cả `net8.0`, `net472` và existing SQL Server verification trước khi đóng P11.

### Tests

Modify/create:

```text
tests/NetUnitOfWorkManager.Tests/PublicContractTests.cs
tests/NetUnitOfWorkManager.Tests/UnitOfWorkManagerTests.cs
tests/NetUnitOfWorkManager.Tests/AmbientSuppressionTests.cs
```

Required test names:

```text
Suppress_Hides_Current_Ambient_Root
Suppress_Current_Throws_While_No_Inner_Root
Suppress_Dispose_Restores_Exact_Outer_Root
Suppress_Without_Current_Is_Valid
Nested_Suppress_Restores_In_Lifo_Order
Suppression_Double_Dispose_Is_Idempotent
Suppression_Out_Of_Order_Dispose_Throws_Without_Corrupting_Ambient
Suppression_Cannot_Be_Disposed_While_Independent_Root_Is_Active
Begin_Inside_Suppression_Creates_Different_Physical_Transaction
Begin_Inside_Suppression_Allows_Different_IsolationLevel
Independent_Root_Commit_Returns_To_Suppressed_State
Independent_Root_Rollback_Returns_To_Suppressed_State
Independent_Root_Finalization_Failure_Returns_To_Suppressed_State
Exception_Inside_Suppression_Restores_Outer_Ambient
Suppress_One_Manager_Does_Not_Affect_Another_Manager
Suppression_Flows_Across_Await
Outer_Ambient_Flows_Across_Await_After_Suppression_Restore
```

`PublicContractTests` phải đồng thời tiếp tục chứng minh public surface không thêm lifecycle method suffix `Async`, `Task`, `ValueTask`, `IAsyncEnumerable<T>` hoặc `IAsyncDisposable`.

### Documentation

Modify:

```text
README.md
docs/usage.md
docs/feature-scope.md
docs/anti-patterns.md
```

Docs phải giải thích rõ ba trường hợp:

```text
Begin()                    -> root hoặc nested scope hiện tại
Suppress()                 -> no ambient Unit of Work
Suppress() + Begin()       -> independent root transaction
```

Docs phải cảnh báo không dùng suppression cho transactional outbox/event row cần atomic commit cùng business data.

### Verification

Run:

```powershell
dotnet test .\tests\NetUnitOfWorkManager.Tests\NetUnitOfWorkManager.Tests.csproj -c Release -f net8.0
pwsh -File .\scripts\verify-net472.ps1
pwsh -File .\scripts\verify-sqlserver.ps1
```

### Acceptance criteria

- `Suppress()` không thực hiện database lifecycle call nào.
- Outer root không bị commit/rollback/dispose chỉ vì suppression token được dispose.
- `Suppress()` + `Begin()` tạo connection/transaction khác outer root.
- Nested suppression và misuse đều deterministic, không dựa `Task.Delay` hoặc timing race.
- Existing nested UoW tests vẫn pass không thay semantics.
- Core vẫn zero runtime NuGet dependency.

---

## P12 — Samples & Integration Reference

### Deliverable

Có bộ reference samples dễ đọc cho ADO.NET, Dapper và RepoDb trên `net472`, dùng cùng public Unit of Work contract và minh họa cả nested scopes lẫn Ambient suppression.

### Project-reference policy

Mọi project thuộc P12 bắt buộc reference source core bằng:

```xml
<ProjectReference Include="../../src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj" />
```

P12 không chuyển reference sample sang local NuGet package và không dùng `PackageReference Include="NetUnitOfWorkManager"`.

`NetUnitOfWorkManager.PrereleaseSmoke.Net472` hiện có là release/package verification infrastructure và nằm ngoài policy này; P14 vẫn được phép dùng package reference ở project đó để test artifact thật.

### Files

Keep/modify:

```text
samples/NetUnitOfWorkManager.Sample.Net472/Program.cs
samples/NetUnitOfWorkManager.Sample.Net472/README.md
samples/NetUnitOfWorkManager.Sample.RepoDb.Net472/NetUnitOfWorkManager.Sample.RepoDb.Net472.csproj
samples/NetUnitOfWorkManager.Sample.RepoDb.Net472/SampleRunner.cs
samples/NetUnitOfWorkManager.Sample.RepoDb.Net472/README.md
```

Create:

```text
samples/NetUnitOfWorkManager.Sample.Dapper.Net472/NetUnitOfWorkManager.Sample.Dapper.Net472.csproj
samples/NetUnitOfWorkManager.Sample.Dapper.Net472/Program.cs
samples/NetUnitOfWorkManager.Sample.Dapper.Net472/Infrastructure/SampleDatabase.cs
samples/NetUnitOfWorkManager.Sample.Dapper.Net472/Models/CounterItem.cs
samples/NetUnitOfWorkManager.Sample.Dapper.Net472/Repositories/ICounterRepository.cs
samples/NetUnitOfWorkManager.Sample.Dapper.Net472/Repositories/DapperCounterRepository.cs
samples/NetUnitOfWorkManager.Sample.Dapper.Net472/Services/CounterService.cs
samples/NetUnitOfWorkManager.Sample.Dapper.Net472/SampleRunner.cs
samples/NetUnitOfWorkManager.Sample.Dapper.Net472/README.md
samples/README.md
```

Modify:

```text
NetUnitOfWorkManager.sln
scripts/verify-net472.ps1
scripts/verify-sqlserver.ps1
```

Dapper sample dùng cùng Dapper version đang được SQL Server integration project kiểm chứng và có thể dùng framework `System.Data.SqlClient` trên `net472`; không thêm dependency vào core.

### Reference scenarios

#### Provider-native ADO.NET sample

`NetUnitOfWorkManager.Sample.Net472` tiếp tục là in-process runtime/provider probe không cần SQL Server. Bổ sung:

```text
outer root visible
suppression hides outer root
nested suppression restore LIFO
independent fake root inside suppression
outer root restored after inner root finalization
suppression flows across async command continuation
```

Sample này tiếp tục là compatibility/runtime probe, không biến thành business repository sample.

#### Dapper SQL Server sample

Dùng `NETUOW_SQLSERVER_CONNECTION_STRING` và demonstrate:

```text
single UoW commit
explicit rollback
nested service reuses one physical transaction
Dapper Execute/Query receives scope.Db.Connection + scope.Db.Transaction
suppression hides outer transaction
Suppress() + Begin() creates independent transaction
inner independent commit followed by outer rollback
```

Repository không được cache hoặc dispose borrowed `scope.Db.Connection` / `scope.Db.Transaction`.

#### RepoDb SQL Server sample

Giữ cấu trúc DI/repository/service hiện có và bổ sung suppression scenario tương đương Dapper sample. Tiếp tục dùng RepoDb attributes/entity operations đang có; không đưa RepoDb mapping logic vào core.

### Sample consistency rules

- Cùng environment variable: `NETUOW_SQLSERVER_CONNECTION_STRING` cho real SQL Server samples.
- Cùng terminology: root, nested scope, rollback-only, suppression, independent root.
- README của mỗi sample phải chỉ rõ connection/transaction ownership.
- Sample code phải fail process nếu scenario invariant không đạt; không chỉ log lỗi rồi exit code `0`.
- Không dùng `Task.Delay` để chứng minh ambient flow.
- Không dùng parallel operations trên cùng connection/transaction.

### Implementation ordering

- [ ] Add Dapper sample project với `ProjectReference` và DI/repository/service structure tương đương RepoDb sample ở mức cần thiết.
- [ ] Add standard commit/rollback/nested scenarios cho Dapper sample.
- [ ] Add suppression/independent-root scenario cho Dapper và RepoDb samples.
- [ ] Extend provider-native `net472` runtime probe với suppression lifecycle scenarios.
- [ ] Add `samples/README.md` làm index, nêu rõ sample nào cần SQL Server và sample nào không.
- [ ] Add solution entries và verification script execution.
- [ ] Verify từng sample trên Windows `net472`.

### Acceptance criteria

- Tất cả P12 reference samples dùng `ProjectReference` tới core.
- Có reference path rõ cho ADO.NET, Dapper và RepoDb.
- Dapper/RepoDb samples chứng minh transaction object được truyền explicit cho ORM operation.
- Ít nhất một real SQL Server sample chứng minh independent inner commit survives outer rollback.
- `verify-net472.ps1` và `verify-sqlserver.ps1` fail nếu sample bắt buộc không build/run được.
- Core project file vẫn không reference Dapper, RepoDb hoặc DI packages.

---

## P13 — Production Hardening

### Deliverable

Stress và failure-path verification đủ mạnh để đóng các lỗ hổng lifecycle/ambient trước stable release, với Ambient suppression là một phần bắt buộc của hardening matrix.

### Files

Create:

```text
tests/NetUnitOfWorkManager.Tests/AmbientSuppressionHardeningTests.cs
tests/NetUnitOfWorkManager.Tests/AsyncFlowHardeningTests.cs
tests/NetUnitOfWorkManager.Tests/LifecycleStressTests.cs
tests/NetUnitOfWorkManager.SqlServer.Tests/SuppressionIntegrationTests.cs
scripts/verify-hardening.ps1
```

Modify as needed:

```text
tests/NetUnitOfWorkManager.Tests/Fakes/*
tests/NetUnitOfWorkManager.Tests/FailureCleanupTests.cs
tests/NetUnitOfWorkManager.Tests/UnitOfWorkManagerTests.cs
tests/NetUnitOfWorkManager.SqlServer.Tests/SqlServerFixture.cs
.github/workflows/ci.yml
README.md
docs/compatibility.md
docs/usage.md
```

### Core lifecycle hardening matrix

Required deterministic scenarios:

```text
64-level nested complete finalizes physical transaction exactly once
64-level nested rollback at inner level forces one final rollback
64-level nested abandon forces one final rollback
200 sequential root Unit of Work instances leave no stale ambient state
connection factory can return an already-open connection
begin failure after previous successful root does not restore stale root
commit failure cannot make manager reuse faulted root
rollback failure cannot make manager reuse faulted root
cleanup failure still allows a fresh subsequent root Begin
multiple manager instances remain isolated through repeated use
```

Counts `64` và `200` là deterministic regression bounds, không phải throughput benchmark.

### Async logical-flow hardening

Tests có thể dùng `async` test methods vì public Unit of Work lifecycle vẫn synchronous. Required scenarios:

```text
root ambient survives await continuation
nested scope started after await reuses same root
suppression remains effective across await
outer root restores after awaited suppression region
child Task created while suppressed observes suppressed ambient
manager remains usable after awaited suppression exception
```

Không chạy concurrent database commands trên cùng root để test các scenario này.

### Ambient suppression hardening matrix

Required scenarios beyond P11 unit coverage:

1. Nested suppression depth lớn, ví dụ 32 levels, restore đúng root sau unwind.
2. Repeated suppress/restore 200 lần không để stale suppression frame.
3. Out-of-order dispose throw nhưng token vẫn có thể được dispose hợp lệ sau khi inner token kết thúc.
4. Failed out-of-order dispose không đổi `HasCurrent`, `Current` hoặc outer root identity.
5. Begin failure bên trong suppression không làm mất suppression boundary hoặc outer root.
6. Commit failure của independent T2 không làm mất outer T1.
7. Rollback failure của independent T2 không làm mất outer T1.
8. Cleanup failure của independent T2 không làm mất outer T1.
9. Suppression của manager A không affect root/suppression stack của manager B.
10. Suppression region có thể chứa nested scopes của independent T2; các nested scopes này reuse T2 chứ không nhìn thấy T1.
11. Inner rollback trong T2 doom T2 nhưng không set rollback-only cho T1.
12. T2 finalization xong phải trở về suppressed state trước khi T1 được restore.

### Real SQL Server suppression verification

`SuppressionIntegrationTests` phải dùng hai physical connections/transactions và tránh artificial lock conflict bằng execution order rõ ràng.

Required scenarios:

```text
Independent_Commit_Survives_Outer_Rollback
Independent_Rollback_Does_Not_Prevent_Outer_Commit
Independent_Transaction_Uses_Different_Connection
Independent_Transaction_Can_Use_Different_IsolationLevel
Dapper_Independent_Commit_Survives_Outer_Rollback
RepoDb_Independent_Commit_Survives_Outer_Rollback
```

Pattern cho scenario đầu:

```text
Begin T1
  Suppress T1
    Begin T2
    write audit marker
    Commit T2
  restore T1
  write business marker
Rollback T1
Assert audit marker exists
Assert business marker does not exist
```

T2 write xảy ra trước T1 write để tránh test vô tình phụ thuộc lock behavior của concurrent transactions.

### Hardening verifier

`scripts/verify-hardening.ps1` phải chạy tối thiểu:

```text
net8.0 unit/contract/hardening tests
net472 unit/contract/hardening tests
verify-net472.ps1
verify-sqlserver.ps1
```

Trên machine không có `NETUOW_SQLSERVER_CONNECTION_STRING`, SQL Server hardening là failure khi chạy release/hardening gate; không silently skip.

### Implementation ordering

- [ ] Add core lifecycle stress tests trước, giữ tất cả test deterministic.
- [ ] Add async logical-flow tests không dùng parallel database access.
- [ ] Add suppression misuse/failure recovery matrix.
- [ ] Add real SQL Server suppression integration tests cho ADO.NET, Dapper và RepoDb.
- [ ] Add one-command hardening verifier.
- [ ] Wire hardening verifier vào Windows CI gate.
- [ ] Update compatibility/usage docs với verified suppression semantics và sequential-use boundary.

### Acceptance criteria

- Không ambient/root/suppression frame stale sau stress loops.
- Independent T2 failures không orphan hoặc corrupt outer T1.
- SQL Server chứng minh commit/rollback independence thật, không chỉ bằng fakes.
- Ambient behavior qua `await` deterministic trên cả supported consumer targets.
- Không test nào dùng sleep/delay để tạo correctness condition.
- CI stable-release path bị block nếu P13 hardening fail.

---

## P14 — Stable v1.0.0 Release

### Deliverable

Phát hành chính artifact `NetUnitOfWorkManager` version `1.0.0` sau khi P11-P13 pass và license gate được quyết định rõ ràng.

### Preconditions

P14 chỉ được xem là bắt đầu release closure khi:

```text
P11 complete
P12 complete
P13 complete
P10 prerelease/package closure vẫn pass sau các thay đổi P11-P13
D7 license decision đã chọn license cho public stable package
```

D7 không được tự động chọn bởi implementation agent. Nếu chưa chọn MIT hoặc Apache-2.0 (hoặc một license public-compatible khác được owner chỉ định rõ), public stable publication bị block và P14 chưa hoàn tất.

### Files

Modify:

```text
src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj
README.md
CHANGELOG.md
docs/compatibility.md
docs/usage.md
docs/prerelease-verification.md
.github/workflows/ci.yml
```

Create:

```text
scripts/verify-release.ps1
```

Reuse:

```text
scripts/verify-prerelease.ps1
scripts/verify-prerelease-package.ps1
samples/NetUnitOfWorkManager.PrereleaseSmoke.Net472/*
```

Package-smoke project ở P14 tiếp tục dùng `PackageReference Include="NetUnitOfWorkManager"` vì mục tiêu của nó là verify produced nupkg, khác với P12 source reference samples.

### Package metadata

Stable package phải có ít nhất:

```text
PackageId = NetUnitOfWorkManager
Version = 1.0.0
Authors
Description
RepositoryUrl / RepositoryType
PackageTags
PackageReadmeFile
license metadata theo D7
Source Link metadata hiện có
symbol package .snupkg
XML documentation
```

Không còn `preview.1` suffix trong stable artifact.

### Release verification

`scripts/verify-release.ps1` phải verify **exact stable candidate artifact** theo thứ tự:

1. Run `net8.0` unit/contract tests.
2. Run full Windows `net472` verification.
3. Run P13 hardening verifier.
4. Run SQL Server ADO.NET/Dapper/RepoDb verification.
5. Pack exact `1.0.0` nupkg/snupkg.
6. Audit package payload và zero runtime dependency budget.
7. Restore/build/run real `net472` package-smoke application từ local feed chứa exact `1.0.0` nupkg.
8. Confirm public contract có `Suppress()` và không có accidental async lifecycle surface.
9. Write release evidence dưới `artifacts/release/1.0.0`.

Không rebuild một artifact khác sau khi verification để publish. Artifact được publish phải là exact nupkg/snupkg đã qua release verifier.

### Release checklist

- [ ] Core `netstandard2.0` Release build clean trên Windows và Linux.
- [ ] `net8.0` consumer tests pass.
- [ ] `net472` consumer/unit/contract tests pass.
- [ ] Ambient suppression contract tests pass.
- [ ] P13 production hardening pass.
- [ ] SQL Server ADO.NET integration pass.
- [ ] Dapper integration pass.
- [ ] RepoDb integration pass.
- [ ] P12 reference samples build/run theo policy của từng sample.
- [ ] Stable nupkg payload audit pass.
- [ ] Stable snupkg audit pass.
- [ ] Real `net472` application loads and executes exact stable package.
- [ ] Borrowed ownership docs complete.
- [ ] Suppression semantics docs complete.
- [ ] Sequential-use/no-parallel contract documented.
- [ ] License metadata matches D7 decision.
- [ ] CHANGELOG contains `1.0.0` section.
- [ ] Git tag `v1.0.0` points to release source commit.
- [ ] GitHub Release / NuGet publication uses exact verified artifact.

### Acceptance criteria

- Stable `1.0.0` không được publish nếu bất kỳ verification gate nào fail.
- Stable package vẫn chỉ expose intended `netstandard2.0` library assets và metadata.
- `Suppress()` là một phần documented của v1 public contract.
- P14 không thêm feature mới; chỉ release closure, metadata, evidence và publication.

---

## P15 — Freeze v1 Public API / Compatibility Baseline

### Deliverable

Sau khi `1.0.0` đã được publish, dùng chính stable package đó làm API/package compatibility baseline bắt buộc cho các release tiếp theo.

### Files

Modify:

```text
src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj
tests/NetUnitOfWorkManager.Tests/PublicContractTests.cs
.github/workflows/ci.yml
docs/compatibility.md
docs/decisions.md
CHANGELOG.md
```

Create:

```text
scripts/verify-api-compatibility.ps1
```

### Package validation baseline

Sau khi `1.0.0` có thể restore từ package source chính thức, configure package validation baseline bằng stable version, ví dụ:

```xml
<PackageValidationBaselineVersion>1.0.0</PackageValidationBaselineVersion>
```

Exact SDK property usage phải được verify bằng `dotnet pack` trên SDK version của CI; không disable compatibility error chỉ để làm pack pass.

### Frozen v1 contract

Baseline bao gồm toàn bộ public surface đã release, đặc biệt:

```text
IUnitOfWorkManager.HasCurrent
IUnitOfWorkManager.Current
IUnitOfWorkManager.Begin(...)
IUnitOfWorkManager.Suppress()
IUnitOfWorkContext
IUnitOfWorkScope
UnitOfWorkOptions
UnitOfWorkDbSession
UnitOfWorkStateException
```

P15 không được xóa, rename hoặc đổi signature member đã ship trong `1.0.0`.

### Compatibility policy

Document và enforce:

- Patch release `1.0.x`: bug fixes, docs, internal hardening; không breaking public API.
- Minor release `1.x.0`: có thể additive API nếu có use case và compatibility verification pass.
- Breaking public API hoặc semantic contract cần major-version decision.
- Thêm runtime NuGet dependency vào core cần explicit architecture decision, không được xem là incidental package change.
- Bỏ `netstandard2.0` hoặc nâng minimum runtime khỏi .NET Framework 4.7.2+ cần explicit major compatibility decision.
- `Suppress()` semantics đã ship là contract: suppression không tự tạo transaction và không tự settle outer root.

### Compatibility verifier

`scripts/verify-api-compatibility.ps1` phải:

1. Restore baseline package `NetUnitOfWorkManager` version `1.0.0`.
2. Pack current source với package validation enabled.
3. Fail nếu package validation báo binary/API incompatibility không được chủ động chấp nhận theo versioning policy.
4. Inspect current package để bảo đảm runtime dependency budget không thay đổi ngoài quyết định explicit.
5. Run `PublicContractTests` để giữ custom invariants mà generic package validation không biết, gồm no fake-async lifecycle và suppression surface.

### CI gate

Mọi PR sau P15 chạm:

```text
src/NetUnitOfWorkManager/**
Directory.Build.props
package metadata
public contract tests
```

phải chạy compatibility verifier cùng normal build/test jobs.

### Implementation ordering

- [ ] Configure `1.0.0` as package-validation baseline.
- [ ] Add compatibility verifier và test failure behavior bằng một controlled local incompatibility trong development branch, sau đó revert controlled change.
- [ ] Extend public contract tests với stable v1 surface assertions.
- [ ] Wire compatibility verification vào CI.
- [ ] Document semantic versioning và runtime/dependency compatibility policy.
- [ ] Run full pack + compatibility verification với source không thay public API để chứng minh baseline gate pass.

### Acceptance criteria

- `1.0.0` là machine-enforced compatibility baseline, không chỉ documentation statement.
- Accidental removal/signature change của `Suppress()` hoặc các v1 public members làm CI fail.
- Accidental runtime dependency addition được detect trước release.
- Existing `net472` compatibility promise vẫn được kiểm tra ở CI.
- P15 không thay đổi stable v1 public API; chỉ freeze và enforce contract đã ship.

---

## Explicitly deferred after P15

Plan này vẫn không tự động mở các feature sau:

```text
RequiresNew
savepoints
BeginAsync / async commit / async rollback
IAsyncDisposable
ambient suppression via System.Transactions
ambient suppression opt-out flags trong UnitOfWorkOptions
parallel operation serialization
operation lease / reader wrapper
provider-specific async lifecycle adapters
net8.0 optimized target framework
repository cache/factory trong core
automatic transaction retry
```

`Suppress()` cung cấp primitive nhỏ để caller explicit compose independent root bằng:

```csharp
using (manager.Suppress())
using (IUnitOfWorkScope independent = manager.Begin())
{
    // independent transaction
    independent.Complete();
}
```

Điều này không được dùng làm lý do để silently đổi `Begin()` thành `RequiresNew` semantics.

## Recommended execution order

```text
P11 Ambient suppression
  -> P12 Samples & Integration Reference
  -> P13 Production Hardening
  -> P14 Stable v1.0.0 Release
  -> P15 Freeze v1 Public API / Compatibility Baseline
```

P11 thay đổi public surface trước stable release.

P12 chứng minh feature bằng consumer code dễ đọc và giữ `ProjectReference` workflow thuận tiện cho development.

P13 chứng minh ambient/lifecycle correctness qua stress, async logical flow, failure recovery và real SQL Server transactions.

P14 chỉ phát hành stable artifact sau khi các semantics mới đã được chứng minh.

P15 mới khóa `1.0.0` thành compatibility baseline cho mọi release tiếp theo.
