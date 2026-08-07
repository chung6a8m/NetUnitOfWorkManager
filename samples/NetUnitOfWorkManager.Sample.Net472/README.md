# NetUnitOfWorkManager .NET Framework 4.7.2 sample

Project này là consumer shell cho runtime floor `.NET Framework 4.7.2`. Runtime scenarios đầy đủ sẽ được bổ sung ở P07; từ P05, database access contract dùng provider-native session như sau:

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
