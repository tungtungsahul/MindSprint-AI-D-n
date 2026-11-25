# Font nội bộ của MindSprint AI

Theo giao diện nhóm đang chạy (`MindSprint-AI-D-n` nhánh `main`, `frontend/ui-refresh.css`): tiêu đề **Manrope**, nội dung **DM Sans**.

- **@fontsource-variable/manrope@5.1.1** (`manrope/`): Latin, Latin mở rộng, Vietnamese; độ đậm 200–800. Dùng cho tiêu đề, logo.
- **@fontsource-variable/dm-sans@5.1.1** (`dm-sans/`): Latin, Latin mở rộng; độ đậm 100–1000. **Không có bộ tiếng Việt** (Fontsource/Google không phát hành) → chữ có dấu tiếng Việt hiện bằng Inter.
- **@fontsource-variable/inter@5.1.1** (`inter/`): Latin, Latin mở rộng, Vietnamese — dự phòng cho chữ có dấu trong nội dung.
- **@fontsource/jetbrains-mono@5.3.0** (`jetbrains-mono/`): mã, mã lớp, đồng hồ.
- Mỗi thư mục giữ `OFL.txt` (SIL Open Font License 1.1) và `metadata.json` gốc. Nguồn ghim: https://cdn.jsdelivr.net/npm/@fontsource-variable/<tên>@5.1.1/ (JetBrains Mono: @fontsource/jetbrains-mono@5.3.0).
- Font phục vụ từ dự án; không tải từ bên thứ ba khi chạy (frontend nhóm cũ tải Google Fonts — đã thay). Khai báo theo unicode-range, font-display: swap ở `css/mindsprint-fonts.css`.
