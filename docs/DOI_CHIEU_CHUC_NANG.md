# Đối chiếu chức năng: bản nhóm cũ (MindSprint-AI-D-n) → project nhóm mới

Rà ngày 15/11/2026 bằng cách liệt kê mọi tab, hộp thoại, nút (`frontend/index.html`), mã JS (`script.js`, `notebook.js`, `ui.js`…) và API (`backend/MindSprint.Api/Controllers`) của bản nhóm, rồi đánh dấu từng mục.
QuizEdu (Project_Tham_Khao): đã chuyển đủ — model DB giống hệt, toàn bộ 1720 test gốc chạy qua; không liệt kê lại ở đây.

Ký hiệu: ✅ đã có · 🟡 có nhưng thiếu/khác · ❌ chưa có

## Khung ứng dụng
| Mục nhóm | Trạng thái | Ghi chú |
|---|---|---|
| Thanh bên: hồ sơ, Học tập / Tổ chức / Ứng dụng, nút "Bộ thẻ mới", nút thu gọn | ✅ | số đo lấy từ bản nhóm đang chạy |
| Danh mục con dưới "Thư viện" (Tất cả · Tiếng Anh · Lập trình · Kiến thức chung) | ✅ | hiện khi đang ở Thư viện/Từ vựng, thêm "Bộ học của tôi" |
| Link "Trợ giúp" cuối thanh bên | ✅ | bản nhóm chỉ là dòng chữ; bên mới mở trang hướng dẫn `/tro-giup` |
| Đầu trang: ô tìm, nút thư (mở Sổ tay AI), chuông, tài khoản | ✅ | có thêm nút sáng/tối |
| Hộp thông báo chung (app-dialog) | ✅ | dùng hộp hỏi lại `data-confirm` có sẵn |

## Trang
| Tab nhóm | Trạng thái | Còn thiếu |
|---|---|---|
| Trang chủ | ✅ | "Mục tiêu hôm nay" 3 ô tích như nhóm, **tự tích** khi đã ôn thẻ / làm quiz / ghi chú hôm nay; vòng tiến độ = % mục tiêu; "Bắt đầu học 15 phút" khi chưa có bài |
| Thư viện bộ thẻ | ✅ | nút "Tạo thẻ bằng AI" trên trang Thư viện (bộ học mới chỉ tạo khi bấm Lưu); thêm/sửa thẻ ở trang Thẻ của bộ học |
| Học thẻ (lật thẻ, Nhớ/Quên, trước/sau) | ✅ | bộ học riêng: trang Ôn thẻ (lặp lại ngắt quãng, QuizEdu). Từ vựng hệ thống: hộp "Ôn tập bộ thẻ" giống ảnh 14–15 (thanh tiến trình, lật thẻ, Chưa thuộc/Đã thuộc, trước/sau) |
| Luyện tập: Trắc nghiệm & Tự luận, Game ghép thẻ | ✅ | |
| Sổ tay AI — nguồn văn bản/tệp/ảnh, hỏi đáp, ghi chú, nói/nghe | ✅ | |
| Sổ tay AI — **nguồn từ đường link** | ✅ | chặn link nội bộ/máy chủ riêng, tối đa 2 MB chữ |
| Sổ tay AI — **Studio**: Tóm tắt, Study guide, FAQ, Timeline, Mind map, Flashcard, Trắc nghiệm, Audio overview | ✅ | đủ 8 loại; nghe tổng quan đọc bằng giọng của trình duyệt |
| Sổ tay AI — **Gia sư**: Giải bài / Kiểm tra bài làm / Tự luyện / Lập trình; Từng bước / Ngắn gọn | ✅ | + đọc chữ từ ảnh đề |
| Sổ tay AI — gợi ý câu hỏi, lưu lịch sử hỏi đáp | ✅ | lịch sử lưu ở trình duyệt như nhóm |
| Hiển thị **công thức toán (KaTeX)** trong câu trả lời | ✅ | KaTeX 0.18.7 (giấy phép MIT) đặt trong `wwwroot/lib/katex` |
| Tổng quan, Điểm yếu | ✅ | "Tạo thẻ ôn tập từ điểm yếu": thẻ "Chưa nhớ" + câu làm sai → bộ học mới (không cần AI). Bài kiểm tra chỉ tính khi đã công bố đáp án |
| Lịch học + chuông báo | ✅ | |
| Công việc (Kanban) | ✅ | |
| Cài đặt | ✅ | |
| **Màn ăn mừng mốc chuỗi ngày học** (30, 50, 100… ngày) | ✅ | mỗi mốc hiện một lần |
| Nút thử của nhà phát triển (mở khóa, thử chuỗi) | — | không chuyển (công cụ thử của nhóm) |

