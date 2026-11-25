#!/usr/bin/env bash
# Đưa MindSprint AI lên server Debian đang chạy Rekal (cùng cách: .NET trên máy + systemd + nginx + Cloudflare Tunnel),
# dùng chung container SQL Server "rekal-sql" nhưng database + tài khoản SQL riêng. Không sửa gì của Rekal.
#   Cùng thư mục cần có: mindsprint-app.tar.gz, efbundle (tạo: xem docs/TRIEN_KHAI_SERVER.md, phần "Server đã có Rekal").
#   Chạy: sudo bash setup-mindsprint.sh     — chạy lại được: lần sau chỉ thay bản build + chạy migration mới, giữ App_Data và cấu hình.
set -euo pipefail
[[ $EUID -eq 0 ]] || { echo "Chạy bằng sudo"; exit 1; }
cd "$(dirname "$(readlink -f "$0")")"

DOMAIN=mindsprint.rekal.id.vn
APP=/var/www/mindsprint
CONF=/etc/mindsprint
PORT=5002          # Kestrel (Rekal 5001, quiz 5000)
NGINX_PORT=8082    # nginx (Rekal 8081) — Cloudflare Tunnel trỏ vào đây
DB=MindSprintAI
LOGIN=mindsprint_app
genpw() { echo "$(head -c 16 /dev/urandom | od -An -tx1 | tr -d ' \n')Ms-"; }  # 32 hex + chữ hoa + ký hiệu (đủ luật mật khẩu SQL)
SQLCMDPASSWORD=$(sed -n 's/^MSSQL_SA_PASSWORD=//p' /etc/rekal/sql.env); export SQLCMDPASSWORD
q() { rekal-sqlcmd -S localhost -U sa -C -b -h -1 -W -Q "SET NOCOUNT ON; $1"; }
[[ -f mindsprint-app.tar.gz && -f efbundle ]] || { echo "Thiếu mindsprint-app.tar.gz hoặc efbundle trong thư mục này."; exit 1; }

echo "== 1/8 Tài khoản 'mindsprint' chạy web và thư mục"
id mindsprint >/dev/null 2>&1 || useradd --system --home-dir "$APP" --no-create-home --shell /usr/sbin/nologin mindsprint
install -d -m 755 "$APP"
install -d -m 700 -o mindsprint -g mindsprint "$APP/App_Data"
install -d -m 750 -o mindsprint -g mindsprint /var/log/mindsprint
install -d -m 750 -o root -g mindsprint "$CONF"   # web đọc được dp.pfx; các file .env chỉ root đọc
# Bài làm theo đề: đề Word được đổi sang PDF bằng LibreOffice (không giao diện). Phông Liberation/Carlito/Caladea
# đo chữ giống Times New Roman/Arial/Calibri/Cambria nên đề Word giữ đúng bố cục.
if ! command -v soffice >/dev/null 2>&1; then
  apt-get update -qq
  apt-get install -y --no-install-recommends libreoffice-writer-nogui fonts-liberation2 fonts-crosextra-carlito fonts-crosextra-caladea fonts-dejavu-core
fi

echo "== 2/8 Chép bản build (web chỉ đọc code, chỉ ghi vào App_Data)"
systemctl stop mindsprint 2>/dev/null || true
find "$APP" -mindepth 1 -maxdepth 1 ! -name App_Data -exec rm -rf {} +
tar -xzf mindsprint-app.tar.gz -C "$APP" --no-same-owner --exclude=./App_Data
chown -R root:root "$APP" && chmod 755 "$APP" && chown -R mindsprint:mindsprint "$APP/App_Data"
# Bản đồ vị trí đăng nhập (GeoIP) của Rekal: chép sang nếu có, thiếu thì app vẫn chạy (chỉ không hiện thành phố của máy lạ).
if [[ ! -d "$APP/App_Data/geoip" && -d /var/www/rekal/App_Data/geoip ]]; then
  cp -r /var/www/rekal/App_Data/geoip "$APP/App_Data/" && chown -R mindsprint:mindsprint "$APP/App_Data/geoip"
