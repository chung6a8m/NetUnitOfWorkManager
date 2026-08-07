# Numbered design decisions

## Trạng thái

**APPROVED — 2026-08-07**

Baseline v1 đã được chốt chính thức với toàn bộ phương án khuyến nghị:

```text
D1=1, D2=1, D3=1, D4=1, D5=1, D6=1
```

Các quyết định này là implementation baseline của v1 và **không cần hỏi lại trong các work package P01–P10**. Chỉ thay đổi khi có yêu cầu explicit cập nhật quyết định kiến trúc.

---

## D1. Unit of Work lifecycle API

### 1. Synchronous lifecycle only — **APPROVED**

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

**Decision:** 1 — approved.

---

## D2. Database access surface

### 1. Borrowed session + safe `CreateCommand()` — **APPROVED**

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

**Decision:** 1 — approved.

---

## D3. Repository management

### 1. Không có repository factory/cache trong core — **APPROVED**

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

**Decision:** 1 — approved.

---

## D4. Concurrency policy

### 1. Sequential-use contract, không runtime guard — **APPROVED**

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

**Decision:** 1 — approved.

---

## D5. v1 transaction options

### 1. Chỉ `IsolationLevel` — **APPROVED**

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

**Decision:** 1 — approved.

---

## D6. Target frameworks của package v1

### 1. Chỉ `netstandard2.0` — **APPROVED**

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

**Decision:** 1 — approved.

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

## Change control

Nếu sau này cần thay đổi một quyết định đã chốt, cập nhật tài liệu này trước khi thay đổi implementation. Ghi rõ quyết định cũ, quyết định mới, lý do và work package bị ảnh hưởng để tránh drift giữa code, test và documentation.