## Kết quả rà lần cuối (15/11)
Đã đối chiếu lại toàn bộ 9 tab (`data-tab`), 10 hộp thoại (thêm/sửa thẻ, tạo thẻ AI, ôn bộ thẻ, mốc chuỗi, lịch học, công việc, chuông báo, hộp hỏi chung) và 6 nút có id của `index.html` nhóm: **không còn mục ❌**.
Khác biệt có chủ ý: Điểm yếu tính ngay khi mở trang nên không cần nút "Phân tích lại"; nút thử của nhà phát triển không chuyển.
Thêm cho buổi demo: dữ liệu mẫu khi bấm "Dùng thử tài khoản demo", `README.md`, `docs/KICH_BAN_DEMO.md`.

## Rà lần 3 (15/11, chiều) — đối chiếu từ mã nguồn, không dựa vào bảng cũ
Nguồn đã đối chiếu: 201 action + 107 view + toàn bộ service/JS của QuizEdu; toàn bộ endpoint backend nhóm (bản chính ASP.NET) và bản FastAPI phụ (`backend/app`, `standalone.html`); mọi hàm trong `script.js`, `notebook.js`, `ui.js`, `auth.js`, `voice.js`, `kanban.js`, `cockpit.js`; yêu cầu FR-01…FR-23 (`docs/requirements`); bảng phân công `YeuCauNhom/Phân Công Nhiệm Vụ`.

