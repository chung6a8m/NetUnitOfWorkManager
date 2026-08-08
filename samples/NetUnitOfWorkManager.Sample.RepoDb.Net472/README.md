# NetUnitOfWorkManager.Sample.RepoDb.Net472

Console reference sample chạy trên **.NET Framework 4.7.2** và minh họa RepoDb dùng public Unit of Work contract, nested scopes và Ambient suppression.

## Thành phần

- `net472` Console App.
- `Microsoft.Extensions.DependencyInjection` `8.0.1` cho DI.
- `RepoDb` `1.15.1`.
- `RepoDb.SqlServer` `1.14.0`.
- `Microsoft.Data.SqlClient` được kéo vào qua `RepoDb.SqlServer`.
- `ProjectReference` tới `src/NetUnitOfWorkManager`.
- SQL Server connection string lấy từ biến môi trường `NETUOW_SQLSERVER_CONNECTION_STRING`.

Project reference của sample:

```xml
<ProjectReference Include="../../src/NetUnitOfWorkManager/NetUnitOfWorkManager.csproj" />
```

RepoDb nhận trực tiếp `UnitOfWorkDbSession.Connection` và `UnitOfWorkDbSession.Transaction`, vì vậy các câu lệnh repository chạy trong đúng transaction do `NetUnitOfWorkManager` quản lý. Repository không cache, commit, rollback hoặc dispose borrowed connection/transaction.

## RepoDb attribute mapping

`CounterItem` dùng attribute mapping của RepoDb để mô tả trực tiếp schema của entity:

```csharp
[Map("[dbo].[NetUnitOfWorkCounter]")]
public sealed class CounterItem
{
    [Primary]
    [Identity]
    public long Id { get; set; }

    public int Value { get; set; }
}
```

Repository dùng các entity operation của RepoDb thay cho raw SQL:

- `Insert<CounterItem, long>()` lấy tên bảng từ `[Map]` và nhận biết cột identity qua `[Identity]`.
- `QueryAll<CounterItem>()` lấy tên bảng từ `[Map]` và sắp xếp theo `Id`.
- Cả hai operation đều nhận `transaction: db.Transaction`, nên RepoDb tham gia đúng physical transaction do Unit of Work quản lý.

## Chuẩn bị SQL Server

```powershell
$env:NETUOW_SQLSERVER_CONNECTION_STRING = "Server=localhost;Database=NetUnitOfWorkManager;Integrated Security=True;TrustServerCertificate=True"
```

Connection string phải trỏ tới database đã tồn tại và tài khoản cần quyền tạo bảng, đọc và ghi dữ liệu. Sample tự tạo `[dbo].[NetUnitOfWorkCounter]` nếu chưa tồn tại.

## Chạy sample

```powershell
dotnet restore .\samples\NetUnitOfWorkManager.Sample.RepoDb.Net472\NetUnitOfWorkManager.Sample.RepoDb.Net472.csproj
dotnet run --project .\samples\NetUnitOfWorkManager.Sample.RepoDb.Net472\NetUnitOfWorkManager.Sample.RepoDb.Net472.csproj -c Release
```

Chạy trên Windows có .NET Framework 4.7.2+.

## Scenarios

### 1. Nested commit

Outer service insert `10`; nested service mở nested scope, insert `20`, rồi `Complete()`. Hai scope dùng cùng physical connection/transaction và outer `Complete()` commit cả hai dòng.

### 2. Nested rollback-only

Outer service insert `30`; nested scope insert `40` nhưng thoát mà không `Complete()`. Root bị đánh dấu rollback-only nên outer `Complete()` không thể commit cặp thứ hai.

### 3. Ambient suppression + independent root

Sample reset bảng rồi chạy:

```text
outer root (Serializable) starts
  -> Suppress(): no ambient Unit of Work
      -> Begin(ReadCommitted): independent root inserts 60 and commits
  -> exact outer root is restored
outer root inserts 50 and rolls back
```

Outer write được thực hiện sau independent commit để sample không tạo lock contention giả tạo giữa hai physical transactions trên cùng bảng. Runner xác nhận chỉ còn giá trị `60`. Điều này chứng minh independent inner commit survives outer rollback, đồng thời connection và transaction của independent root khác outer root.

Ba trạng thái cần phân biệt:

```text
Begin()                    -> root hoặc nested scope hiện tại
Suppress()                 -> no ambient Unit of Work
Suppress() + Begin()       -> independent root transaction
```

Không dùng suppression cho outbox/event row hoặc dữ liệu khác cần atomic commit cùng outer business transaction.

Mọi invariant failure đều throw để process exit non-zero.
