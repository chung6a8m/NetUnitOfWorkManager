# NetUnitOfWorkManager .NET Framework 4.7.2 sample

Project này là executable consumer target `net472` dùng để chứng minh NetUnitOfWorkManager không chỉ compile `netstandard2.0` mà còn chạy được trong một .NET Framework consumer thực tế.

P07 dùng một ADO.NET provider probe in-process, không cần SQL Server/LocalDB hay package runtime bên ngoài. Probe vẫn trả provider-native `DbConnection`, `DbTransaction` và `DbCommand`, vì vậy nó kiểm tra trực tiếp transaction lifecycle và transaction binding của `UnitOfWorkDbSession.CreateCommand()` mà không làm CI phụ thuộc hạ tầng database.

## Runtime scenarios

`Program.cs` fail process ngay khi một invariant không đúng và chỉ exit code `0` khi tất cả scenario sau pass:

- single scope commit;
- explicit rollback;
- nested complete, trong đó inner completion chưa commit physical transaction;
- inner rollback làm root rollback-only và outer completion kết thúc bằng rollback;
- provider `ExecuteNonQueryAsync` chạy bên trong synchronous Unit of Work scope, với command vẫn bound vào root transaction.

Sample cũng in target framework và CLR version của process để log CI cho thấy executable `net472` đã thực sự được launch.

## Verification

Chạy trên Windows development machine có .NET Framework 4.7.2 targeting pack phù hợp:

```powershell
pwsh -File .\scripts\verify-net472.ps1
```

Script không có nhánh skip `net472`. Nó thực hiện theo thứ tự:

1. restore solution;
2. build core `netstandard2.0` ở `Release`;
3. build sample `net472`;
4. launch trực tiếp file `.exe` của sample để chạy runtime scenarios;
5. chạy full test suite với target `net472`.

CI Windows gọi chính script này, vì vậy lỗi build hoặc runtime của consumer `net472` làm job fail thay vì silently degrade sang modern .NET-only verification.

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

Caller vẫn nên dispose các `DbCommand` mà mình tạo. Sau khi scope settled hoặc root finalized, không tiếp tục sử dụng `scope.Db` hay session đã cache trước đó.
