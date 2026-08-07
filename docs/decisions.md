# Numbered design decisions

Tài liệu này gom các điểm mà có nhiều phương án hợp lý. Baseline hiện tại đã chọn sẵn phương án khuyến nghị để implementation có thể bắt đầu mà không bị block.

Nếu muốn đổi, chỉ cần trả lời theo dạng:

```text
D2=3, D4=2
```

Nếu không đổi gì, baseline mặc định là:

```text
D1=1, D2=1, D3=1, D4=1, D5=1, D6=1
```

## D1. Unit of Work lifecycle API

### 1. Synchronous lifecycle only — **Recommended**

```text
Begin / Complete / Rollback / Dispose
```

- truthful với `netstandard2.0`;
- không fake async transaction lifecycle;
- không cần `IAsyncDisposable` compatibility package;
- không cần cancellation choreography cho begin/commit/rollback.

Database command bên trong scope vẫn được phép async.

### 2. Hybrid `BeginAsync`, sync complete/rollback

- tận dụng `DbConnection.OpenAsync`;
- transaction begin vẫn sync;
- public API khó giải thích hơn;
- cancellation chỉ có ý nghĩa ở phần open, không phải toàn lifecycle.

### 3. Core sync + optional provider-specific async package

- core giữ phương án 1;
- package riêng có thể cung cấp true-async lifecycle cho provider cụ thể;
- chỉ nên làm sau khi có use case production rõ.

**Baseline:** 1.

---

## D2. Database access surface

### 1. Borrowed session + safe `CreateCommand()` — **Recommended**

Expose:

```text
Db.Connection
Db.Transaction
Db.CreateCommand()
```

- `CreateCommand()` auto-bind transaction;
- Dapper/RepoDb vẫn truy cập provider-native objects;
- không full wrapper tree;
- ownership của raw objects là contract/documentation, không runtime-enforced tuyệt đối.

### 2. Full transaction-bound `DbConnection` facade

- mạnh hơn về ownership enforcement;
- Dapper có thể dùng thuận tiện;
- RepoDb/provider code có thể phụ thuộc concrete connection type;
- phải mirror nhiều ADO.NET members;
- compatibility tax lớn trên `netstandard2.0`.

### 3. Commands-only API, không expose connection/transaction

- invariant mạnh nhất;
- ADO.NET thuần đẹp;
- Dapper/RepoDb phải có adapter package hoặc API riêng;
- tăng số package và integration surface.

**Baseline:** 1.

---

## D3. Repository management

### 1. Không có repository factory/cache trong core — **Recommended**

- application/DI quản lý repository;
- core chỉ quản lý transaction boundary;
- giảm type resolution/lifetime/concurrency policy.

### 2. Có optional repository factory trong core

- tiện cho service locator style;
- tăng responsibility của core;
- khó tối ưu lifetime đúng cho mọi application.

### 3. Tách package `NetUnitOfWorkManager.Repositories`

- giữ core nhỏ;
- vẫn có convenience layer cho app cần;
- chỉ làm nếu có nhiều consumer yêu cầu.

**Baseline:** 1.

---

## D4. Concurrency policy

### 1. Sequential-use contract, không runtime guard — **Recommended**

- ghi rõ một UoW không thread-safe;
- không hỗ trợ parallel DB operations trên cùng connection/transaction;
- không wrapper reader/command;
- không false sense of safety.

### 2. Runtime fail-fast guard

- phát hiện overlap sớm;
- muốn đầy đủ phải intercept cả ORM/provider path;
- kéo operation lease và reader wrapper trở lại.

### 3. Serialize operations bằng semaphore

- tránh exception do overlap;
- che lỗi kiến trúc của caller;
- có thể tạo deadlock/latency khó đoán;
- không nên là behavior mặc định của transaction manager.

**Baseline:** 1.

---

## D5. v1 transaction options

### 1. Chỉ `IsolationLevel` — **Recommended**

- ADO.NET generic contract rõ;
- dễ test trên nhiều provider;
- tránh flag không enforce được.

### 2. `IsolationLevel` + command timeout

- tiện cho ADO.NET `CreateCommand()`;
- Dapper/RepoDb raw path có thể không dùng cùng policy;
- command timeout thuộc command hơn là Unit of Work.

### 3. Full options: isolation, transaction timeout, read-only, command timeout

- API giàu hơn;
- nhiều semantics không portable;
- dễ quay lại tình trạng option tồn tại nhưng behavior phụ thuộc provider.

**Baseline:** 1.

---

## D6. Target frameworks của package v1

### 1. Chỉ `netstandard2.0` — **Recommended**

- một implementation path;
- đúng mục tiêu .NET Framework 4.7.2+;
- modern .NET vẫn consume được;
- tránh package behavior khác nhau theo TFM.

### 2. `netstandard2.0;net8.0`

- có thể optimize modern path;
- phải package-validation giữa hai assembly;
- tăng test matrix và nguy cơ semantics divergence.

### 3. `net472;netstandard2.0`

- có thể dùng API đặc thù .NET Framework trong asset riêng;
- gần như không cần nếu `netstandard2.0` đã đáp ứng requirement;
- tăng maintenance mà không có lợi ích rõ ở v1.

**Baseline:** 1.

---

## Những quyết định không cần hỏi lại ở v1

Các điểm sau được coi là invariant sản phẩm, không phải option:

1. Runtime tối thiểu được công bố là .NET Framework 4.7.2+.
2. Inner rollback/abandon phải rollback toàn root transaction.
3. Mỗi nested `Begin()` phải trả scope riêng.
4. Không có public `ClearCurrent()`.
5. Root cleanup phải clear ambient state kể cả khi provider lỗi.
6. Commit failure không tự retry.
7. Core không dùng reflection để dò async transaction methods.
8. Stable release phải có test chạy thật trên `net472`.
9. Stable release phải có SQL Server integration verification.
10. Core không phụ thuộc Dapper hoặc RepoDb runtime package.
