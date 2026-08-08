# Feature scope: keep, simplify, remove, add

## 1. Mục tiêu của ma trận

Tài liệu này so sánh các capability chính của `chung6a8m/UnitOfWorkManager` với baseline đề xuất cho `NetUnitOfWorkManager`.

Nguyên tắc: không đánh giá feature theo tiêu chí “càng nhiều càng tốt”, mà theo ba câu hỏi:

1. Feature có bảo đảm được semantics thật trên `netstandard2.0` không?
2. Feature có giúp transaction correctness đủ lớn so với complexity nó tạo ra không?
3. Feature có thuộc trách nhiệm Unit of Work core hay nên nằm ở application/provider/ORM layer?

## 2. Feature matrix

| Feature | Quyết định cho v1 | Lý do |
| --- | --- | --- |
| Target `netstandard2.0` | **Keep / core requirement** | Là target phù hợp để hỗ trợ .NET Framework 4.7.2+ và modern .NET bằng một asset. |
| Multi-target `net8.0;netstandard2.0` | **Remove** | Tạo hai implementation path và kéo modern API pressure vào legacy-first package. |
| Ambient `AsyncLocal` | **Keep** | Phù hợp async logical flow và có trên runtime mục tiêu. |
| Ambient suppression qua `IDisposable Suppress()` | **Add / v1 contract** | Cho phép tạm ẩn ambient root mà không đụng transaction lifecycle; `Suppress() + Begin()` tạo independent root rõ ràng bằng connection mới. |
| Public `IUnitOfWorkSuppressionScope` | **Remove / unnecessary** | Suppression token chỉ quản lý ambient frame, không sở hữu database resource; `IDisposable` là đủ cho v1. |
| Ambient state static cho mọi manager | **Remove** | Multi-database/multi-manager dễ cross-talk. |
| Ambient state theo manager instance | **Keep** | Isolation đơn giản, không cần global dictionary. |
| Immutable ambient frame stack | **Add** | Push/pop bằng frame mới tránh child execution context mutate shared ambient holder của parent logical flow. |
| Root + scope tách riêng | **Keep** | Giải quyết ownership của nested scopes với complexity hợp lý. |
| Mỗi nested `Begin` trả cùng root object | **Remove** | Inner dispose có thể phá root ownership. |
| Ref-count nested scopes | **Keep** | Đơn giản và đúng cho rollback-all semantics. |
| Inner rollback doom outer transaction | **Keep** | Hợp với atomic business operation; dễ dự đoán. |
| Savepoints | **Remove from v1** | Không cần cho rollback-all baseline; provider differences cao. |
| `RequiresNew` | **Remove from v1** | Không expose transaction nesting mode; independent root chỉ được tạo explicit bằng `Suppress() + Begin()`. |
| Synchronous `Begin/Complete/Rollback/Dispose` | **Add / preferred** | Phù hợp khả năng transaction lifecycle thật của `netstandard2.0`. |
| `BeginAsync` | **Remove from v1** | Begin transaction generic async không thuộc .NET Standard 2.0. |
| `CompleteAsync` / async commit | **Remove from v1** | Không muốn sync fallback dưới tên async. |
| `RollbackAsync` | **Remove from v1** | Không muốn sync fallback dưới tên async. |
| `IAsyncDisposable` | **Remove from v1** | Thêm dependency nhưng resource lifecycle bên dưới vẫn sync. |
| Lifecycle `CancellationToken` | **Remove from v1** | Commit/rollback/begin sync không thể honor cancellation đáng tin cậy. |
| `Task.WaitAsync` compatibility helper | **Remove** | Không còn async initialization nên không cần. |
| Shared initialization `TaskCompletionSource` | **Remove** | Initialization sync trước khi publish ambient root. |
| Reserved/canceled-before-activation scope states | **Remove** | Chỉ cần khi `BeginAsync` có thể bị cancel trong khi initialization đang chạy. |
| Initialization `CancellationTokenSource` choreography | **Remove** | Complexity không còn cần thiết. |
| Full lifecycle state machine | **Simplify** | Giữ `Active/Finalizing/Disposed/Faulted`; bỏ states phục vụ async initialization. |
| `UnitOfWorkStateException` | **Keep** | Một exception misuse nhỏ, rõ ràng là đủ; dùng cả cho suppression stack misuse. |
| `UnitOfWorkConcurrencyException` | **Remove from v1** | Không có runtime concurrency guard toàn diện thì exception riêng dễ tạo false confidence. |
| Operation lease | **Remove** | Muốn enforce đầy đủ phải intercept mọi provider/ORM path. |
| Reader lifetime wrapper | **Remove** | Tránh wrapping toàn bộ `DbDataReader` và API compatibility burden. |
| Transaction-bound `DbConnection` facade | **Remove from v1** | Có thể phá concrete provider expectations và kéo nhiều override không có trên netstandard2.0. |
| Transaction-bound `DbCommand` wrapper | **Remove** | `CreateCommand()` provider-native + auto-bind transaction đơn giản hơn. |
| Transaction-bound `DbDataReader` wrapper | **Remove** | Không cần khi bỏ operation lease. |
| Transaction-bound `DbTransaction` wrapper | **Remove** | Borrowed transaction object đủ cho interop. |
| `CreateCommand()` auto-bind transaction | **Add** | Safe ADO.NET path, giữ provider-native command type, tránh quên transaction. |
| Borrowed `DbConnection` + `DbTransaction` pair | **Add** | Cho Dapper/RepoDb/provider interop mà không ép wrapper type. |
| Public `IsRollbackRequested` | **Add** | Outer layer biết transaction đã rollback-only và có thể fail fast về nghiệp vụ. |
| Public raw `ClearCurrent()` | **Remove** | Có thể orphan transaction; ambient cleanup phải gắn với root finalization hoặc suppression token hợp lệ. |
| Repository factory trong core | **Remove** | Không phải trách nhiệm transaction manager. |
| Repository cache trong core | **Remove** | Tạo concurrency/lifetime policy không cần thiết; DI/application quản lý tốt hơn. |
| `CommandTimeoutSeconds` trong UoW options | **Remove from v1** | Là command policy, không phải transaction invariant; raw ORM path cũng có thể bypass. |
| `TransactionTimeout` generic option | **Remove from v1** | ADO.NET generic contract không thực thi thống nhất. |
| `ReadOnly` generic option | **Remove from v1** | Provider-neutral semantics không đáng tin cậy nếu chỉ là flag. |
| `IsolationLevel` | **Keep** | Generic ADO.NET transaction option rõ ràng và có thể test; independent root trong suppression có thể chọn isolation khác outer root. |
| Public transaction factory abstraction | **Remove from v1** | Không cần nếu core chỉ gọi `BeginTransaction`; thêm khi có provider use case thật. |
| Reflection để tìm provider async lifecycle | **Reject** | Version-sensitive, khó đoán exception/cancellation semantics. |
| Provider-specific async lifecycle trong core | **Reject for v1** | Làm core mất tính nhỏ gọn và tạo support matrix lớn. |
| Async command/query | **Keep as provider capability** | `DbCommand` async APIs tồn tại; core không bọc nên provider có thể override thật. |
| Dapper dependency | **Remove from core** | Chỉ integration tests/samples reference Dapper. |
| RepoDb dependency | **Remove from core** | Chỉ integration tests/samples reference RepoDb. |
| Explicit rollback khi dispose incomplete scope | **Keep** | Deterministic, không phụ thuộc provider implicit rollback-on-dispose. |
| Cleanup cả transaction lẫn connection | **Keep** | Correct resource ownership. |
| Retry commit tự động | **Reject** | Commit failure có thể có unknown outcome; retry mù nguy hiểm. |
| Real `net472` consumer tests | **Add / release blocker** | Chứng minh runtime support thật. |
| Modern .NET consumer smoke tests | **Add** | Chứng minh package vẫn dùng được trên modern .NET. |
| SQL Server provider integration | **Add / release blocker** | Runtime mục tiêu cần provider thực tế, không chỉ mock. |
| Package validation/API baseline | **Add** | Bảo vệ public contract khi package bắt đầu phát hành. |
| Zero runtime NuGet dependency budget | **Add** | Giảm compatibility risk cho .NET Framework applications. |

