# Kế hoạch chỉ triển khai server laptop — 09–22/11/2026

## Phân công hiện hành sau rà soát phạm vi — 09/11/2026

Theo yêu cầu chủ trì, bỏ các công việc **chưa hoàn thành** không sát mục tiêu triển khai server trên laptop và thay bằng việc cài đặt/vận hành trực tiếp. Các issue đã hoàn thành được giữ nguyên.

**20 đầu việc hiện hành, đều là issue:** Sơn/Hùng/Linh/Diễn mỗi người **4**, Duy/Chiến mỗi người **2**; **6/6 người có việc cả hai tuần**.
- Tuần 1 **09–15/11/2026**, hạn chốt **20:00 15/11**.
- Tuần 2 **16–22/11/2026**, hạn chốt **20:00 22/11**.
- Mọi thời gian **Asia/Saigon**; các hạn sớm nằm trong bảng và từng issue.
- Mục tiêu: frontend, ASP.NET Core và SQL Server Express 2022 chạy Docker trên **laptop chủ trì**, mặc định **http://127.0.0.1:8080**.
- Bản này chỉ truy cập trên máy chủ; LAN/Internet không thuộc đợt hiện tại. Gemini vẫn cần Internet khi sử dụng; không chuyển thành chạy model AI offline.
- Công việc không gồm mở tunnel/router, runner laptop, deploy Render/Vercel, chấm 90 mẫu AI, accessibility toàn bộ, kiểm thử đa trình duyệt hay chiến dịch NFR ngoài phạm vi. Không thay tiêu chí lịch sử thành đạt khi thu hẹp kế hoạch.

## Bảng nhiệm vụ hiện hành

**Cập nhật kỹ thuật 10/11/2026:** bản self-host đã chuyển sang SQL Server Express 2022.
Các mục backup/migration/restore dưới đây áp dụng cho SQL Server. Đã bổ sung script
backup/restore, `/readyz`, restart policy và kiểm tra phục hồi; xem
[hướng dẫn vận hành](../docs/SELF-HOST-DEMO.md). Đây là cập nhật mã nguồn/tài liệu local,
không xác nhận hoàn thành hoặc thay trạng thái issue/PR trên GitHub.

