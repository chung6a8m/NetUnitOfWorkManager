# NetUnitOfWorkManager design

## 1. Executive summary

`NetUnitOfWorkManager` nên được xây như một thư viện Unit of Work **legacy-first** cho .NET Framework 4.7.2+, target duy nhất `netstandard2.0` ở v1.

Điểm quan trọng nhất của thiết kế là **không cố giữ nguyên async lifecycle của `UnitOfWorkManager` hiện tại**. Trên `netstandard2.0`, `DbConnection.BeginTransactionAsync`, `DbTransaction.CommitAsync`, `RollbackAsync` và `DisposeAsync` không nằm trong BCL surface tương đương của .NET Standard 2.0. Nếu giữ nguyên API async, implementation hoặc phải fallback sync, hoặc phải thêm provider-specific adapter/reflection/conditional code. Cả hai hướng đều làm package khó hiểu hơn và tăng chi phí bảo trì.

Vì vậy, lifecycle của Unit of Work được thiết kế synchronous và truthful:

```text
Begin -> use connection/transaction -> Complete/Rollback -> Dispose
```

Các query/command bên trong scope vẫn có thể bất đồng bộ khi provider hỗ trợ async ADO.NET thật sự. Đây là ranh giới quan trọng:

```text
Unit of Work lifecycle: sync
Database command execution: sync hoặc async tùy provider
```

## 2. Vì sao không port nguyên kiến trúc hiện tại

`chung6a8m/UnitOfWorkManager` hiện đã giải quyết nhiều invariant đúng đắn, nhưng để hỗ trợ lifecycle async và cancellation nó phải mang theo nhiều machinery:

- shared initialization task;
- `TaskCompletionSource` cho root initialization;
- chờ initialization với `Task.WaitAsync(cancellationToken)`;
- scope state `Reserved` và `CanceledBeforeActivation`;
- logic release reservation khi caller cancel trong lúc root đang initialize;
- `CancellationTokenSource` riêng cho initialization;
- nhiều flag để tránh dispose `CancellationTokenSource` trong lúc đang dùng;
- async commit/rollback/dispose;
- transaction-bound `DbConnection`, `DbCommand`, `DbDataReader`, `DbTransaction` wrappers;
- operation lease giữ concurrency guard qua vòng đời data reader.

Đây là thiết kế hợp lý hơn khi runtime chính là .NET hiện đại. Khi chuyển xuống `netstandard2.0`, phần phức tạp trên không còn mang lại cùng giá trị vì các primitive lifecycle nền không đồng đều.

Nếu cố giữ nguyên surface sẽ xuất hiện hai vấn đề:

1. **Semantics suy yếu**: method `Async` có thể block thread và cancellation không thật sự dừng I/O.
2. **Compatibility tax**: phải thêm helper, package compatibility, conditional override và provider-specific path chỉ để giữ hình dạng API.

`NetUnitOfWorkManager` tránh cả hai bằng cách bỏ yêu cầu tương thích source với package hiện đại.

## 3. Design principles

### 3.1. Truthful API

Không đặt tên `Async` cho transaction lifecycle nếu core không thể bảo đảm hành vi async ở BCL contract của target framework.

### 3.2. Legacy-first, không phải downgraded-modern

Mọi API public phải được đánh giá từ khả năng của `netstandard2.0` và .NET Framework 4.7.2+, không từ API surface của .NET 8+.

### 3.3. Strong scope ownership, light database interop

Root Unit of Work sở hữu connection và transaction. Mỗi `Begin()` trả một scope token riêng. Tuy nhiên core không wrap toàn bộ ADO.NET để cố chặn mọi misuse của caller.

### 3.4. Safe path rõ ràng, escape hatch rõ ràng

ADO.NET thuần dùng `CreateCommand()` để command tự gắn transaction. Dapper/RepoDb/provider-specific integration có thể dùng borrowed `DbConnection` + `DbTransaction` khi cần.

### 3.5. No false thread-safety

Một database connection/transaction không được dùng song song. Core không tạo runtime guard nửa vời rồi quảng bá như thread-safe.

