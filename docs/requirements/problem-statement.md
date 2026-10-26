# Phát biểu bài toán — MindSprintAI

**Ngày lập:** 26/10/2026. **Phạm vi đối chiếu:** đặc tả yêu cầu MindSprintAI 0.1.

## 1. Bối cảnh và vấn đề cần giải quyết

Trong quá trình tự học, học sinh, sinh viên và người tự học cần thực hiện nhiều công việc liên quan: thu thập tài liệu, hệ thống hóa kiến thức, giải đáp nội dung chưa hiểu, ôn tập và sắp xếp thời gian học. Tài liệu có thể đến từ văn bản, trang web, hình ảnh hoặc tệp PDF; trong khi ghi chú, thẻ ôn tập, lịch học và danh sách công việc có thể được lưu ở các công cụ riêng biệt.

Khi các hoạt động này thiếu sự liên kết, người học có thể gặp những khó khăn sau:

- **Khó chuyển tài liệu thành nội dung có thể ôn tập:** phải tự chọn lọc ý chính, viết câu hỏi, tạo thẻ và tổ chức ghi chú.
- **Khó duy trì việc ôn tập có lịch trình:** chưa xác định rõ nội dung cần ôn và thời điểm ôn tiếp theo dựa trên kết quả học trước đó.
- **Khó giải đáp thắc mắc gắn với tài liệu:** cần tìm lại đoạn liên quan, đối chiếu câu trả lời và lưu nội dung hữu ích để sử dụng về sau.
- **Khó theo dõi và điều chỉnh quá trình học:** thông tin về tiến độ, chủ đề còn yếu, lịch học và công việc chưa được tập hợp trong cùng một quy trình.

Đây là những tình huống sử dụng làm cơ sở xác định bài toán của dự án; mức độ phổ biến và ảnh hưởng của chúng cần được đánh giá thêm với người dùng thực tế.

## 2. Phát biểu bài toán

**Xây dựng một ứng dụng web hỗ trợ học tập cá nhân, cho phép học sinh, sinh viên và người tự học chuyển tài liệu của mình thành nội dung học có cấu trúc; hỏi đáp và nhận hỗ trợ theo tài liệu; ôn tập theo lịch dựa trên kết quả học; đồng thời quản lý công việc, lịch học và theo dõi tiến độ trong một hệ thống thống nhất.**

Hệ thống cần kết nối được chu trình **tiếp nhận tài liệu → tổ chức kiến thức → học và ôn tập → ghi nhận kết quả → xác định nội dung cần củng cố**, đồng thời giúp người học chủ động sắp xếp các hoạt động tiếp theo. Dữ liệu cá nhân phải được quản lý theo đúng tài khoản; khi xảy ra lỗi mạng hoặc dịch vụ AI, hệ thống cần thông báo rõ trạng thái xử lý và bảo toàn nội dung trong phạm vi được hỗ trợ.

## 3. Đối tượng sử dụng, đầu vào và đầu ra

**Đối tượng sử dụng chính:** học sinh, sinh viên và người tự học có nhu cầu tổ chức tài liệu, ôn tập và quản lý hoạt động học tập cá nhân. Người học có thể sử dụng chế độ cục bộ trong phạm vi hỗ trợ hoặc đăng nhập để sử dụng dữ liệu trên máy chủ.

| Thành phần | Nội dung |
| --- | --- |
| Đầu vào về kiến thức | Văn bản, URL công khai, hình ảnh, tệp tài liệu và thẻ học do người dùng cung cấp, theo định dạng và giới hạn được hỗ trợ |
| Đầu vào trong quá trình học | Câu hỏi, bài làm, câu trả lời hoặc mức đánh giá khi ôn thẻ; ghi chú, công việc và lịch học do người dùng tạo |
| Đầu ra phục vụ học tập | Tài liệu được tổ chức trong sổ tay; câu trả lời theo nguồn kèm trích dẫn khi phù hợp; tóm tắt, thẻ học, trắc nghiệm, sơ đồ tư duy và gợi ý của gia sư AI |
| Đầu ra phục vụ theo dõi | Danh sách thẻ đến hạn, tiến độ ôn tập, thống kê ngày học, chủ đề cần củng cố, bảng công việc và lịch học cá nhân |

