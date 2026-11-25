# Giao diện chuẩn của nhóm MindSprint (đối chiếu bắt buộc)

**Nguồn sự thật:** giao diện **đang chạy** của repo nhóm cũ `MindSprint-AI-D-n`, nhánh `main` (commit `452426d`, 13/11/2026), mở thật bằng trình duyệt ngày 15/11/2026 — **không** phải `frontend/tokens.css` (cam, 06/10) hay ảnh `design-reference/*-v39.png` (đã lỗi thời). File quyết định màu là `frontend/ui-refresh.css` (nạp sau cùng, 02–04/11).

Ảnh chụp: `docs/giao-dien-nhom/` (00–18). Trước khi sửa giao diện, mở ảnh tương ứng để so; sửa xong chụp màn hình MVC đặt cạnh.

## Cách chạy lại bản nhóm để đối chiếu (chỉ đọc, không sửa repo nhóm)

```powershell
# Backend .NET 8 chạy trên runtime .NET 10 của máy; DB riêng MindSprintGroupRef (tự tạo + dữ liệu mẫu).
cd LapTrinhWeb\DangTrienKhai\MindSprint-AI-D-n\backend\MindSprint.Api
$env:DOTNET_ROLL_FORWARD='Major'; $env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5000'
$env:ConnectionStrings__Default='Server=localhost;Database=MindSprintGroupRef;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --no-launch-profile
# Frontend: phục vụ thư mục frontend ở http://127.0.0.1:5500 (CORS của backend chỉ nhận cổng này), máy chủ tĩnh bất kỳ.
# Tài khoản demo (từ Data/DataSeeder.cs của nhóm): notebookai@demo.local / MindSprint123!
```

Không có key Gemini → Sổ tay AI, tạo thẻ bằng AI, gia sư, phân tích điểm yếu chỉ xem được giao diện.

## Bảng màu (ui-refresh.css)

| Vai trò | Sáng | Tối |
|---|---|---|
| Nền trang | `#fffefa` + vệt xanh chanh `rgba(238,246,217,.82)` góc trên trái, hồng đào `rgba(255,240,233,.82)` góc dưới phải | `#191721` (vệt rất nhạt) |
| Sidebar | `#fcfbf8`, viền phải `#eae8e0` | `#25212f` |
| Thẻ/khung | `#fffefa`, viền `#eae8e0`, bóng `0 8px 24px rgba(34,42,54,.045)`, bo 22px | `#25212f`, viền `#494052` |
| Nền phụ (ô nhập, hover) | `#f0f0e9` | `#302a3a` |
| Chữ chính / phụ | `#202b3e` / `#536071` | `#f5f1f9` / `#c8bfd1` |
| Nút chính | nền `#c7e977`, chữ `#24321d`, hover `#bce365`, bóng xanh nhẹ, bo 13px | `#b5d965`, chữ `#1f291b` |
| Nhấn nhạt (mục đang chọn, ô icon) | nền `#eef6d9`, viền `#dce8c2`, chữ/icon `#55763c` | `#2f3b2a` / `#536541` / `#c5df9b` |
| Phụ hồng đào | `#fff0e9` (nút "Bắt đầu ngay", thẻ B) | `#3d302a` |
| Thẻ pastel xoay vòng | A `#f2f6e7→#fbf8e9`, B `#fff0e9→#fffefa`, C `#e9e9f5→#f3f2fa` | tối tương ứng |
| Lỗi | `#bf5b52` (nút "Kết thúc", "Chưa thuộc" viền đỏ) | `#eea49a` |

Font: tiêu đề **Manrope** 700–800 (chữ -0.03 → -0.045em), nội dung **DM Sans** (không có tiếng Việt → chữ có dấu dùng font dự phòng).

## Bố cục và thành phần