## 3. Những phần được xem là “over thinking” nếu mang sang legacy package

### 3.1. Async lifecycle compatibility layer

Nếu phải viết `WaitAsyncCompatible`, `IAsyncDisposable` shim, conditional overrides và fallback sync chỉ để giữ cùng public surface, package đã ưu tiên hình dạng API hơn semantics.

**Quyết định:** bỏ surface đó.

### 3.2. Full ADO.NET wrapper tree

Wrapping connection -> command -> transaction -> reader nhằm cưỡng chế ownership/concurrency có giá trị trên paper, nhưng tạo chi phí lớn:

- phải mirror rất nhiều virtual members;
- target framework khác nhau có base members khác nhau;
- ORM/provider có thể kiểm tra concrete type;
- wrapper phải giữ đúng async/cancellation/reader semantics;
- mỗi API mới của BCL lại tăng maintenance cost.

**Quyết định:** safe helper + ownership contract thay vì full wrapper.

### 3.3. Runtime concurrency enforcement không toàn diện

Nếu raw/provider path vẫn tồn tại thì guard command wrapper không thể chứng minh Unit of Work thread-safe.

**Quyết định:** contract “sequential use only” rõ ràng hơn một guard partial.

### 3.4. Repository lifecycle nằm trong transaction core

Repository factory/cache làm core chịu trách nhiệm thêm DI, object lifetime, thread safety và type resolution.

