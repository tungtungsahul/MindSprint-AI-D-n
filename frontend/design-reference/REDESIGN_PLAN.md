# Kế hoạch redesign MindSprintAI — đổi hướng lần 2

Ngày 2026-10-06, người dùng yêu cầu và cho phép triển khai hướng **cam/gradient pastel theo ảnh Minded** tại `dribbble-minded.png`. Kế hoạch này **thay thế hoàn toàn hướng “giấy mực” và “cam phẳng”** trước đó, bao gồm palette, font và quy định cấm gradient/bóng/hover. Giữ nội dung và chức năng tiếng Việt, triển khai trên nhánh `frontend-redesign`.

## Bảng màu hiện hành

| Thành phần | Mã / cách dùng |
|---|---|
| Nền trang | `#F7F5FA`: trắng xám nhẹ ánh tím, không dùng ngà/be |
| Sidebar, panel sáng | `#FFFFFF`, viền mảnh `#E5E0ED` |
| Nút chính, nhấn | `#FF7A45`, hover `#E8622E` |
| Chữ chính | `#1A1A2E`, dùng cả trên nút cam để đạt tương phản |
| Chữ phụ | `#625C70` |
| Chữ/icon cam trên nền sáng | `#B23E13`, active nền `#FFF0E8` |
| Quả cầu | `linear-gradient(135deg, #A78BFA, #F472B6, #60A5FA)`; **chỉ một quả cầu lớn trong Sổ tay AI**, có lớp sáng nhẹ trên bề mặt |
| Card luân phiên A | `#FFF0E8 → #FCE4EC` |
| Card luân phiên B | `#FCE4EC → #F3E5F5` |
| Card luân phiên C | `#F3E5F5 → #E8F0FE` |
| Focus input | Cam + `0 0 0 4px rgba(255,122,69,.12)`; outline bàn phím xuất hiện ngay |
| Shadow hover card | `0 8px 24px rgba(255,122,69,.15)` |
| Đúng/hoàn thành, lỗi | Xanh `#26704B`, đỏ hồng `#BF354A`; không đổi ý nghĩa phản hồi |

Chế độ tối vẫn sử dụng được: nền tím xám `#191721`, panel `#25212F`, chữ sáng `#F5F1F9`, card gradient giảm sáng tương ứng. Nút chính vẫn cam. Không dùng lại bộ màu giấy mực. Các giá trị màu/gradient/shadow tập trung ở `tokens.css`.

## Font

Dùng **Inter 400–800** cho toàn giao diện, gồm tiêu đề, nhãn, nội dung và nút. Bỏ Lora/serif. Giữ KaTeX cho công thức và monospace cho đoạn mã; không sửa vendor.

## Header và sidebar

Header dùng chung tất cả tab: thanh tìm kiếm pill lớn phía trái; mail, chuông và avatar chữ cái + tên/email thật từ `MindSprintAuth` phía phải. Avatar header dùng nền cam nhẹ, không lặp quả cầu gradient. Cập nhật theo yêu cầu tiếp theo: quả cầu duy nhất chuyển từ Trang chủ sang Sổ tay AI. Không dùng tên, ảnh hoặc số thông báo mẫu.

Tìm kiếm chỉ hỗ trợ điều hướng các tab và bộ thẻ đang được render, không thêm endpoint. Mail mở ghi chú/tài liệu trong Sổ tay AI; chuông mở lịch học và nhắc nhở đang có; tài khoản mở đăng nhập hoặc Cài đặt.

Sidebar giữ nhóm **Học tập / Tổ chức / Ứng dụng**, đủ tám tab, icon Font Awesome rõ ràng. Active nền cam nhạt bo góc; chữ/icon cam đậm. **Danh mục chỉ hiển thị trong Thư viện**, giữ thay đổi đã được yêu cầu ở lượt trước. Mobile xếp lại header và sidebar, không tràn ngang.

## Bố cục các tab

| Tab | Hướng triển khai |
|---|---|
| Trang chủ | Bỏ quả cầu; lời chào và hành động học căn trái, chip hành động pill cam; thống kê thực; bộ thẻ gợi ý và bốn card chức năng gradient pastel luân phiên. Không thêm ô hỏi giả khi trang chưa có chức năng này. |
| Thư viện | Giữ lọc/tìm chủ đề/SRS; bộ thẻ gradient pastel, icon tròn nhỏ trước tiêu đề, nút pill cam. |
| Luyện tập | Giữ chọn trắc nghiệm/tự luận/ghép thẻ; panel trắng bo góc, tiến độ cam; đáp án dài xuống dòng đầy đủ, giữ phản hồi đúng/sai. |
| Lịch học | Lưới và widget nền trắng bo góc, tiêu đề ngày cam nhẹ; giữ lịch và nhắc nhở. |
| Công việc | Ba cột Kanban rõ ràng, task gradient pastel luân phiên; giữ kéo thả và nút chuyển cột. |
| Tổng quan | Card số liệu gradient nhẹ, chữ tối và nhấn cam; giữ biểu đồ/dữ liệu thực. |
| Sổ tay AI | Bám ảnh gốc: nguồn ở cạnh trái; quả cầu và lời chào giữa vùng hỏi đáp; chip trên composer lớn bo tròn; tám card Studio pastel ở phía dưới; kết quả/ghi chú giữ riêng. Tablet/mobile thu gọn nguồn và reflow. |
| Cài đặt | Nhóm thiết lập nền trắng bo góc; controls pill, checkbox/switch bật màu cam; giữ đổi theme. |

