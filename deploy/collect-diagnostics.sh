#!/usr/bin/env bash
# Gói chẩn đoán MindSprint AI trên server (systemd + nginx + Cloudflare Tunnel + SQL Server Docker) — tương đương
# scripts/collect-diagnostics.ps1 của repo nhóm (issue #115) cho cách triển khai của project mới.
#   bash collect-diagnostics.sh              -> ~/mindsprint-diagnostics/<thời-gian>/ + .tar.gz (không cần sudo)
#   bash collect-diagnostics.sh --self-test  -> thêm bí mật giả (canary), kiểm không lọt vào gói
#   sudo bash collect-diagnostics.sh         -> thêm trạng thái container SQL Server (docker cần quyền root)
# Chỉ ĐỌC trạng thái: không khởi động lại, không đổi cấu hình. KHÔNG đọc /etc/mindsprint/*.env; che mật khẩu/khóa/token/email
# trong mọi tệp; mỗi dòng log tối đa 500 ký tự.
set -uo pipefail
DOMAIN=${DOMAIN:-mindsprint.rekal.id.vn}
SERVICE=${SERVICE:-mindsprint}
APP_PORT=${APP_PORT:-5002}
NGINX_PORT=${NGINX_PORT:-8082}
SQL_CONTAINER=${SQL_CONTAINER:-rekal-sql}
LINES=${LINES_PER_LOG:-300}
ROOT=${OUTPUT_ROOT:-$HOME/mindsprint-diagnostics}
OUT="$ROOT/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$OUT" && chmod 700 "$ROOT" "$OUT"
HINTS=()

mask() {   # cùng quy tắc với bản PowerShell; chuỗi chỉ gồm hex thường (SHA, ID image) được giữ
  perl -pe '
    s/("?(?:[\w.:-]*?(?:password|passwd|pwd|secret|token|api[_-]?key|apikey|jwt[_-]?key|[_:.-]key|client[_-]?secret|connectionstring[s]?(?:__|:)?)\w*)"?\s*[:=]\s*"?)([^";\s,}]+)/$1***/gi;
    s/AIza[0-9A-Za-z_\-]{20,}/AIza***/g;
    s/eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+/<jwt-da-che>/g;
    s/\bbearer\s+[A-Za-z0-9\-._~+\/]+=*/Bearer ***/gi;
    s/[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}/<email-da-che>/g;
    s/\b(?![0-9a-f]{40,}\b)[A-Za-z0-9+\/_\-]{40,}={0,2}/<chuoi-dai-da-che>/g;
    s/^(.{500}).+$/$1 ...[cat]/;
  '
}
save() { mask > "$OUT/$1"; }   # mọi tệp đi qua mask trước khi ghi

{ echo "Thời điểm: $(date -Is)"; echo "Máy: $(hostname) · $(. /etc/os-release; echo "$PRETTY_NAME") · $(uname -r)"; echo "Chạy từ: $(uptime -p)"
  echo ".NET: $(dotnet --list-runtimes 2>/dev/null | grep AspNetCore | tr '\n' ' ')"
  echo "Bản app: $(stat -c '%y' /var/www/mindsprint/MindSprintAI_ASP.NET_MVC.dll 2>/dev/null || echo 'không thấy') · sha256 dll: $(sha256sum /var/www/mindsprint/MindSprintAI_ASP.NET_MVC.dll 2>/dev/null | cut -c1-16)"
  echo "Tên miền: $DOMAIN · dịch vụ: $SERVICE · app 127.0.0.1:$APP_PORT · nginx 127.0.0.1:$NGINX_PORT"; } | save meta.txt

for s in "$SERVICE" nginx cloudflared docker; do
  printf '%s: %s · từ %s · khởi động lại %s lần\n' "$s" "$(systemctl is-active "$s" 2>&1)" \
    "$(systemctl show "$s" -p ActiveEnterTimestamp --value 2>/dev/null)" "$(systemctl show "$s" -p NRestarts --value 2>/dev/null)"
done | save services.txt
[[ "$(systemctl is-active "$SERVICE")" == active ]] || HINTS+=("Dịch vụ $SERVICE không chạy: xem logs-app.txt; khởi động: sudo systemctl start $SERVICE")
[[ "$(systemctl is-active cloudflared)" == active ]] || HINTS+=("cloudflared không chạy: người ngoài không vào được tên miền; sudo systemctl start cloudflared")

ss -ltnp 2>/dev/null | awk 'NR==1 || /:('"$APP_PORT"'|'"$NGINX_PORT"'|1433|80) /' | save ports.txt
grep -q ":$APP_PORT " "$OUT/ports.txt" || HINTS+=("Không có gì nghe cổng $APP_PORT: app chưa chạy hoặc lỗi lúc khởi động (xem logs-app.txt)")
grep -q ":$NGINX_PORT " "$OUT/ports.txt" || HINTS+=("nginx không nghe cổng $NGINX_PORT: sudo nginx -t rồi sudo systemctl reload nginx")

