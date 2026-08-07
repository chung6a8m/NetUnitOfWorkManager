# NetUnitOfWorkManager documentation

## Mục tiêu

`NetUnitOfWorkManager` là một Unit of Work manager được thiết kế **native cho .NET Framework 4.7.2+** và phát hành dưới dạng thư viện **target `netstandard2.0`**.

Dự án này không phải là bản port giảm cấp của `chung6a8m/UnitOfWorkManager`. Kiến trúc được chọn lại từ đầu theo khả năng thật của .NET Standard 2.0, nhằm tránh API trông bất đồng bộ nhưng thực tế phải fallback đồng bộ, tránh compatibility shim không cần thiết và giảm số lớp wrapper/state machine phải duy trì.

## Baseline v1 — APPROVED 2026-08-07

Baseline sau đã được chốt chính thức và là cơ sở triển khai cho `docs/plans/20260807-001-netunitofworkmanager-v1.md`:

1. Package chỉ target `netstandard2.0` ở v1.
2. Unit of Work lifecycle là synchronous: `Begin()`, `Complete()`, `Rollback()`, `Dispose()`.
3. Code bên trong Unit of Work vẫn có thể dùng `DbCommand.Execute*Async`, Dapper async hoặc API async của provider khi provider thật sự hỗ trợ.
4. Nested scope dùng chung một root transaction qua `AsyncLocal`, nhưng mỗi `Begin()` trả một scope token riêng.
5. Inner rollback hoặc scope bị abandon làm root transaction bị đánh dấu rollback-only.
6. Core không chứa repository factory/cache.
7. Core không cố wrap toàn bộ `DbConnection`/`DbCommand`/`DbDataReader` để giả lập một ADO.NET surface khác.
8. `CreateCommand()` là safe path cho ADO.NET thuần: command được tự gắn transaction hiện tại.
9. `DbConnection` và `DbTransaction` chỉ được expose như borrowed interop objects cho Dapper/RepoDb/provider code; Unit of Work vẫn sở hữu lifecycle.
10. Không hứa thread-safe hoặc parallel-use trên cùng connection/transaction.

Các lựa chọn đã chốt:

```text
D1=1, D2=1, D3=1, D4=1, D5=1, D6=1
```

Các work package P01–P10 không cần hỏi lại những quyết định này, trừ khi có yêu cầu explicit thay đổi baseline.

## Provider-native database session

Trong một scope đang active, `scope.Db` expose trực tiếp provider-native `DbConnection` và `DbTransaction`. Với ADO.NET thuần, ưu tiên `scope.Db.CreateCommand()` vì command được tạo bởi chính provider connection và tự động bind với transaction hiện tại.

Ví dụ:

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

`scope.Db.Connection` và `scope.Db.Transaction` là **borrowed objects**. Caller không sở hữu lifecycle của hai object này:

- không `Close()` hoặc `Dispose()` connection;
- không `Commit()`, `Rollback()` hoặc `Dispose()` transaction;
- không bắt đầu competing transaction trên cùng connection;
- không đổi database hoặc connection string khi Unit of Work đang active.

Command do `CreateCommand()` trả về vẫn là provider-native command và caller sở hữu lifecycle của command đó, vì vậy nên dispose command theo cách thông thường.

Sau khi scope đã settled hoặc root Unit of Work đã finalized, database session không còn hợp lệ và access sẽ fail-fast bằng `UnitOfWorkStateException`.

## Bộ tài liệu

Public/release documentation:

- [Public package README](../README.md)
- [Usage guide](usage.md)
- [Compatibility and release contract](compatibility.md)
- [Anti-patterns](anti-patterns.md)
- [Changelog](../CHANGELOG.md)

Architecture and planning documentation:

- [Thiết kế và kiến trúc](netunitofworkmanager-design.md)
- [Ma trận giữ / lược bỏ / bổ sung tính năng](feature-scope.md)
- [Các quyết định đã chốt](decisions.md)
- [Implementation plan v1](plans/20260807-001-netunitofworkmanager-v1.md)

## Nguồn phân tích

Thiết kế dựa trên:

- báo cáo đính kèm `Kết luận việc bổ sung target netstandard2.0 cho UnitOfWork.Core.md`;
- trạng thái hiện tại của `chung6a8m/UnitOfWorkManager`;
- các giới hạn API ADO.NET của .NET Standard 2.0;
- yêu cầu sản phẩm: .NET Framework 4.7.2+ là runtime legacy tối thiểu.

## Nguyên tắc ra quyết định

Khi một tính năng chỉ tồn tại để bù cho API hiện đại không có trên `netstandard2.0`, mặc định **không mang tính năng đó sang**.

Khi một invariant có thể đạt được bằng API nhỏ và rõ hơn thay vì wrapper/state machine lớn, ưu tiên API nhỏ.

Khi một capability phụ thuộc provider, core chỉ công bố capability ở mức mà BCL bảo đảm; capability cao hơn được kiểm chứng bằng integration test hoặc adapter riêng, không suy đoán bằng reflection.
