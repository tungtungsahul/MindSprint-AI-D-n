# Báo cáo kết quả Issue #44 — [Test][NFR-03] Kiểm thử xác thực, phân quyền và cô lập tài khoản

* **Người thực hiện:** `phandien665-prog`
* **Nhánh:** `dien/issue-44-nfr03-auth-isolation`
* **Pull Request:** https://github.com/tungtungsahul/MindSprint-AI-D-n/pull/new/dien/issue-44-nfr03-auth-isolation
* **Ngày kiểm tra:** 28/10/2026
* **Mức độ ưu tiên:** P0 (Milestone: Sprint 4 - Tuần 4: Kiểm thử, triển khai, đóng gói)
* **Issue:** https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/44

---

## 1. Tóm tắt kết quả kiểm thử

Đã hoàn thành kiểm thử toàn diện các tiêu chí **NFR-03** trên nền tảng **backend chính ASP.NET Core 8 + PostgreSQL 17**:

| STT | Bộ kiểm thử | Số test | Kết quả | Ghi chú |
|:---:|---|:---:|:---:|---|
| 1 | **AuthIsolationChecks** (C# ASP.NET Core + PostgreSQL) | 40/40 | **100% PASS** | Kiểm thử trực tiếp JWT, IDOR Matrix, Concurrency Rotation & PostgreSQL Engine |
| 2 | **GoogleAuthChecks** (C# Google Token & Challenges) | 33/33 | **100% PASS** | Kiểm thử Nonce replay/expiration, Origin Whitelist, Account Linking |
| 3 | **KanbanChecks** (C# Tasks CRUD & Permissions) | 19/19 | **100% PASS** | Kiểm thử quyền sở hữu thẻ Kanban và tính toàn vẹn thứ tự kéo thả |
| 4 | **Frontend Session & Refresh** (`frontend-session.test.cjs`) | 11/11 | **100% PASS** | Tự động refresh token, xử lý đồng thời, hủy token cũ khi đăng xuất |
| 5 | **Frontend Google Auth** (`frontend-google.test.cjs`) | 6/6 | **100% PASS** | Session Google đồng bộ, đổi tài khoản không đè session |
| 6 | **Frontend Validation & Text** (Node.js test suite) | 9/9 | **100% PASS** | Client-side validation email, password, URL, file & math rendering |
| **TỔNG CỘNG** | **Toàn bộ hệ thống kiểm thử Sprint 4** | **118/118** | **100% PASS** | Đạt đầy đủ tiêu chuẩn nghiệm thu P0 |

---

## 2. Chi tiết các nội dung đã khắc phục và kiểm chứng

### 2.1. Phát hiện & Sửa lỗi Race Condition trong Refresh Token Rotation
* **Hiện trạng ban đầu:** Khi có 10 luồng gửi đồng thời cùng một Refresh Token cũ lên server, cơ chế `RotateAsync` cũ gặp race condition: có 2 request đồng thời đọc `IsRevoked == false`, dẫn đến việc cấp 2 phiên mới song song (vi phạm tiêu chí NFR-03).
* **Giải pháp khắc phục:** Cập nhật `TokenService.RotateAsync` sử dụng câu lệnh nguyên tử `ExecuteUpdateAsync` của EF Core 8:
  ```csharp
  var affected = await db.RefreshTokens
      .Where(r => r.Id == rt.Id && !r.IsRevoked)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRevoked, true));
  if (affected == 0) return null; // Luồng khác đã thu hồi trước đó
  ```
* **Kết quả kiểm chứng:** Chạy thử nghiệm 10 luồng đồng thời trên PostgreSQL: **Chính xác 1 request thành công, 9 request bị từ chối** (`Success: 1, Rejected: 9`).

### 2.2. Ma trận cô lập dữ liệu người dùng (Anti-IDOR Matrix)
Tạo hai tài khoản riêng biệt: User A (`user-a@mindsprint.test`) và User B (`user-b@mindsprint.test`). Dùng token của User B để tấn công vào tài nguyên của User A:
1. **Tasks (Kanban):** User B không xem được task của A (`GetAll()` trả về danh sách rỗng); gọi `Update`, `Move`, `Delete` trên `taskA.Id` đều trả về `404 Not Found`. Dữ liệu của A nguyên vẹn.
2. **Flashcards:** User B không thấy thẻ cá nhân của A; thấy thẻ hệ thống Oxford (`OwnerId == null`). User B không thể `Update` hoặc `Delete` thẻ của A lẫn thẻ hệ thống (trả về 404).
3. **Review (SRS):** User B không thể gửi submit kết quả ôn tập cho thẻ cá nhân của User A (`POST /api/review` trả về 404).
4. **Notebooks & Sổ tay:** User B không thấy sổ tay của A; truy cập nguồn (`/sources`), ghi chú (`/notes`), câu hỏi (`/chat`) hoặc xóa sổ tay của A đều nhận `404 Not Found`.
5. **Study Stats:** Ngày học và chuỗi học tập (streak) hoàn toàn cô lập theo từng UserId.

### 2.3. Kiểm thử trên PostgreSQL 17 thật (Live Database Engine)
* Đã kết nối thành công đến PostgreSQL `mindsprint` tại `localhost:5432`.
* Kiểm tra việc tạo bảng, cascade delete, ràng buộc `Unique Index` (Email, GoogleSubject, ExternalId, Token).
* Xác minh tính toàn vẹn dữ liệu khi tạo và xóa ephemeral records trong bài kiểm tra thực tế.

---

## 3. Lệnh chạy kiểm thử để tái lập kết quả (Reproduction Steps)

```powershell
cd "C:\Users\phand\.gemini\antigravity\scratch\MindSprint-AI-D-n"

# 1. Chạy Auth & Account Isolation Checks (40 tests)
dotnet run --project backend/AuthIsolationChecks/AuthIsolationChecks.csproj

# 2. Chạy Google Authentication Checks (33 tests)
dotnet run --project backend/GoogleAuthChecks/GoogleAuthChecks.csproj

# 3. Chạy Kanban Ownership & Position Checks (19 tests)
dotnet run --project backend/KanbanChecks/KanbanChecks.csproj

# 4. Chạy Frontend Session & Token Tests (26 tests)
node --test tests/frontend-session.test.cjs tests/frontend-text.test.cjs tests/frontend-validation.test.cjs backend/GoogleAuthChecks/frontend-google.test.cjs
```

---

## 4. Kết luận & Đề xuất
Yêu cầu phi chức năng **NFR-03 (Issue #44)** đã được kiểm thử, khắc phục điểm nghẽn concurrency và xác minh đạt **100% tiêu chí nghiệm thu** trên kiến trúc ASP.NET Core 8 + PostgreSQL.
Sẵn sàng cho việc rà soát (code review) và đóng Issue #44 trên GitHub.