fi

echo "== 3/8 Chứng chỉ mã hóa thư mục khóa (Data Protection)"
if [[ ! -f $CONF/dp.pfx ]]; then
  tmp=$(mktemp -d); DPPW=$(genpw); export DPPW
  openssl req -x509 -newkey rsa:3072 -nodes -days 3650 -subj "/CN=MindSprint DataProtection" -keyout "$tmp/k" -out "$tmp/c" 2>/dev/null
  openssl pkcs12 -export -inkey "$tmp/k" -in "$tmp/c" -out $CONF/dp.pfx -passout env:DPPW
  rm -rf "$tmp"
  ( umask 077; printf 'DataProtection__CertificatePath=%s/dp.pfx\nDataProtection__CertificatePassword=%s\n' "$CONF" "$DPPW" > $CONF/dp.env )
fi
chown root:mindsprint $CONF/dp.pfx && chmod 640 $CONF/dp.pfx

echo "== 4/8 Database $DB + tài khoản SQL riêng $LOGIN (không dùng sa, không đụng database của Rekal)"
if [[ ! -f $CONF/app-db.env ]]; then
  apppw=$(genpw)
  q "IF DB_ID('$DB') IS NULL CREATE DATABASE [$DB];"
  q "IF SUSER_ID('$LOGIN') IS NULL CREATE LOGIN [$LOGIN] WITH PASSWORD = '$apppw', DEFAULT_DATABASE = [$DB]
     ELSE ALTER LOGIN [$LOGIN] WITH PASSWORD = '$apppw';"
  q "USE [$DB]; IF USER_ID('$LOGIN') IS NULL CREATE USER [$LOGIN] FOR LOGIN [$LOGIN]; ALTER ROLE db_owner ADD MEMBER [$LOGIN];"
  ( umask 077; echo "ConnectionStrings__Default=Server=127.0.0.1,1433;Database=$DB;User Id=$LOGIN;Password=$apppw;TrustServerCertificate=True;MultipleActiveResultSets=True;Max Pool Size=300" > $CONF/app-db.env )
fi

echo "== 5/8 Tạo/cập nhật bảng"
chmod 700 efbundle
./efbundle --connection "$(sed -n 's/^ConnectionStrings__Default=//p' $CONF/app-db.env)"
echo "Số migration đã áp: $(q "SELECT COUNT(*) FROM [$DB].dbo.__EFMigrationsHistory")"
# Như Rekal: lệnh đọc không phải chờ người đang ghi (bật lúc web đang dừng ở bước 2).
q "IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = '$DB') = 0 ALTER DATABASE [$DB] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;"

echo "== 6/8 Cấu hình (dùng lại Turnstile, email Admin, thông tin pháp lý và Gmail của Rekal — chép trên máy, không in ra)"
if [[ ! -f $CONF/mindsprint.env ]]; then
  ( umask 077; {
    echo "ASPNETCORE_ENVIRONMENT=Production"
    echo "ASPNETCORE_URLS=http://127.0.0.1:$PORT"
    echo "App__BaseUrl=https://$DOMAIN"
    echo "AllowedHosts=$DOMAIN"
    echo "Passkeys__ServerDomain=$DOMAIN"
    echo "Logs__Directory=/var/log/mindsprint"
    grep -hE '^(Turnstile__(SiteKey|SecretKey)|Admin__Emails__[0-9]+|Legal__[A-Za-z]+)=' /etc/rekal/rekal.env || true
    echo "# Sửa: sudo nano $CONF/mindsprint.env rồi sudo systemctl restart mindsprint"
  } > $CONF/mindsprint.env )