### 3.6. Dependency-light

Runtime package v1 chỉ phụ thuộc BCL của `netstandard2.0`. Không thêm `Microsoft.Bcl.AsyncInterfaces` chỉ để giữ `IAsyncDisposable`, và không thêm ORM dependency vào core.

## 4. Target contract

### 4.1. Runtime support

- Library TFM: `netstandard2.0`.
- Runtime chính: .NET Framework 4.7.2, 4.8, 4.8.1.
- Modern .NET có thể consume `netstandard2.0` asset, nhưng không phải lý do để thêm API chỉ có ở modern runtime.

### 4.2. Compatibility promise

v1 chỉ công bố những behavior có test consumer thực tế trên `net472`.

Build thành công của project `netstandard2.0` không được coi là đủ để tuyên bố hỗ trợ .NET Framework.

## 5. Proposed public API

API dưới đây là baseline thiết kế, chưa phải implementation final:

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

public sealed class UnitOfWorkDbSession
{
    public DbConnection Connection { get; }

    public DbTransaction Transaction { get; }

    public DbCommand CreateCommand();
}

public sealed class UnitOfWorkOptions
{
    public UnitOfWorkOptions(IsolationLevel? isolationLevel = null);

    public IsolationLevel? IsolationLevel { get; }
}
```

### 5.1. Tại sao dùng `UnitOfWorkDbSession`

`UnitOfWorkDbSession` làm rõ rằng connection và transaction là một cặp resource thuộc cùng Unit of Work.

`Connection` và `Transaction` là **borrowed objects**:

- caller được dùng để chạy query/command;
- caller không được `Close`, `Dispose`, đổi connection string, đổi database hoặc tạo transaction cạnh tranh;
- lifecycle vẫn thuộc root Unit of Work.

Contract này không cố runtime-enforce toàn bộ ownership bằng wrapper inheritance. Đây là trade-off có chủ đích để tránh incompatibility với Dapper/RepoDb/provider code vốn có thể phụ thuộc concrete provider type.

### 5.2. `CreateCommand()` là safe ADO.NET path

`CreateCommand()` phải:

1. tạo command từ provider connection thật;
2. gắn `command.Transaction = Transaction`;
3. trả provider-native `DbCommand`;
4. không wrap command nếu không có yêu cầu cụ thể.

Nhờ vậy ADO.NET code mặc định không thể quên transaction nếu đi qua API được khuyến nghị.

## 6. Root và scope model

### 6.1. Root Unit of Work

Root là object internal duy nhất sở hữu:

- `DbConnection`;
- `DbTransaction`;
- rollback-only flag;
- active scope count;
- lifecycle state;
- callback clear ambient state.

Root không public.

### 6.2. Scope token

Mỗi `Begin()` trả một `UnitOfWorkScope` riêng.

Scope chỉ sở hữu **quyền biểu đạt outcome**, không sở hữu database resource:

- `Complete()` -> scope thành công;
- `Rollback()` -> yêu cầu rollback root;
- `Dispose()` khi chưa settle -> abandoned -> yêu cầu rollback root.

Inner scope dispose không được dispose root resource.

### 6.3. Nested semantics

```text
outer Begin
  root scope count = 1

inner Begin
  reuse same root
  scope count = 2

inner Complete
  scope count = 1
  no real commit

outer Complete
  scope count = 0
  commit real transaction
```

Nếu bất kỳ scope nào `Rollback()` hoặc bị abandon:

```text
root IsRollbackRequested = true
last scope Complete/Dispose
=> real transaction Rollback
```

Đây là rollback-all semantics. v1 không có savepoint và không có nested physical transaction.

## 7. Ambient model

`UnitOfWorkManager` có một instance field:

```csharp
private readonly AsyncLocal<RootHolder> _current;
```

Không dùng static ambient state dùng chung cho mọi manager.

Điều này cho phép hai manager khác nhau có hai ambient slot khác nhau trong cùng logical flow.

### Manager lifetime contract

Các service muốn share cùng Unit of Work phải nhận **cùng manager instance**. Vì vậy documentation và DI sample phải nêu rõ manager lifetime.

Không thêm static dictionary key theo manager ID chỉ để cứu transient manager registration sai; đó là complexity không cần thiết ở core.

## 8. Lifecycle model

Root lifecycle được rút gọn còn các trạng thái cần thiết:

```text
Active -> Finalizing -> Disposed
                   \-> Faulted