## Hiệu ứng tương tác — thay thế bản trước

Các phản hồi thao tác 120–200ms, easing tự nhiên `cubic-bezier(.4,0,.2,1)`; loading là trạng thái đang xử lý, không phải trang trí tự chạy. Không ripple. Không đổi cơ chế lật, chọn đáp án, kéo thả hoặc xử lý dữ liệu. Có hỗ trợ `prefers-reduced-motion`.

1. **Card chức năng/bộ thẻ/task:** hover `translateY(-4px)`, shadow cam `0 8px 24px rgba(255,122,69,.15)`, viền cam nhạt; active `translateY(-1px)`, bóng thu nhỏ. Transition 180ms; thiết bị không có hover không nâng thẻ khi chạm.
2. **Nút pill chính:** nền cam, hover cam đậm + bóng tăng; active `scale(.97)` + bóng thu nhỏ, 150ms. Disabled không transform/shadow.
3. **Input:** focus viền cam + shadow `0 0 0 4px rgba(255,122,69,.12)`, 150ms; outline focus bàn phím tức thì. Áp dụng tìm kiếm, ô hỏi AI và biểu mẫu.
4. **Avatar:** hover `scale(1.05)` 150–180ms; avatar header nền cam nhẹ và quả cầu Sổ tay AI dùng gradient. Không xoay liên tục.
5. **Chip nhanh/gợi ý:** hover `scale(1.03)` + viền cam đậm, active `scale(.96)`, 150ms.
6. **Sidebar:** hover nền cam rất nhạt, active cam nhạt + chữ/icon cam đậm; transition màu 150ms, active click nhẹ.
7. **Flashcard 3D:** giữ rotateY/cơ chế lật, easing `cubic-bezier(.4,0,.2,1)`, 200ms; shadow cam tăng khi flipped, không đổi state học.
8. **Đã thuộc/Chưa thuộc:** mỗi click nảy `1 → 1.05 → 1` trong 150ms bằng Web Animations API thuần hiển thị, bỏ motion nếu người dùng chọn reduced-motion.
9. **Toggle/switch:** núm translate và nền chuyển mượt 150ms; trạng thái bật cam. Checkbox/radio/range cũng dùng accent cam.
10. **Loading AI:** ba chấm cam đổi opacity mờ–rõ theo nhịp lệch, chỉ hiện khi chat/Studio đang xử lý; thay spinner trong các thông báo loading. Reduced-motion dùng chấm tĩnh.
11. **Liên kết chữ:** đường gạch chân cam 1px từ trái sang phải khi hover, 150ms; không gradient tô chữ.

## Phạm vi và xác minh

Không sửa backend, tests, các file chạy/cấu hình test, `standalone.html`, `frontend/js/`, `vocab_data.js`, `vendor/katex/`, API client hoặc bộ render công thức. Các hàm, exports, endpoint/payload và xử lý dữ liệu giữ nguyên. JS được sửa/thêm chỉ phục vụ DOM/render/hiệu ứng.

Các file triển khai: `tokens.css`, `style.css`, `kanban.css`, `index.html`, template icon trong `script.js`, template loading trong `notebook.js`, **thêm `ui.js`** cho header và phản hồi click; `sw.js`, `manifest.json` để cập nhật tài nguyên/PWA. Cập nhật tài liệu kế hoạch và audit/kết quả trong `design-reference/`.

Chạy `node --test tests/frontend-session.test.cjs tests/frontend-text.test.cjs` trước và sau; kiểm tra trình duyệt trên tám tab ở 320, 375, 414, 768px và desktop, sáng/tối, focus/hover/click/loading. Chụp preview từng tab. Hallmark audit theo yêu cầu, ghi rõ ngoại lệ người dùng đã chỉ định cho gradient/pill/glow/hover, vẫn kiểm tra an toàn layout, tương phản, trạng thái và tính nhất quán.

## Điều chỉnh theo phản hồi mới nhất

Người dùng yêu cầu Sổ tay AI bám ảnh Minded gốc sát hơn và chuyển quả cầu từ Trang chủ sang Sổ tay AI. Chỉ một `.welcome-orb` nằm trong template Sổ tay AI. Khi đã có hội thoại, lời chào/quả cầu được thu gọn để dành chỗ đọc câu trả lời. Ô hỏi giữ `#nb-q`, nút gửi giữ `data-act="send"`; tất cả `data-gen` và hành động tài liệu/ghi chú giữ nguyên. Nút cộng ở composer mở phần tải file hiện có, nút nguồn trên tablet/mobile chỉ đóng/mở vùng hiển thị, không gọi API.