{ for p in /healthz /readyz; do
    echo "qua nginx  $p -> $(curl -s -m 5 -o /dev/null -w '%{http_code}' -H "Host: $DOMAIN" "http://127.0.0.1:$NGINX_PORT$p")"
    echo "thẳng app  $p -> $(curl -s -m 5 -o /dev/null -w '%{http_code}' -H "Host: $DOMAIN" "http://127.0.0.1:$APP_PORT$p")"
  done
  echo "từ Internet /healthz -> $(curl -s -m 10 -o /dev/null -w '%{http_code}' "https://$DOMAIN/healthz")"; } | save http.txt
grep -q "qua nginx  /readyz -> 503" "$OUT/http.txt" && HINTS+=("/readyz = 503: app chạy nhưng không nối được SQL Server (xem logs-app.txt, sql.txt)")
grep -q "từ Internet /healthz -> 200" "$OUT/http.txt" || HINTS+=("Từ Internet không vào được: kiểm Cloudflare Tunnel (Public hostname $DOMAIN -> HTTP 127.0.0.1:$NGINX_PORT)")

{ df -h / | tail -1; free -h | head -2; } | save resources.txt
journalctl -u "$SERVICE" -n "$LINES" --no-pager -o cat 2>&1 | save logs-app.txt
journalctl -u cloudflared -n 80 --no-pager -o cat 2>&1 | save logs-cloudflared.txt
tail -n 100 /var/log/nginx/error.log 2>&1 | save logs-nginx-error.txt
grep -qi 'Login failed for user' "$OUT/logs-app.txt" && HINTS+=("Log app có 'Login failed': mật khẩu SQL trong /etc/mindsprint/app-db.env không khớp")
grep -qiE 'address already in use' "$OUT/logs-app.txt" && HINTS+=("Cổng $APP_PORT bị chương trình khác chiếm (xem ports.txt)")
grep -qE 'App:BaseUrl|Turnstile đang trống|AllowedHosts' "$OUT/logs-app.txt" && HINTS+=("Thiếu cấu hình bắt buộc khi chạy thật: sudo nano /etc/mindsprint/mindsprint.env")

if docker ps >/dev/null 2>&1; then
  docker ps -a --filter "name=$SQL_CONTAINER" --format '{{.Names}} · {{.Status}} · image {{.Image}}' | save sql.txt
  docker logs --tail 60 "$SQL_CONTAINER" 2>&1 | save logs-sql.txt
  grep -q 'Up' "$OUT/sql.txt" || HINTS+=("Container $SQL_CONTAINER không chạy: sudo docker start $SQL_CONTAINER")
else
  echo "Không có quyền docker (chạy bằng sudo để xem container $SQL_CONTAINER)" | save sql.txt
fi

if [[ "${1:-}" == --self-test ]]; then
  id=$(head -c 6 /dev/urandom | od -An -tx1 | tr -d ' \n')
  canaries=("CanaryPass-$id" "AIzaSyCANARY$id$id" "canary-jwt-$id" "canary.$id@example.com" "$id$id$id$id")
  { echo "ConnectionStrings__Default=Server=127.0.0.1,1433;Database=MindSprintAI;User Id=mindsprint_app;Password=${canaries[0]};TrustServerCertificate=True"
    echo "DataProtection__CertificatePassword=${canaries[4]}"
    echo "Turnstile__SecretKey=${canaries[0]} key=${canaries[1]}"
    echo "{\"token\":\"${canaries[2]}\",\"refreshToken\":\"${canaries[2]}\"}"
    echo "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.$id"
    echo "Smtp__UserName=${canaries[3]} Người dùng ${canaries[3]} đăng nhập"; } | save canary-log.txt
  leaks=0; for c in "${canaries[@]}"; do grep -rqF -- "$c" "$OUT" && leaks=$((leaks + 1)); done
  if (( leaks )); then echo "SELF-TEST FAIL: $leaks bí mật giả còn trong gói"; HINTS+=("SELF-TEST FAIL"); else echo "SELF-TEST PASS: ${#canaries[@]} bí mật giả đều đã bị che trong $(ls "$OUT" | wc -l) tệp"; fi
fi

{ echo "Gói chẩn đoán MindSprint AI ($DOMAIN). Đã che mật khẩu/khóa/token/email; không chứa tệp .env."; echo
  if (( ${#HINTS[@]} )); then echo "Gợi ý nguyên nhân:"; printf ' - %s\n' "${HINTS[@]}"; else echo "Không phát hiện lỗi thường gặp."; fi; } | save summary.txt
tar -czf "$OUT.tar.gz" -C "$ROOT" "$(basename "$OUT")" && chmod 600 "$OUT.tar.gz"
echo "Đã tạo: $OUT.tar.gz"; cat "$OUT/summary.txt"
[[ " ${HINTS[*]-} " == *"SELF-TEST FAIL"* ]] && exit 1 || exit 0