| Thành viên | Tuần 1 — 09–15/11 | Tuần 2 — 16–22/11 | Tổng |
|---|---|---|---:|
| **Minh Sơn** @minhsonle30122007-png | [#106](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/106) Cấu hình môi trường riêng cho bản localhost — **13/11**<br>[#109](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/109) Kiểm chứng cấu hình Production và startup cùng origin — **15/11** | [#53](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/53) Dựng gói self-host tái lập từ checkout sạch — **19/11**<br>[#119](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/119) Hướng dẫn vận hành và xử lý sự cố trên máy chủ — **21/11** | **4** |
| **Đình Hùng** @James-Lloyd20 | [#108](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/108) Kiểm tra frontend và đường dẫn API trên localhost — **14/11**<br>[#110](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/110) Tránh cache cũ khi chuyển sang bản localhost — **15/11** | [#114](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/114) Smoke toàn luồng trên trình duyệt của máy chủ — **21/11**<br>[#115](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/115) Thu thập chẩn đoán Docker và log lỗi để hỗ trợ — **20/11** | **4** |
| **Tuấn Linh** @tuanlinh0511 | [#95](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/95) Sao lưu SQL Server của bản self-host — **14/11**<br>[#111](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/111) Giữ dữ liệu khi dừng và tạo lại container — **15/11** | [#99](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/99) Kiểm chứng migration trên SQL Server của bản self-host — **18/11**<br>[#102](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/102) Khôi phục backup vào SQL Server test riêng — **21/11** | **4** |
| **Phan Diễn** @phandien665-prog | [#113](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/113) Đo tài nguyên và thời gian khởi động bộ Docker — **15/11**<br>[#107](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/107) Kiểm chứng build Docker từ source sạch — **14/11** | [#116](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/116) Khởi động đúng thứ tự và tự phục hồi container — **20/11**<br>[#117](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/117) Kiểm tra phục hồi sau reboot, sleep và mất Internet — **21/11** | **4** |
| **Bảo Duy** @tungtungsahul | [#105](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/105) Chốt baseline và bàn giao phần self-host đang có — **11/11** | [#104](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/104) Review tích hợp và chốt bản demo trên máy chủ — **22/11** | **2** |
| **Minh Chiến** @mchihihaha | [#112](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/112) Phân biệt tiến trình sống và database sẵn sàng — **15/11** | [#118](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/118) Kiểm chứng cổng localhost và cách truy cập máy chủ — **21/11** | **2** |

## Phụ thuộc và checkpoint

- **11/11:** #105 bàn giao baseline/source self-host đang có, giữ provenance và bí mật ngoài repo/image. Các phần đã có được tiếp nhận qua PR, không tính toàn bộ là code mới người tích hợp.
- **15/11, 20:00:** chốt 10 đầu việc tuần 1; có build, config, đường dẫn localhost, cache, dữ liệu/backup, health và số đo tài nguyên.
- **18/11:** #99 kiểm chứng migration. **19/11:** #53 bàn giao gói self-host. Các task tuần 2 có thể chuẩn bị script/checklist từ trước, chạy nghiệm thu sau khi có gói.
- **20–21/11:** kiểm tra phục hồi container/laptop, chẩn đoán, cổng, smoke trình duyệt và restore; cập nhật #119 runbook.
- **22/11, 20:00:** #104 chốt SHA/image, hướng dẫn và bản demo với bằng chứng; lỗi chưa đạt giữ đúng trạng thái.
- Mỗi người cập nhật issue trước **20:00 11/11 và 18/11**, ghi đã làm/còn thiếu/blocker và gửi PR sớm đủ review.
- Mỗi task có reviewer/tiêu chí/lệnh kiểm chứng; dùng DB/volume test riêng. Việc reboot/sleep hoặc thay thiết lập máy chủ do chủ máy chủ động làm, không tự thao tác.
- Tự commit đúng danh tính và giữ provenance; không commit rỗng, sửa tác giả/backdate hoặc tính assign/comment thành code đã thực hiện.

## Đầu việc bị rút khỏi đợt này

**14 issue cũ đóng `not_planned` và PR #77 đóng không merge**, không ghi thành hoàn thành. Có 15 issue thay thế #105–#119. Giữ và thu hẹp đúng mục tiêu laptop cho #53/#95/#99/#102/#104. Các mô tả NFR trước đó được lưu trong lịch sử, không tự đổi thành kết quả Pass.

| Việc bị rút | Việc thay thế | Người thực hiện |
|---|---|---|
| #98 | [#105](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/105) Chốt baseline và bàn giao phần self-host đang có | @tungtungsahul |
| #46 | [#106](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/106) Cấu hình môi trường riêng cho bản localhost | @minhsonle30122007-png |
| #93 | [#109](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/109) Kiểm chứng cấu hình Production và startup cùng origin | @minhsonle30122007-png |
| #103 | [#119](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/119) Hướng dẫn vận hành và xử lý sự cố trên máy chủ | @minhsonle30122007-png |
| #45 | [#108](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/108) Kiểm tra frontend và đường dẫn API trên localhost | @James-Lloyd20 |
| #94 | [#110](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/110) Tránh cache cũ khi chuyển sang bản localhost | @James-Lloyd20 |
| #51 | [#114](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/114) Smoke toàn luồng trên trình duyệt của máy chủ | @James-Lloyd20 |
| #54 | [#115](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/115) Thu thập chẩn đoán Docker và log lỗi để hỗ trợ | @James-Lloyd20 |
| #96 | [#111](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/111) Giữ dữ liệu khi dừng và tạo lại container | @tuanlinh0511 |
| PR #77 | [#113](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/113) Đo tài nguyên và thời gian khởi động bộ Docker | @phandien665-prog |
| #97 | [#107](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/107) Kiểm chứng build Docker từ source sạch | @phandien665-prog |
| #100 | [#116](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/116) Khởi động đúng thứ tự và tự phục hồi container | @phandien665-prog |
| #101 | [#117](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/117) Kiểm tra phục hồi sau reboot, sleep và mất Internet | @phandien665-prog |
| #47 | [#112](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/112) Phân biệt tiến trình sống và database sẵn sàng | @mchihihaha |
| #49 | [#118](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/118) Kiểm chứng cổng localhost và cách truy cập máy chủ | @mchihihaha |

PR #85 đã có phần code idempotency được giữ để nhóm tự xử lý riêng; không phải một trong 20 nhiệm vụ triển khai laptop hiện hành. Không xóa branch hay commit của thành viên.

## Căn cứ đóng góp

Snapshot GitHub `main/b331cb291945257d82965b8dc867407a02a39591`: **269 commit**, trong đó **198 commit không phải merge** (tính cả root commit); đọc toàn bộ 3 trang commits và 23 PR.

| Thành viên | Commit không merge gắn tài khoản | Commit mang tên nhưng chưa gắn tài khoản (tham khảo) | PR tác giả đã merge | Đầu việc đợt này |
|---|---:|---:|---:|---:|
| Minh Sơn (@minhsonle30122007-png) | 6 | — | 0 | 4 |
| Đình Hùng (@James-Lloyd20) | 7 | — | 2 | 4 |
| Phan Diễn (@phandien665-prog) | 18 | — | 2 | 4 |
| Tuấn Linh (@tuanlinh0511) | 6 | 13 | 0 | 4 |
| Bảo Duy (@tungtungsahul) | 28 | — | 8 | 2 |
| Minh Chiến (@mchihihaha) | 47 | 21 | 6 | 2 |

**Giới hạn:** Đây là dấu vết Git, không phải thước đo chất lượng/năng lực hay công sức tuyệt đối. Commit cùng thay đổi có thể xuất hiện với SHA khác trong lịch sử; không cộng mọi nhánh/backup hoặc comment/issue được assign để tăng điểm. Các commit mang tên Tuấn Linh/Minh Chiến dùng email chưa liên kết chỉ ghi riêng, chưa xác nhận danh tính tuyệt đối. Danh tính dùng chung “MindSprint User” và bot không phân bổ tùy tiện cho cá nhân. PR do người khác mở thay không phải bằng chứng người được assign đã tự viết code.

Theo bổ sung trực tiếp của chủ trì, **Linh và Diễn cũng thuộc nhóm nhận nhiều việc**, không chỉ Sơn/Hùng. Việc đóng/mở issue lịch sử không được đổi chỉ để tạo chỉ tiêu.




## Kiểm tra sau cập nhật

Đã đọc lại GitHub: 20/20 nhiệm vụ hiện hành có assignee, hạn và phụ thuộc đang mở; 14 issue cũ đóng not_planned, PR #77 đóng không merge. Các issue đã hoàn thành trước đợt sửa không bị mở lại. Xóa vĩnh viễn chưa thực hiện vì connector không hỗ trợ và GitHub CLI hết hiệu lực đăng nhập. GitHub #92 là nguồn trạng thái chính thức; bản phân công trước trong assignments-2026-11-09.md chỉ là snapshot lịch sử.