```

Initialization xảy ra synchronously trước khi publish root vào `AsyncLocal`, nên không cần `Initializing`, shared initialization task, reservation cancellation hay canceled-before-activation state.

### Begin root

1. Tạo connection.
2. `Open()` nếu chưa open.
3. `BeginTransaction()` với isolation level nếu được cung cấp.
4. Tạo root.
5. Publish root vào ambient state.
6. Acquire scope đầu tiên.

Nếu bước 2 hoặc 3 lỗi:

- connection được dispose;
- ambient state chưa được publish;
- exception provider được giữ nguyên nếu có thể.

### Finalization

Khi scope cuối settle:

- nếu rollback requested -> `Rollback()`;
- ngược lại -> `Commit()`;
- luôn cố dispose transaction và connection;
- luôn clear ambient state trong `finally`.

Nếu commit/rollback lỗi, root chuyển `Faulted`; không tự retry transaction outcome không xác định.

## 9. Error handling policy

### 9.1. Misuse

Dùng một custom exception nhỏ:

```text
UnitOfWorkStateException
```

Dùng cho:

- scope complete/rollback hai lần;
- access scope sau khi scope đã settle;
- nested options không tương thích;
- access root sau khi finalized.

Không tạo hierarchy exception lớn nếu chưa có use case.

### 9.2. Provider errors

Exception từ `Open`, `BeginTransaction`, `Commit`, `Rollback`, command execution được giữ nguyên càng nhiều càng tốt.

### 9.3. Cleanup errors

Cleanup phải thử cả transaction và connection ngay cả khi một resource dispose lỗi.

Nếu finalization operation và cleanup cùng lỗi, implementation phải giữ được cả primary và cleanup failures; cách biểu đạt cụ thể được khóa bằng test trước khi public API stable.

## 10. Async policy

### 10.1. Không có async Unit of Work lifecycle ở v1

Không có:

- `BeginAsync`;
- `CompleteAsync`;
- `RollbackAsync`;
- `DisposeAsync`;
- lifecycle cancellation token.

### 10.2. Async command vẫn được phép

Caller có thể viết:

```csharp
using (var scope = manager.Begin())
{
    using (var command = scope.Db.CreateCommand())
    {
        command.CommandText = "...";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    scope.Complete();
}
```

Nếu provider override async ADO.NET bằng I/O thật, caller nhận true async ở command layer. Nếu provider dùng implementation mặc định đồng bộ, đó là limitation của provider/BCL contract chứ core không che giấu.

## 11. Concurrency contract

v1 **không thread-safe** và không hỗ trợ parallel database operations trên cùng Unit of Work.

Không đưa operation lease, reader wrapper hoặc semaphore serialization vào core v1.

Lý do:

- muốn guard đầy đủ phải intercept mọi command/reader path;
- Dapper/RepoDb/raw provider access có thể bypass wrapper;
- guard nửa vời tạo false sense of safety;
- ADO.NET connection/transaction bản thân không được thiết kế cho parallel use.

Documentation phải có anti-pattern rõ ràng:

```csharp
await Task.WhenAll(
    repositoryA.SaveAsync(),
    repositoryB.SaveAsync()); // unsupported on one UoW
```

Nếu workload cần parallel I/O, dùng Unit of Work/connection riêng cho từng operation.

## 12. Repository and ORM policy

Core không có:

- repository factory;
- repository cache;
- DI integration bắt buộc;
- Dapper dependency;
- RepoDb dependency.

Repository nhận `IUnitOfWorkManager` hoặc `IUnitOfWorkContext` theo application architecture.

Dapper/RepoDb integration được chứng minh bằng tests/samples, không bằng việc nhúng ORM vào core.

## 13. Options policy

v1 chỉ giữ option có generic ADO.NET semantics rõ ràng:

- `IsolationLevel?`.

Không đưa vào v1 core:

- `TransactionTimeout`;
- `ReadOnly`;
- provider-specific transaction flags;
- automatic command timeout policy.

Những option trên chỉ được thêm khi có implementation contract rõ và test provider tương ứng.

## 14. Features intentionally removed from the modern design

Các thành phần sau không được port vào v1:

- async transaction lifecycle;
- `IAsyncDisposable` compatibility package;
- `WaitAsyncCompatible`;
- initialization cancellation choreography;
- transaction lifecycle strategy abstraction chỉ để phân nhánh modern/legacy;
- reflection để tìm provider-specific async lifecycle methods;
- `TransactionBoundDbConnection` full facade;
- `TransactionBoundDbCommand` wrapper;
- `TransactionBoundDbDataReader` wrapper;
- `TransactionBoundDbTransaction` wrapper;
- operation lease / reader lifetime concurrency guard;
- repository factory/cache;
- `ReadOnly` và `TransactionTimeout` nếu core không thực thi được;
- public `ClearCurrent()`.

## 15. Better features added for the legacy-first product

### 15.1. Borrowed database session

Connection và transaction được nhóm thành một object có ownership contract rõ ràng.

### 15.2. Safe command creation

`CreateCommand()` tự bind transaction mà không đổi concrete provider command type.

### 15.3. Public rollback-only visibility

`IsRollbackRequested` cho phép outer layer biết inner scope đã doom transaction và dừng xử lý sớm nếu muốn.

### 15.4. Real `net472` consumer verification

Release contract yêu cầu test chạy trên .NET Framework 4.7.2, không chỉ compile `netstandard2.0`.

### 15.5. Multi-manager isolation test

Hai manager instance phải không chia sẻ ambient root.

### 15.6. Provider-native async command compatibility

Tests phải chứng minh `CreateCommand()` trả provider command gốc và không phá async command API.

### 15.7. Runtime dependency budget

Core v1 đặt mục tiêu zero runtime NuGet dependencies ngoài platform assemblies của `netstandard2.0`.

## 16. Non-goals for v1

- savepoints;
- `RequiresNew`;
- ambient suppression;
- distributed transactions / `System.Transactions` orchestration;
- automatic retry;
- transaction timeout abstraction;
- read-only transaction abstraction;
- ORM repository cache;
- runtime concurrency serialization;
- diagnostics framework/plugin model;
- provider-specific true-async transaction lifecycle;
- multi-target `net8.0` optimized assembly.

Các mục này chỉ được thêm khi có use case thực tế và benchmark/test chứng minh giá trị.

## 17. Package quality gates

Trước stable v1:

1. build `netstandard2.0` với warnings as errors;
2. chạy test consumer `net472` trên Windows;
3. chạy test consumer modern .NET để chứng minh package chọn asset đúng;
4. test nested complete/rollback/abandon;
5. test begin/commit/rollback failure và cleanup;
6. test two manager instances trong cùng async flow;
7. test `CreateCommand()` tự bind transaction;
8. test Dapper integration mà core không cần reference Dapper;
9. test SQL Server provider thực sự dùng trong production;
10. package validation và API baseline trước stable release;
11. NuGet metadata, license, Source Link, symbol package;
12. prerelease trước stable.

## 18. References

- Source comparison: https://github.com/chung6a8m/UnitOfWorkManager
- .NET Standard guidance: https://learn.microsoft.com/dotnet/standard/net-standard
- `DbConnection.BeginTransactionAsync`: https://learn.microsoft.com/dotnet/api/system.data.common.dbconnection.begintransactionasync
- `DbConnection.OpenAsync`: https://learn.microsoft.com/dotnet/api/system.data.common.dbconnection.openasync
- `DbCommand.ExecuteNonQueryAsync`: https://learn.microsoft.com/dotnet/api/system.data.common.dbcommand.executenonqueryasync
