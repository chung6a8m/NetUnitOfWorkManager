# NetUnitOfWorkManager .NET Framework 4.7.2 provider-native sample

Project này là executable consumer target `net472` dùng để chứng minh NetUnitOfWorkManager chạy được trong một .NET Framework consumer thực tế và giữ đúng provider-native transaction semantics.

Đây là ADO.NET provider probe in-process, **không cần SQL Server/LocalDB** hay ORM package. Probe trả provider-native `DbConnection`, `DbTransaction` và `DbCommand`, nên kiểm tra trực tiếp lifecycle, nested scopes và Ambient suppression mà không phụ thuộc database infrastructure.

Project reference:

```xml
<ProjectReference Include="../../src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj" />
```

## Runtime scenarios

`Program.cs` fail process ngay khi một invariant không đúng và chỉ exit code `0` khi tất cả scenario sau pass:

- single scope commit;
- explicit rollback;
- nested complete, trong đó inner completion chưa commit physical transaction;
- inner rollback làm root rollback-only và outer completion kết thúc bằng rollback;
- provider `ExecuteNonQueryAsync` chạy bên trong synchronous Unit of Work scope;
- `Suppress()` ẩn outer root và `Current` throw khi ambient bị ẩn;
- nested suppression restore theo LIFO;
- `Suppress() + Begin()` tạo independent fake connection/transaction và cho phép isolation level khác outer root;
- independent root finalize quay lại suppressed state, sau đó dispose suppression restore đúng outer context;
- suppression flow qua `await`/async command continuation bằng `AsyncLocal` semantics, không dùng `Task.Delay`.

Ba trạng thái reference:

```text
Begin()                    -> root hoặc nested scope hiện tại
Suppress()                 -> no ambient Unit of Work
Suppress() + Begin()       -> independent root transaction
```

`Suppress()` không commit, rollback hoặc dispose outer transaction.

## Verification

Chạy trên Windows development machine có .NET Framework 4.7.2 targeting pack phù hợp:

```powershell
pwsh -File .\scripts\verify-net472.ps1
```

P12 verifier restore solution, build core và cả ba reference samples, launch provider-native probe, rồi chạy full test suite target `net472`.

## Provider-native session usage

Trong application code, database access đi qua session provider-native:

```csharp
using (IUnitOfWorkScope scope = manager.Begin())
{
    using (DbCommand command = scope.Db.CreateCommand())
    {
        command.CommandText = "...";
        command.ExecuteNonQuery();
    }

    scope.Complete();
}
```

`CreateCommand()` tạo chính command type của ADO.NET provider và tự bind `scope.Db.Transaction`.

## Borrowed ownership rules

`scope.Db.Connection` và `scope.Db.Transaction` chỉ được mượn để interop với ADO.NET/Dapper/RepoDb/provider code. Trong Unit of Work đang active:

- không close hoặc dispose connection;
- không commit, rollback hoặc dispose transaction;
- không bắt đầu competing transaction trên cùng connection;
- không đổi database hoặc connection string.

Caller vẫn dispose các `DbCommand` mà mình tạo. Sau khi scope settled hoặc root finalized, không tiếp tục sử dụng `scope.Db` hay session đã cache trước đó.
