# Chia sẻ MindSprint qua Internet — SQL Server self-host

## Bật, xem link và tắt

```powershell
.\scripts\share.ps1
.\scripts\share.ps1 -Action Status
.\scripts\share.ps1 -Action Stop
```

Start build/khởi động web, bảo vệ file secret, bật Cloudflare Quick Tunnel, cập nhật
origin HTTPS và kiểm tra `/readyz` qua link Internet. Share Status đọc link của lần
khởi động tunnel hiện tại; Share Stop chỉ tắt tunnel, giữ web localhost và SQL Server.
Link hiện tại được ghi trong `.public.selfhost.json`, được Git bỏ qua.

## Địa chỉ ngắn không cần mua tên miền

Địa chỉ đã xuất bản: **https://msai.smokycrow18.chatgpt.site**.
Cổng Sites chuyển tiếp website/API về tunnel đang chạy. Thanh địa chỉ giữ hostname
ngắn; giao diện, backend, API key và SQL Server vẫn ở laptop. Không chuyển database
sang dịch vụ cloud. Mọi người vẫn đăng ký/đăng nhập bằng tài khoản MindSprint.

Cấu hình gateway nằm trong repository Sites riêng tại `sites/mindsprint-gateway/`.
Worker không có API key Gemini; chỉ nhận biến runtime `UPSTREAM_ORIGIN` là link tunnel.
Nó không chuyển cookie/thông tin tài khoản ChatGPT sang backend, vẫn chuyển JWT của
tài khoản MindSprint và đặt `Cache-Control: no-store` để tránh cache dữ liệu cá nhân.
Phiên đăng nhập của hostname cũ không tự chuyển sang địa chỉ mới, nên cần đăng nhập lại.

Địa chỉ ngắn không đổi khi tunnel restart, nhưng nếu Cloudflare cấp link tunnel mới,
cần cập nhật `UPSTREAM_ORIGIN` trong cấu hình runtime Sites rồi triển khai lại phiên bản
đã lưu. Có thể yêu cầu Codex “cập nhật link ngắn MindSprint” để đồng bộ từ
`.public.selfhost.json`; project ID được lưu tại `sites/mindsprint-gateway/.openai/hosting.json`.
Script Share phát hiện khác biệt và nhắc cập nhật, không báo link ngắn đã đồng bộ khi chưa cập nhật.
Máy laptop/Docker/tunnel vẫn phải hoạt động để website dùng được.

Native Sites được dùng để cấp hostname và xuất bản gateway; không thực hiện mua tên miền
hoặc bật gói trả phí. Google Sign-In cần thêm hostname ngắn vào Authorized JavaScript
origins trong tài khoản Google của chủ OAuth client; email/mật khẩu không cần bước đó.

Gửi link HTTPS cho người khác. Họ đăng ký/đăng nhập bằng email và mật khẩu.
Máy chủ và Docker cần chạy, máy không Sleep. Link có thể đổi sau khi tunnel restart.
Quick Tunnel dùng để chia sẻ thử, không có cam kết uptime; muốn địa chỉ cố định cần
named tunnel và tên miền thuộc quyền quản lý của bạn.

## API key và IP máy chủ

- Người truy cập kết nối tới hostname của Cloudflare. Không tạo DNS trỏ về IP gốc,
  không mở router, không bind cổng web ra LAN/Internet.
- Cổng web chỉ bind `127.0.0.1:8080`; SQL Server không publish cổng ra host.
- Cloudflared mở kết nối đi ra Cloudflare. Service này không nhận mount secret.
- Khóa Gemini nằm trong `.secrets.selfhost/gemini-api-key`, được gắn chỉ đọc tại
  `/run/secrets/gemini_api_key` cho backend. Khóa không ở frontend, URL hay environment container.
- Start tự chuyển khóa cũ khỏi dòng `GEMINI_API_KEY` trong `.env.selfhost`. Khóa có
  thể được nhập vào file secret bằng trình soạn thảo local; không gửi qua chat hoặc Git.
- Trên Windows, file secret và `.env.selfhost` chỉ cấp quyền cho tài khoản chạy
  script và SYSTEM; thư mục secret cũng được giới hạn quyền trước khi ghi khóa.
- Header outbound của Gemini được che trong log HttpClient. Lỗi AI không trả nguyên
  văn exception nội bộ; Production có phản hồi lỗi chung và không hiện Swagger.
- Frontend gọi API cùng origin của trang HTTPS, không cần biết IP máy chủ.
- Google Client ID được gửi công khai theo thiết kế OAuth; nó khác API key bí mật Gemini.

Đây không phải cam kết bảo mật tuyệt đối. Cloudflare biết IP kết nối đi ra và làm
trung gian HTTPS; Google biết IP của backend khi backend gọi Gemini. Người có quyền
quản trị Windows/Docker có thể đọc secret. Không gửi `.env.selfhost`, thư mục secret
hoặc backup cho người truy cập. Khóa từng bị lộ phải được thu hồi/thay tại nhà cung cấp.

## Google và AI

Đăng nhập email/mật khẩu hoạt động qua link. Để dùng Google Sign-In ngoài máy chủ,
chủ OAuth client cần thêm đúng origin HTTPS vào Authorized JavaScript origins trong
Google Cloud Console. Backend đã cho phép origin mà Share Start ghi vào `PUBLIC_ORIGIN`;
việc này không tự thay cấu hình tài khoản Google. Khi link đổi, cập nhật Google tương ứng.

Người dùng đã đăng nhập có thể gọi tính năng AI của ứng dụng bằng khóa backend.
Không thấy khóa không đồng nghĩa không thể tiêu thụ quota qua ứng dụng; theo dõi quota
và giới hạn chi phí tại Google AI Studio/Cloud Console theo tài khoản của bạn.

## Kiểm chứng

```powershell
python tests/public-security.py
python tests/selfhost-smoke.py 'https://LINK-HIEN-TAI.trycloudflare.com'
# Tùy chọn: một yêu cầu Gemini thực với văn bản mẫu.
python tests/selfhost-smoke.py 'https://LINK-HIEN-TAI.trycloudflare.com' --ai
```

Kiểm tra public-security không in hoặc gửi khóa Gemini ra ngoài. Nó kiểm tra file
riêng bị chặn, asset/header không có khóa, secret readonly, API cùng origin và cổng
host chỉ mở loopback. Nó không thay cho kiểm toán bảo mật toàn diện hoặc thử tấn công.

Nguồn: [Cloudflare Quick Tunnels](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/),
[Docker Compose secrets](https://docs.docker.com/compose/how-tos/use-secrets/).
