# NetUnitOfWorkManager.Sample.RepoDb.Net472

Console sample chạy trên **.NET Framework 4.7.2** và mô phỏng luồng transaction của sample RepoDb Minimal API trong `UnitOfWorkManager`, nhưng dùng API hiện tại của `NetUnitOfWorkManager`.

## Thành phần

- `net472` Console App.
- `Microsoft.Extensions.DependencyInjection` `8.0.1` cho DI.
- `RepoDb` `1.15.1`.
- `RepoDb.SqlServer` `1.14.0`.
- `Microsoft.Data.SqlClient` được kéo vào qua `RepoDb.SqlServer`.
- `ProjectReference` tới `src/NetUnitOfWorkManager`.
- SQL Server connection string lấy từ biến môi trường `NETUOW_SQLSERVER_CONNECTION_STRING`.

RepoDb nhận trực tiếp `UnitOfWorkDbSession.Connection` và `UnitOfWorkDbSession.Transaction`, vì vậy các câu lệnh repository chạy trong đúng transaction do `NetUnitOfWorkManager` quản lý.

## Chuẩn bị SQL Server

PowerShell:

```powershell
$env:NETUOW_SQLSERVER_CONNECTION_STRING = "Server=localhost;Database=NetUnitOfWorkManager;Integrated Security=True;TrustServerCertificate=True"
```

Connection string phải trỏ tới database đã tồn tại và tài khoản cần quyền tạo bảng, đọc và ghi dữ liệu.

Sample tự tạo bảng sau nếu chưa tồn tại:

```text
[dbo].[NetUnitOfWorkCounter]
```

Để kết quả mỗi lần chạy dễ kiểm chứng, sample xóa dữ liệu trong **chính bảng sample này** trước khi chạy các scenario.

## Chạy sample

Từ thư mục gốc repository:

```powershell
dotnet restore .\samples\NetUnitOfWorkManager.Sample.RepoDb.Net472\NetUnitOfWorkManager.Sample.RepoDb.Net472.csproj
dotnet run --project .\samples\NetUnitOfWorkManager.Sample.RepoDb.Net472\NetUnitOfWorkManager.Sample.RepoDb.Net472.csproj -c Release
```

Chạy trên Windows có .NET Framework 4.7.2+.

## Scenario 1 - nested commit

1. Outer Unit of Work insert `10`.
2. Nested service mở Unit of Work mới, insert `20`, rồi `Complete()`.
3. Nested scope dùng cùng physical connection/transaction với outer scope.
4. Outer scope `Complete()`.
5. Hai dòng `10`, `20` được commit.

## Scenario 2 - nested rollback-only

1. Outer Unit of Work insert `30`.
2. Nested service mở Unit of Work mới, insert `40`, nhưng thoát scope mà không `Complete()`.
3. Nested scope bị xem là abandoned và đánh dấu root Unit of Work rollback-only.
4. Outer scope vẫn gọi `Complete()`.
5. Physical transaction rollback, nên `30` và `40` đều không được lưu.

Kết quả cuối cùng vẫn chỉ còn hai giá trị từ scenario commit: `10`, `20`.