fi
if [[ ! -f $CONF/secrets.env ]]; then
  ( umask 077; grep -hE '^Smtp__[A-Za-z]+=' /etc/rekal/rekal.env /etc/rekal/secrets.env 2>/dev/null > $CONF/secrets.env || true )
  echo "# Đăng nhập Google (không bắt buộc): bỏ # rồi điền — xem docs/HUONG_DAN_DANG_NHAP_GOOGLE.md mục 3" >> $CONF/secrets.env
  echo "#Authentication__Google__ClientId=" >> $CONF/secrets.env
  echo "#Authentication__Google__ClientSecret=" >> $CONF/secrets.env
fi
echo "Đã có: Turnstile=$(grep -c '^Turnstile__' $CONF/mindsprint.env) dòng, Admin=$(grep -c '^Admin__' $CONF/mindsprint.env) dòng, Gmail=$(grep -c '^Smtp__' $CONF/secrets.env) dòng"

echo "== 7/8 Dịch vụ systemd 'mindsprint'"
cat > /etc/systemd/system/mindsprint.service <<EOF
[Unit]
Description=MindSprint AI
After=network-online.target docker.service time-sync.target
Wants=network-online.target time-sync.target

[Service]
User=mindsprint
Group=mindsprint
WorkingDirectory=$APP
ExecStart=$(command -v dotnet) $APP/MindSprintAI_ASP.NET_MVC.dll
EnvironmentFile=$CONF/mindsprint.env
EnvironmentFile=$CONF/app-db.env
EnvironmentFile=$CONF/dp.env
EnvironmentFile=-$CONF/secrets.env
Environment=DOTNET_NOLOGO=1
Restart=always
RestartSec=10
KillSignal=SIGINT
TimeoutStopSec=30
UMask=0027
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=true

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable --now mindsprint

echo "== 8/8 nginx ($NGINX_PORT -> $PORT), giống Rekal"
cat > /etc/nginx/sites-available/mindsprint <<EOF
# MindSprint AI: Cloudflare Tunnel -> nginx (127.0.0.1:$NGINX_PORT) -> app (127.0.0.1:$PORT)
map \$http_upgrade \$mindsprint_connection_upgrade {   # SignalR dùng WebSocket
    default upgrade;
    ''      close;
}
server {
    listen 127.0.0.1:$NGINX_PORT;
    server_name $DOMAIN;
    # Địa chỉ thật của khách: cloudflared gửi trong CF-Connecting-IP; chỉ tin khi đến từ chính máy này.
    set_real_ip_from 127.0.0.1;
    real_ip_header CF-Connecting-IP;
    client_max_body_size 25m;   # tệp đề/bài làm theo đề tối đa 20 MB (các form khác app tự giới hạn 12 MB)
    location / {
        proxy_pass http://127.0.0.1:$PORT;
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Forwarded-For \$remote_addr;   # ghi đè: app chỉ tin 1 lớp proxy
        proxy_set_header X-Forwarded-Proto https;          # khách luôn vào qua HTTPS của Cloudflare
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection \$mindsprint_connection_upgrade;
        proxy_read_timeout 150s;    # AI tạo nội dung dài chờ tới 120 giây
    }
}
EOF
ln -sf /etc/nginx/sites-available/mindsprint /etc/nginx/sites-enabled/mindsprint
nginx -t && systemctl reload nginx

echo "== Kiểm tra"
code=000
for i in $(seq 1 45); do
  code=$(curl -s -o /dev/null -w '%{http_code}' -H "Host: $DOMAIN" -H 'CF-Connecting-IP: 203.0.113.7' http://127.0.0.1:$NGINX_PORT/ || true)
  [[ "$code" =~ ^[23] ]] && break; sleep 2
done
echo "Trang chủ qua nginx: HTTP $code"
echo "Sẵn sàng (database): $(curl -s -H "Host: $DOMAIN" http://127.0.0.1:$NGINX_PORT/readyz || true)"
systemctl is-active mindsprint || true
journalctl -u mindsprint -n 15 --no-pager -o cat | cut -c1-220 || true
echo "== XONG. Việc còn lại trên trang Cloudflare: Tunnel -> Public hostname $DOMAIN -> HTTP 127.0.0.1:$NGINX_PORT; Turnstile -> thêm $DOMAIN vào widget."