## 4. Mục tiêu và phạm vi giải quyết

MindSprintAI hướng tới giảm công sức chuẩn bị nội dung ôn tập, hỗ trợ ôn tập đều đặn bằng phương pháp lặp lại ngắt quãng và giúp người học theo dõi quá trình học để lựa chọn nội dung cần củng cố. Những lợi ích này là mục tiêu sản phẩm, cần được kiểm chứng qua đánh giá sử dụng thực tế.

Trong phạm vi MVP, bài toán được giải quyết thông qua các nhóm chức năng:

1. **Tài khoản và dữ liệu cá nhân:** đăng ký, đăng nhập và quản lý dữ liệu theo tài khoản.
2. **Thẻ học và ôn tập:** tạo hoặc nhập thẻ, hỗ trợ tạo thẻ bằng AI, ôn tập theo lịch và ghi nhận tiến độ.
3. **Sổ tay và hỗ trợ học bằng AI:** quản lý nguồn tài liệu, hỏi đáp theo nguồn, tạo nội dung ôn tập, lưu ghi chú và nhận hướng dẫn từ gia sư AI.
4. **Tổ chức hoạt động học:** quản lý công việc bằng Kanban và thiết lập lịch học thủ công.
5. **Theo dõi quá trình học:** xem thống kê, chuỗi ngày học và gợi ý chủ đề cần củng cố từ dữ liệu tiến độ.
6. **Học cục bộ và đồng bộ:** tiếp tục sử dụng dữ liệu học đã lưu trên thiết bị và đồng bộ thẻ, ngày học trong phạm vi được đặc tả.

Chức năng AI đề xuất kế hoạch học thuộc yêu cầu mục tiêu FR-23, nhưng chưa được xác nhận triển khai tại mốc đối chiếu của đặc tả 0.1; lịch học thủ công không thay thế yêu cầu này.

Phạm vi MVP không bao gồm quản lý lớp học hoặc vai trò giáo viên/quản trị, thanh toán, cộng tác sổ tay, thực thi mã nguồn của người học, ứng dụng di động native hay thông báo email/push theo lịch. Các chức năng AI cần kết nối mạng và dịch vụ được cấu hình; hệ thống không bảo đảm mọi chức năng đều hoạt động hoặc đồng bộ khi ngoại tuyến.

## 5. Kết quả cần đạt và căn cứ đánh giá

Kết quả cần đạt là một ứng dụng web trong đó người học có thể hoàn thành chu trình từ đưa tài liệu vào hệ thống, tạo và sử dụng nội dung học, đến lưu tiến độ và quản lý công việc, lịch học của mình. Nội dung do AI sinh cần có thể đối chiếu với nguồn khi phù hợp và được người học kiểm tra trước khi sử dụng.

Việc đánh giá cần dựa trên khả năng hoàn thành các luồng sử dụng, tính đúng đắn và khả năng lưu/đọc lại dữ liệu, sự cô lập dữ liệu giữa các tài khoản, cùng cách hệ thống xử lý lỗi. Tiêu chí chức năng, chất lượng và bằng chứng kiểm thử được quản lý trong các tài liệu dưới đây:

- [Đặc tả yêu cầu phần mềm 0.1](software-requirements.md): phạm vi, yêu cầu chức năng, tiêu chí nghiệm thu và các điểm chưa nghiệm thu.
- [Yêu cầu phi chức năng](../architecture/non-functional-requirements.md): các mục tiêu chất lượng và phương pháp đo.
- [Báo cáo dự án](../report/BAO_CAO_MINDSPRINT.md): tầm nhìn MVP và mô tả hệ thống.
- [Kết quả kiểm thử ban đầu](../testing/sprint-3-results.md): bằng chứng đã có và giới hạn của kết quả kiểm thử.
