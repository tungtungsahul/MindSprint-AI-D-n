# Kịch bản demo MindSprint AI (~10 phút)

Chuẩn bị trước buổi demo:

1. Chạy app (README mục 2). Mở sẵn hai cửa sổ trình duyệt: cửa sổ thường (giảng viên) và cửa sổ ẩn danh (học viên).
2. Có sẵn API key Gemini nếu muốn diễn phần AI (dán ở trang Tài khoản trước khi lên trình bày — không dán key trước khán giả).
3. Thử chạy hết kịch bản một lần; nếu muốn bắt đầu lại sạch, xóa database rồi `dotnet ef database update`.

| # | Ai làm | Thao tác | Điểm cần nói |
|---|--------|----------|--------------|
| 1 | Người demo | Trang Đăng nhập → **Dùng thử tài khoản demo** | Một cú bấm có ngay dữ liệu mẫu |
| 2 | Học viên | **Trang chủ**: lời chào, "Hôm nay học gì?", Tiến độ hôm nay, Mục tiêu hôm nay | Số liệu lấy từ hoạt động thật; mục tiêu tự tích khi đã học |
| 3 | Học viên | **Thư viện** → bộ "Demo — Sinh học tế bào" → **Ôn thẻ**, chấm Đã nhớ / Chưa nhớ | Ôn lặp lại ngắt quãng: thẻ chưa nhớ quay lại sớm hơn |
| 4 | Học viên | Thanh bên **Thư viện → Tiếng Anh** → một chủ đề → **Ôn thẻ** (lật thẻ, Đã thuộc/Chưa thuộc) → **Chơi ghép thẻ** | 1.800+ thẻ từ vựng có sẵn; lưu chủ đề vào thư viện riêng |
| 5 | Học viên | **Luyện tập** → tab Trắc nghiệm & Tự luận | Tự ôn từ vựng bất cứ lúc nào |
| 6 | Giảng viên | Đổi sang **Giảng viên** → **Lớp** → tạo lớp → lấy **mã vào lớp** | |
| 7 | Học viên (ẩn danh) | Đăng ký nhanh → **Vào lớp bằng mã** | |
| 8 | Giảng viên | Giao bộ "Demo — Sinh học tế bào" cho lớp | Hạn nộp, số lần làm |
| 9 | Học viên | **Bài cần làm** → làm quiz → xem kết quả | Chấm điểm phía máy chủ, không gian lận được từ trình duyệt |
| 10 | Giảng viên | **Báo cáo** → mở bài giao → xem kết quả từng học viên | Kết quả cập nhật ngay |
| 11 | Học viên | **Điểm yếu** → **Tạo thẻ ôn tập từ điểm yếu** | Câu làm sai thành thẻ ôn, không cần AI |
| 12 | (Có key) | **Sổ tay AI** → hỏi "Ti thể làm gì?" → câu trả lời có trích dẫn [S1]; Studio → **Sơ đồ tư duy**; chế độ **Gia sư** | AI chỉ trả lời dựa trên tài liệu, có nguồn |
| 13 | Học viên | **Lịch học** (bật chuông) · **Công việc** (kéo thẻ giữa cột) · **Tổng quan** | Tổ chức việc học |
| 14 | Bất kỳ | Bật **giao diện tối**; thu nhỏ cửa sổ cỡ điện thoại; **Cài đặt → Cài ứng dụng lên máy** | Dùng được trên điện thoại như ứng dụng |

Câu hỏi hay gặp:

- *Dữ liệu AI có gửi đi đâu không?* — Chỉ đoạn tài liệu liên quan được gửi tới Google Gemini bằng key của chính người dùng; xem trang **Chính sách**.
- *Không có mạng/không có key thì sao?* — Mọi phần trừ AI vẫn chạy.