| Phát hiện | Nguồn | Đã làm |
|---|---|---|
| Trang chủ giảng viên thiếu "Cần xem" từng lớp (link + loại việc), chế độ + hạn bài, lối tắt "Tạo lớp"; "Giao bài" trỏ sai | QuizEdu `Home/Lecturer` | Bù đủ trong trang chủ nhóm |
| Mất nút "Nghe câu trả lời" (do viết lại notebook.js) | FR-16, voice.js | Nút Nghe/Dừng ở mọi câu trả lời hỏi đáp + Gia sư; đổi chế độ/rời trang thì dừng |
| Ghi chú mất xuống dòng/thụt dòng | FR-13 | Giữ nguyên (cả dòng đầu) |
| Studio Flashcard/Trắc nghiệm không làm thử tại chỗ | notebook.js quizHtml/pickAnswer | Làm thử tại chỗ (chấm, giải thích, tổng điểm; lật thẻ) + "Lưu thành bộ học" không gọi AI lần hai |
| Không có key / AI lỗi thì không tạo được thẻ | script.js extractFallbackCards | Tự tách thẻ theo quy tắc của nhóm (`FallbackCards`), có thông báo |
| PDF đọc sai ký hiệu toán (£ ≥ ¥ Dx x!"#) | AiController CleanExtractedText | `PdfSymbols` (chỉ cho chữ từ PDF) |
| Không tự gợi ý số thẻ | estimateOptimalCardCount | `ai-count.js` |
| Không có học bù khi lỡ giờ học | checkMakeupStudyAlert | Hộp "Bạn đã lỡ giờ học → Học bù ngay" |
| Không dán được nhiều thẻ một lúc | standalone + flashcards_bulk.py | Trang Soạn thẻ + trang Nhập |
| Ô tìm chỉ tìm trang | ui.js "Tìm trang hoặc bộ thẻ" | Tìm cả bộ học + chủ đề từ vựng |
| Không có nút sửa trên thẻ đang ôn | ảnh 14 nhóm | "Sửa thẻ" mở đúng thẻ ở Soạn thẻ |
| Lật thẻ không có hiệu ứng 3D | phân công 5B | `MindSprintFlip` (tắt khi giảm chuyển động) |
| Không chặn tệp quá cỡ/sai loại trước khi gửi | phân công 5B | `data-max-bytes` + kiểm loại theo `accept` |
| Chế độ tối: nút "Bắt đầu ngay" 1,7:1; chữ đỏ giao diện sáng 4,3:1 | đo WCAG 25 trang | đều ≥ 4,5:1 |

Phạm vi MVC sử dụng số lượt ôn và câu đã trả lời, lịch tạo/sửa/xóa và bộ ôn từ điểm yếu. Tệp Word `.docx` được đọc qua `OfficeText`. Tự tạo công việc ôn tập và lập kế hoạch bằng AI thuộc các mục của bản đặc tả trước.

**Học khi mất mạng (FR-21/22).** Người học có thể tiếp tục ôn trên trang tự học đã mở. Bài giao mặc định lưu đánh giá rồi mới chuyển thẻ. Giảng viên có thể bật **Cho học viên ôn thẻ tiếp khi mất mạng**.

## Rà lần 4 (15/11, tối) — từng cột database, từng service backend, NFR-01→14, CI/CD, giao diện
Nguồn: bảng/cột cả hai project (QuizEdu 516/516 cột có đủ), mọi service + endpoint backend nhóm (53 endpoint, đều có chỗ tương ứng trừ các mục cố ý không chuyển dưới đây), `docs` NFR của nhóm, `.github/workflows`, `vocab_seed.json`, toàn bộ tab Cài đặt / hộp thoại thẻ / menu tài khoản.

| Phát hiện | Nguồn nhóm | Đã làm |
|---|---|---|
| Thiếu 740 thẻ từ vựng (bộ phía backend, nhiều chủ đề Lập trình / Kiến thức chung) | `backend/MindSprint.Api/Data/vocab_seed.json` (DataSeeder) | `tools/vocab/extract-vocab.cjs` đọc thêm tệp này, bỏ thẻ trùng (cùng danh mục + câu hỏi): 1832 → **2572 thẻ** |
| Thẻ không có "Ví dụ minh họa" | hộp Thêm/Sửa thẻ | Cột `Flashcards.Example` (≤ 2000 ký tự), ô ở Soạn thẻ, hiện dưới mặt sau khi ôn; thẻ từ vựng và thẻ ôn điểm yếu dùng ô này |
| Bộ học không có danh mục, không lọc theo danh mục | FR-04, hộp tạo thẻ AI (danh mục / danh mục mới) | Cột `StudySets.Category` (≤ 60), gõ tự do có gợi ý Tiếng Anh / Lập trình / Kiến thức chung, bộ lọc ở Thư viện |
| Không có model Gemini dự phòng | `GeminiOptions.FallbackModels` | Model chính hết lượt (429), ngừng (404), quá tải (500/503) → thử `gemini-flash-lite-latest` (đổi được bằng `Gemini:FallbackModels`). Không dùng `gemini-flash-latest` của nhóm: thử thật 16/11 nó treo quá 400 giây |
| Không kiểm kết quả Studio / Gia sư | NFR-14 `NotebookOutputValidation` | Quá 200.000 ký tự, sơ đồ tư duy quá 3 cấp / 100 nút / 12 con / tiêu đề 100 ký tự → báo lỗi thay vì hiện |
| Không nhận tệp Word (tạo thẻ AI), không đọc DOCX/PPTX/XLSX/RSS qua đường link | hộp tạo thẻ AI (.docx), `NotebookDocumentExtractor` | `OfficeText` (không thêm thư viện): tải tệp và đường link đều đọc được Word, PowerPoint, Excel; RSS/XML bỏ thẻ lấy chữ |
| Thiếu header bảo mật | `ApplicationBuilderExtensions` | `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin` |
| Không có địa chỉ kiểm tra sức khỏe | NFR-08 `/healthz`, `/readyz` | `/healthz` = "ok"; `/readyz` = "ready" hoặc 503 "not ready" khi mất database (không lộ cấu hình) |
| Điểm yếu thiếu dòng "nên làm gì" | `BuildSuggestedAction` | Gợi ý theo loại (bộ thẻ / bài quiz); nút "Ôn lại thẻ" bộ tự học nay mở trang Ôn thẻ (trước mở Soạn thẻ) |
| Không đổi được ảnh đại diện | menu tài khoản "Đổi ảnh đại diện / Dùng ảnh mặc định" | JPG/PNG/WebP ≤ 5 MB, trình duyệt cắt về 256×256; **khác nhóm**: lưu trên server (`App_Data/avatars`) thay vì localStorage nên đổi máy không mất; chỉ chủ tài khoản xem; đăng nhập Google không đè |
| "Giọng nói AI" khi báo giờ học | tab Cài đặt (bên nhóm chọn vào thì **không báo gì**: mã chỉ xử lý text/sound/both) | Kiểu "Chuông + đọc to giờ học" (giọng đọc có sẵn của trình duyệt, không tốn key) |
| Không có kiểm tra tự động trên GitHub | `.github/workflows/ci-cd.yml`, `nfr14-ai-output.yml` | `.github/workflows/ci.yml`: build + toàn bộ test với SQL Server tạm + đóng gói Docker. Không tự triển khai (server cá nhân) |
| Hướng dẫn sao lưu chưa có lệnh cụ thể | NFR-09 | `docs/TRIEN_KHAI_SERVER.md` mục 9: lệnh BACKUP/RESTORE + sao lưu ổ dữ liệu; sửa lệnh kiểm tra app (cần `-H "Host: …"`) |

Cố ý **không** chuyển (giữ cách hiện tại, có lý do):
- **Ôn ngắt quãng SM-2 của nhóm** → giữ hộp Leitner của QuizEdu (đã dùng cho bài giao, báo cáo GV, thẻ đến hạn); đổi thuật toán là đổi quy tắc nghiệp vụ.
- **Đọc trang web cần chạy JavaScript (Playwright)**: phải cài trình duyệt Chromium trong ảnh Docker (nặng ~300 MB) và chạy trình duyệt ẩn trên server; chưa làm, chờ quyết định. Trang thường, PDF, Word, PowerPoint, Excel, RSS vẫn đọc được.
- JWT/refresh token, bộ nhớ đệm kết quả Studio, `tutor/file` (giao diện nhóm không gọi), tệp `.doc` (máy chủ nhóm cũng từ chối), nút giả lập chuỗi ngày, "phút học" giả.
- Hiệu năng NFR-01 chưa đo trên server thật (có sẵn `tools/loadtest`).

## Đối chiếu bản MVC trên main (26/11/2026)

Bản MVC trên `main` có trang hồ sơ với đổi tên hiển thị và đổi mật khẩu, giao diện 403, lớp học và dữ liệu mẫu, cùng thư viện hiển thị riêng quiz, thẻ và nguồn. Các yêu cầu và ca nghiệm thu của bản hiện tại được cập nhật trong Wiki. [Yêu cầu chức năng](https://github.com/tungtungsahul/MindSprint-AI-D-n/wiki/Yeu-cau-chuc-nang) · [Yêu cầu phi chức năng](https://github.com/tungtungsahul/MindSprint-AI-D-n/wiki/Yeu-cau-phi-chuc-nang)

CI của `be7352f` chạy đạt 1.844 kiểm thử với SQL Server và dựng thành công ảnh Docker. [Kết quả CI](https://github.com/tungtungsahul/MindSprint-AI-D-n/actions/runs/45703769120)