**Quyết định:** bỏ khỏi core.

### 3.5. Generic options không có generic enforcement

`ReadOnly` hoặc `TransactionTimeout` chỉ hữu ích khi provider path thực sự áp dụng được.

**Quyết định:** không expose flag chỉ để “đủ tính năng”.

### 3.6. Biến suppression thành transaction mode hierarchy

Ambient suppression chỉ cần giải quyết một việc: tạm thời làm cho manager không thấy outer root và restore đúng frame sau đó. Nếu biến nó thành `RequiresNew`, transaction strategy hierarchy hoặc public suppression scope abstraction thì core lại phải mang thêm policy và ownership semantics không cần thiết.

**Quyết định:** `IDisposable Suppress()` chỉ quản lý ambient visibility. Independent transaction chỉ xuất hiện khi application explicit gọi `Begin()` trong suppression region.

## 4. Những invariant bắt buộc không được lược bỏ

Dù tối giản, v1 vẫn phải giữ các invariant sau:

1. Root duy nhất sở hữu connection/transaction.
2. Nested scopes ngoài suppression dùng cùng physical transaction.
3. Mỗi `Begin()` trả scope riêng.
4. Inner scope không thể dispose root resource.
5. Một rollback/abandon làm root rollback-only.
6. Root chỉ finalize một lần.
7. Ambient state luôn quay về frame cha khi root kết thúc, kể cả finalization lỗi.
8. Hai manager instance không share ambient root hoặc suppression state.
9. `Suppress()` không thực hiện database lifecycle call và làm ambient root hiện tại không visible.
10. Dispose suppression token hợp lệ restore chính xác outer ambient frame; nested suppression restore LIFO.
11. Dispose suppression sai thứ tự hoặc khi independent root còn active phải throw mà không mutate ambient state.
12. `Suppress() + Begin()` tạo independent physical connection/transaction và có thể dùng isolation level khác outer root.
13. Finalize independent root trong suppression phải quay lại suppressed state, không tự restore outer root.
14. Suppression phải flow qua `await` theo `AsyncLocal`, nhưng database use vẫn sequential-only.
15. `CreateCommand()` luôn bind transaction hiện tại.
16. Incomplete final scope phải explicit rollback.
17. Commit failure không tự retry.
18. Connection và transaction đều được cleanup best-effort.

## 5. Better-than-port success criteria

`NetUnitOfWorkManager` chỉ được xem là tốt hơn một bản port netstandard2.0 khi đạt đồng thời:

- public API nhỏ hơn;
- không có method async giả;
- ít state hơn;
- ít wrapper hơn;
- không cần reflection/provider detection trong core;
- không có runtime ORM dependency;
- nested ownership vẫn đúng;
- ambient suppression stack-safe, không sở hữu transaction lifecycle;
- independent root chỉ được tạo explicit bằng `Suppress() + Begin()`;
- transaction binding có safe path rõ ràng;
- chạy test thật trên `net472`;
- suppression semantics được kiểm chứng trên cả modern .NET và legacy compatibility path;
- tài liệu nói rõ unsupported parallel use;
- SQL Server integration test xác nhận transaction semantics.