| Thành phần | Bản nhóm | Ảnh |
|---|---|---|
| Sidebar (rộng ~210px) | Logo: ô icon vuông 36px nền xanh nhạt + "MindSprintAI" Manrope 800. **Thẻ người dùng** ngay dưới logo (tên, "Học mỗi ngày, tiến bộ một chút.", nút Đăng xuất/Đăng nhập). Tiêu đề nhóm **IN HOA nhỏ giãn chữ** (HỌC TẬP / TỔ CHỨC / ỨNG DỤNG). Mục: icon xám + chữ; đang chọn nền `#eef6d9` chữ xanh rêu đậm. Đáy: nút chính "+ Bộ thẻ mới" rộng hết + link "Trợ giúp" | 01 |
| Header | Ô "Tìm trang hoặc bộ thẻ…" viền xám bo tròn; phải: nút tròn thư, chuông; avatar **vuông bo góc** nền xanh nhạt chữ cái xanh + tên đậm + email nhỏ; vạch mảnh dưới | 01 |
| Đầu trang | Nút nhỏ ẩn/hiện sidebar · tiêu đề lớn Manrope · mô tả nhỏ xám · nút chính xanh chanh góc phải | 05, 07–11 |
| Trang chủ | "Chào buổi tối, {tên}" + "Mỗi ngày một chút tiến bộ…" + ngày (có icon) góc phải; khối hero "Hôm nay học gì?" (nhãn viên thuốc, nút chính + nút phụ trắng); "Tiến độ hôm nay" (vòng % + 3 số liệu có vạch ngăn); "Mục tiêu hôm nay" (checkbox + nút hồng đào); "Bộ thẻ flashcard gợi ý" + "Xem tất cả ›"; "Góc học tập của bạn" 4 thẻ có ô icon + link "Mở … →" | 01–03 |
| Thẻ bộ học (Thư viện) | Nền pastel xoay vòng, ô icon vuông, nhãn danh mục IN HOA nhỏ, tên đậm, "N thẻ học" + "Thành thạo 0%", thanh tiến độ, 2 nút "Xem thẻ" (trắng) + "Luyện tập" (xanh chanh) | 04 |
| Tab con | Hộp viên thuốc trắng chứa 2 tab, tab chọn nền xanh chanh (Trắc nghiệm & Tự luận / Game Ghép Thẻ) | 05 |
| Quiz | Khung trắng giữa trang: "Câu hỏi 1/10" + thanh + "★ Điểm" + nút "Kết thúc" viền đỏ; thẻ câu hỏi (nhãn danh mục, "CHỌN NGHĨA PHÙ HỢP CHO TỪ:", từ lớn, gợi ý nghiêng); 4 đáp án 2 cột có vòng chữ cái; đúng → nền xanh nhạt viền xanh xám + "Chính xác! Làm tốt lắm." + nút "Tiếp theo →" | 16 |
| Thẻ học | Hộp thoại: thanh "Tiến trình học tập · Thẻ 1/17"; mặt trước kem (từ), mặt sau tím nhạt (nghĩa) + "Chưa thuộc" (viền đỏ) / "Đã thuộc" (viền xanh); mũi tên ‹ › | 14, 15 |
| Công việc (Kanban) | Ô tìm + lọc ưu tiên + nút "+ Thêm công việc"; 3 cột (Chưa bắt đầu / Đang làm / Hoàn thành, có số đếm, nút "+" tròn); thẻ việc: cờ ưu tiên, tiêu đề, mô tả, nút sửa/xóa, mũi tên chuyển cột/đổi thứ tự; hộp thoại thêm việc (tiêu đề, mô tả, ưu tiên, trạng thái, hạn) | 10, 12, 13 |
| Lịch học | Lưới tuần Thứ Hai → Chủ Nhật × giờ, đầu cột nền xanh nhạt; khối "Không có lịch học hôm nay" bên phải; nút "Bật thông báo", "+ Thêm lịch học" | 09 |
| Tổng quan | 3 ô số liệu (Điểm thành thạo, Số từ ôn hôm nay, Chủ đề cần cải thiện), "Độ thành thạo theo ngành học" có thanh | 07 |
| Điểm yếu | Khối trạng thái "Chưa có điểm yếu rõ rệt", nút "Phân tích lại", "Tạo thẻ ôn tập từ điểm yếu", "Luyện tập ngay" | 08 |
| Sổ tay AI | Cột trái "Sổ tay & Nguồn" (chọn sổ, thêm nguồn); giữa quả cầu gradient + "Bạn muốn khám phá điều gì từ tài liệu?"; tab Theo nguồn / Gia sư / Hỏi đáp theo nguồn; ô hỏi + giọng nói + nút "Gửi câu hỏi" | 06 |
| Cài đặt | Các khối: Ứng dụng PWA, Giao diện sáng/tối, Kiểu âm thanh báo lịch học (3 ô chọn), Thử nghiệm Streak… | 11 |
| Điện thoại | Header thu gọn (tìm kiếm + nút + chuông + avatar); **thanh tab dưới đáy**: Hôm nay · Thư viện · Luyện tập · Công việc · Khác | 18 |
| Chế độ tối | Nền `#191721`, khung `#25212f`, nút vẫn xanh chanh | 17 |

## Chênh lệch MindSprintAI_ASP.NET_MVC so với bản nhóm (15/11/2026)

1. ~~Màu cam~~ → đã đổi sang bảng màu trên (15/11). Font Manrope/DM Sans đã đổi.
2. Sidebar thiếu **thẻ người dùng** dưới logo; tiêu đề nhóm chưa IN HOA giãn chữ; thiếu link "Trợ giúp" ở đáy.
3. Header: avatar đang tròn + hiện vai trò (Học viên/Giảng viên); nhóm: avatar vuông bo góc nền xanh nhạt + email. Chưa có nút thư (thuộc Sổ tay AI — làm khi chuyển Sổ tay).
4. Đầu trang chưa có nút nhỏ ẩn/hiện sidebar cạnh tiêu đề (MVC đặt nút này trong sidebar).
5. Nút phụ: MVC nền xanh nhạt; nhóm nền trắng viền xám (hover xanh nhạt).
6. Điện thoại: MVC dùng nút ☰ + menu thả; nhóm dùng **thanh tab dưới đáy**.
7. Trang chủ người đã đăng nhập: MVC chuyển thẳng sang "Bài của tôi"; nhóm có trang chủ bảng điều khiển (lời chào, tiến độ hôm nay, mục tiêu, gợi ý, góc học tập) — làm cùng các chức năng nhóm (Tổng quan, streak).
8. Kanban, Lịch học, Điểm yếu, Sổ tay AI, Tổng quan, Thư viện từ vựng hệ thống, Game ghép thẻ, Cài đặt (âm thanh, PWA): **chưa có** trong MVC — chuyển theo `docs/AI_HANDOFF.md`.
