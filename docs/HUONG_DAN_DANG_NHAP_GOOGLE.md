# Bật đăng nhập bằng Google

Mã đã có sẵn (`Services/ExternalProviderConfig.cs`). Nút "Đăng nhập bằng Google" chỉ hiện khi app đọc được **Client ID** và **Client Secret**. Làm một lần trên Google Cloud, rồi đặt hai giá trị đó vào máy chạy app.

## 1. Tạo mã ứng dụng trên Google Cloud

1. Mở **https://console.cloud.google.com/** bằng tài khoản Google của nhóm → tạo project mới (ví dụ `MindSprint AI`).
2. Vào **Google Auth Platform** (https://console.cloud.google.com/auth/overview) → bấm **Get started**:
   - *App name*: MindSprint AI; *User support email*: email của bạn.
   - *Audience*: **External**.
   - Điền email liên hệ → đồng ý điều khoản của Google → **Create**.
3. **Audience** → mục *Test users* → **Add users** → thêm Gmail của các bạn sẽ thử (khi app còn ở chế độ *Testing*, chỉ những email này đăng nhập được).
4. **Clients** (https://console.cloud.google.com/auth/clients) → **Create client**:
   - *Application type*: **Web application**, *Name*: MindSprint MVC.
   - *Authorized redirect URIs* → thêm từng dòng:
     - `http://localhost:5144/signin-google` (Visual Studio)
     - `https://localhost:7014/signin-google` (Visual Studio, https)
     - `https://<tên-miền-server>/signin-google` (thêm khi đã triển khai lên server cá nhân)
   - **Create** → Google hiện **Client ID** và **Client Secret**.

> **Client ID** không bí mật (nó hiện trên trình duyệt khi đăng nhập). **Client Secret là bí mật**: không dán vào chat, không đưa lên GitHub.

## 2. Đặt mã vào máy để chạy thử

Mở terminal **của chính bạn** (không gõ trong cửa sổ chat với AI) tại thư mục gốc của repo (thư mục có tệp `MindSprintAI_ASP.NET_MVC.slnx`):

```bash
dotnet user-secrets set "Authentication:Google:ClientId" "<Client ID>" --project MindSprintAI_ASP.NET_MVC
dotnet user-secrets set "Authentication:Google:ClientSecret" "<Client Secret>" --project MindSprintAI_ASP.NET_MVC
```

Báo lỗi "Could not find a MSBuild project file" nghĩa là đang đứng sai thư mục hoặc thiếu `--project`.

User Secrets lưu ngoài thư mục project (trong hồ sơ người dùng Windows) nên không bị đưa lên GitHub. Chạy lại app → trang Đăng nhập có nút Google.

## 3. Trên server cá nhân

Đặt hai biến môi trường cho tiến trình chạy app (dấu `__` thay cho `:`):

```
Authentication__Google__ClientId=<Client ID>
Authentication__Google__ClientSecret=<Client Secret>
```

Nhớ thêm `https://<tên-miền-server>/signin-google` vào *Authorized redirect URIs* ở bước 1.4.

## 4. Thử

1. Trang Đăng nhập → **Đăng nhập bằng Google** → chọn một Gmail nằm trong *Test users*.
2. Với địa chỉ chưa đăng ký, đăng nhập Google tạo tài khoản MindSprint. Với tài khoản MindSprint đã xác nhận email, đăng nhập bằng cách cũ rồi chọn **Nối với Google** trong trang **Tài khoản**.
3. Với tài khoản MindSprint đang chờ xác nhận email, chủ địa chỉ đã được Google xác minh có thể hoàn tất xác nhận bằng Google. Hệ thống yêu cầu chấp nhận lại chính sách và hủy mật khẩu đã đặt trước khi xác nhận.
4. Khi Google báo `redirect_uri_mismatch`, kiểm tra lại giao thức, tên miền và cổng của địa chỉ ở bước 1.4.
