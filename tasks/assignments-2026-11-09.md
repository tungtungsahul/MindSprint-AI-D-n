# Phân công 09–22/11/2026

## Mục tiêu và quyết định phân công ngày 09/11/2026

Theo yêu cầu chủ trì: ưu tiên giao thêm công việc thực chất cho **Sơn, Hùng, Linh và Diễn**; mỗi người **4 đầu việc**. **Duy và Chiến mỗi người 2 đầu việc**. Cả **6/6 thành viên có ít nhất một đầu việc trong từng tuần**.

- Tuần 1: **09–15/11/2026**; hạn cuối **20:00 Chủ nhật 15/11**.
- Tuần 2: **16–22/11/2026**; hạn cuối **20:00 Chủ nhật 22/11**.
- Mọi thời gian **Asia/Saigon**. Nhiệm vụ có hạn sớm hơn ghi riêng để mở đường cho việc phụ thuộc.
- Hướng triển khai: **laptop của chủ trì làm máy chủ**; CI vẫn build/test. Không giao triển khai Render, mở tunnel/router hay runner trên laptop. Phạm vi mặc định của kế hoạch là loopback; truy cập LAN/Internet cần cấu hình và nghiệm thu riêng.
- Có bản self-host chưa commit trong workspace; người nhận tích hợp phải giữ provenance/tác giả, không nhận toàn bộ làm phần code mới của mình.

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

## Phạm vi phân công

**20 đầu việc thực hiện**, gồm **7 issue đang mở được bổ sung lịch**, **PR #77 được giao tiếp**, và **12 issue công việc mới**. Issue tổng hợp này không tính là một đầu việc của bất kỳ thành viên nào.

| Thành viên | Tuần 1 — 09–15/11 | Tuần 2 — 16–22/11 | Tổng / điểm |
|---|---|---|---|
| **Minh Sơn** @minhsonle30122007-png | [#46](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/46) Nghiệm thu cấu hình và quyền riêng tư cho bản self-host — **15/11**<br>[#93](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/93) Giới hạn thử đăng nhập và đăng ký email — **13/11** | [#53](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/53) Triển khai self-host tái lập trên laptop — **19/11**<br>[#103](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/103) Đồng bộ release notes và ma trận nghiệm thu — **21/11** | **4 việc / 9 điểm** |
| **Đình Hùng** @James-Lloyd20 | [#45](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/45) Nghiệm thu nguồn ngoài, upload và XSS trên self-host — **15/11**<br>[#94](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/94) Làm rõ dữ liệu gửi AI và quyền dùng giọng nói — **13/11** | [#51](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/51) Nghiệm thu accessibility và thao tác bàn phím — **21/11**<br>[#54](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/54) Correlation, log và đo lỗi trên bản self-host — **22/11** | **4 việc / 10 điểm** |
| **Phan Diễn** @phandien665-prog | [#77](https://github.com/tungtungsahul/MindSprint-AI-D-n/pull/77) Đo lại hiệu năng và hoàn thiện PR #77 — **15/11**<br>[#97](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/97) Bắt buộc kiểm tra bảo mật và PostgreSQL trước tích hợp — **14/11** | [#101](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/101) Kiểm chứng trình duyệt và PWA bằng bằng chứng thực — **21/11**<br>[#100](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/100) Hoàn thiện Chromium cho đọc URL JavaScript trong image self-host — **18/11** | **4 việc / 9 điểm** |
| **Tuấn Linh** @tuanlinh0511 | [#95](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/95) Sao lưu PostgreSQL của bản self-host — **14/11**<br>[#96](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/96) Kiểm chứng retry và ghi đồng thời trên PostgreSQL — **15/11** | [#102](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/102) Diễn tập restore và xác minh backup — **21/11**<br>[#99](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/99) Kiểm chứng migration nâng cấp và dữ liệu sau restart — **18/11** | **4 việc / 9 điểm** |
| **Bảo Duy** @tungtungsahul | [#98](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/98) Hoàn tất người thật đối chiếu 90 mẫu NFR-14 — **15/11** | [#104](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/104) Review tích hợp và chốt bản demo trên laptop — **22/11** | **2 việc / 4 điểm** |
| **Minh Chiến** @mchihihaha | [#47](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/47) Hoàn thiện PR #85 và nghiệm thu phục hồi khi mất mạng/AI lỗi — **15/11** | [#49](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/49) Readiness và theo dõi độ sẵn sàng trên laptop — **21/11** | **2 việc / 5 điểm** |

Mọi hạn trong bảng là **20:00 Asia/Saigon**. Điểm chỉ so sánh khối lượng tương đối (1 nhỏ, 2 vừa, 3 lớn), không phải chỉ tiêu commit hoặc điểm đánh giá cá nhân.

**Thứ tự tích hợp:** #93 và #46 phục vụ bảo mật; #95 → #102 cho backup/restore; #99 và #100 bàn giao 18/11 → #53 ngày 19/11 → #49 đo ít nhất 24 giờ; #46 → #54; #98/#102/#53 → #103; các kết quả cuối → #104. #96 phối hợp #47/PR #85, không tạo bản sửa idempotency cạnh tranh. #77 và #97 có thể chuẩn bị song song.

Bản phân công chưa có nghĩa công việc đã được thực hiện hoặc nghiệm thu.

## Quy tắc bàn giao và checkpoint

- Mỗi nhiệm vụ có một người chịu trách nhiệm chính, reviewer, phạm vi, tối đa 3 tiêu chí bổ sung, phụ thuộc và cách xác minh; giữ nguyên đầy đủ tiêu chí/nguỡng NFR gốc.
- Mỗi người cập nhật tiến độ tại issue của mình **trước 20:00 thứ Tư 11/11 và 18/11**, ghi đã làm/còn thiếu/blocker; gửi PR hoặc bằng chứng sớm đủ để review trước hạn.
- **Checkpoint tuần 1: 20:00 15/11** — kiểm tra 10 đầu việc tuần 1, kết quả bảo mật/retry/backup/AI, và chốt đầu vào bản self-host.
- **Checkpoint nền tảng: 20:00 19/11** — image/migration/self-host sẵn sàng, bắt đầu cửa sổ monitor 24h và kiểm thử cuối.
- **Checkpoint tuần 2: 20:00 22/11** — kiểm tra 10 đầu việc tuần 2; chốt SHA/image, restore/smoke và ma trận NFR có bằng chứng.
- Người thực hiện tự commit bằng danh tính thật, một thay đổi độc lập/review được; không commit rỗng, không tách vụn vô nghĩa, không sửa tác giả/backdate.
- PR ghi issue, SHA, lệnh kiểm tra/kết quả, môi trường và link artifact; tách mock/controller/SQLite với Kestrel/PostgreSQL/browser/AI thật.
- Thiếu thiết bị, quyền truy cập laptop, người chấm hoặc bằng chứng thì ghi **Blocked + phần đã chuẩn bị + người/điều kiện cần**, không tự giảm ngưỡng hoặc đóng issue là đạt.
- Reviewer không thay thế người thực hiện; chủ trì hỗ trợ tích hợp nhưng không làm thay hết để gom commit.


## Kiểm tra sau khi đăng

Đã đọc lại GitHub sau cập nhật: 20/20 đầu việc có assignee đúng, tuần/hạn và liên kết #92; 7 issue cũ giữ nguyên nội dung gốc trước phần lịch bổ sung. Cả 6 thành viên đều có việc ở cả hai tuần. GitHub Issues/PR là nguồn trạng thái chính thức; file này là snapshot lúc phân công, không đánh dấu công việc đã hoàn thành.
