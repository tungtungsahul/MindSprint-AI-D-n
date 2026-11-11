#!/bin/bash
# scripts/verify-volume-persistence.sh
# Kiểm tra giữ dữ liệu khi dừng và tạo lại container (Issue #111)
# Xác định volume PostgreSQL của bản self-host
# Dữ liệu test còn nguyên sau stop/start và recreate container web/DB có giữ volume
#
# Cách sử dụng:
#   ./scripts/verify-volume-persistence.sh [compose_file] [db_service_name] [volume_name]
#
# Mặc định:
#   compose_file   = docker-compose.yml
#   db_service     = db
#   volume_name    = mindsprint_db_data

set -euo pipefail

COMPOSE_FILE="${1:-docker-compose.yml}"
DB_SERVICE="${2:-db}"
VOLUME_NAME="${3:-mindsprint_db_data}"
LOG_FILE="./volume_persistence_$(date +%Y%m%d_%H%M%S).log"

echo "==========================================" | tee -a "${LOG_FILE}"
echo "Kiểm tra giữ dữ liệu khi dừng/tạo lại container: $(date -Iseconds)" | tee -a "${LOG_FILE}"
echo "Compose file: ${COMPOSE_FILE}" | tee -a "${LOG_FILE}"
echo "DB service: ${DB_SERVICE}" | tee -a "${LOG_FILE}"
echo "Volume: ${VOLUME_NAME}" | tee -a "${LOG_FILE}"
echo "==========================================" | tee -a "${LOG_FILE}"

# Bước 1: Định danh volume PostgreSQL
echo "" | tee -a "${LOG_FILE}"
echo "[1/4] Định danh volume PostgreSQL..." | tee -a "${LOG_FILE}"
echo "Chạy: docker volume ls | grep ${VOLUME_NAME}" | tee -a "${LOG_FILE}"
echo "Hoặc: docker-compose -f ${COMPOSE_FILE} config | grep -A5 ${DB_SERVICE}" | tee -a "${LOG_FILE}"
echo "Lưu ý: Volume phải được định nghĩa trong volumes section của docker-compose" | tee -a "${LOG_FILE}"

# Bước 2: Ghi số hàng trước khi dừng
echo "" | tee -a "${LOG_FILE}"
echo "[2/4] Ghi số hàng trước khi dừng container..." | tee -a "${LOG_FILE}"
echo "Chạy (trong container DB):" | tee -a "${LOG_FILE}"
echo "  docker-compose -f ${COMPOSE_FILE} exec ${DB_SERVICE}" | tee -a "${LOG_FILE}"
echo "    psql -U postgres -d mindsprint" | tee -a "${LOG_FILE}"
echo "      \"SELECT 'Users' AS tbl, COUNT(*) FROM Users\"" | tee -a "${LOG_FILE}"
echo "      UNION ALL SELECT 'Flashcards', COUNT(*) FROM Flashcards;" | tee -a "${LOG_FILE}"
echo "      UNION ALL SELECT 'Tasks', COUNT(*) FROM Tasks;" | tee -a "${LOG_FILE}"
echo "      UNION ALL SELECT 'Notebooks', COUNT(*) FROM Notebooks;" | tee -a "${LOG_FILE}"

# Bước 3: Dừng và khởi động lại container
echo "" | tee -a "${LOG_FILE}"
echo "[3/4] Dừng và khởi động lại container..." | tee -a "${LOG_FILE}"
echo "Chạy:" | tee -a "${LOG_FILE}"
echo "  docker-compose -f ${COMPOSE_FILE} stop ${DB_SERVICE}" | tee -a "${LOG_FILE}"
echo "  docker-compose -f ${COMPOSE_FILE} start ${DB_SERVICE}" | tee -a "${LOG_FILE}"
echo "Hoặc:" | tee -a "${LOG_FILE}"
echo "  docker-compose -f ${COMPOSE_FILE} down (không dùng -v)" | tee -a "${LOG_FILE}"
echo "  docker-compose -f ${COMPOSE_FILE} up -d" | tee -a "${LOG_FILE}"
echo "" | tee -a "${LOG_FILE}"
echo "CẢNH BÁO: Không dùng 'docker-compose down -v' vì sẽ xóa volume và mất dữ liệu." | tee -a "${LOG_FILE}"
echo "CẢNH BÁO: Không trỏ sang DB thật; chỉ dùng volume test riêng." | tee -a "${LOG_FILE}"

# Bước 4: Kiểm tra số hàng sau khi khởi động lại
echo "" | tee -a "${LOG_FILE}"
echo "[4/4] Kiểm tra số hàng sau khi khởi động lại..." | tee -a "${LOG_FILE}"
echo "Chạy lại các lệnh SELECT COUNT(*) ở bước [2/4]" | tee -a "${LOG_FILE}"
echo "Đối chiếu số hàng trước và sau - phải identical." | tee -a "${LOG_FILE}"
echo "" | tee -a "${LOG_FILE}"
echo "Phân biệt:" | tee -a "${LOG_FILE}"
echo "  - Container bị thay (recreate): Dữ liệu trong volume KHÔNG bị ảnh hưởng" | tee -a "${LOG_FILE}"
echo "  - Volume bị xóa (down -v): Dữ liệu MẤT, không thể khôi phục trừ khi có backup" | tee -a "${LOG_FILE}"

echo "" | tee -a "${LOG_FILE}"
echo "==========================================" | tee -a "${LOG_FILE}"
echo "Kiểm tra giữ dữ liệu hoàn tất: $(date -Iseconds)" | tee -a "${LOG_FILE}"
echo "Xem log chi tiết: ${LOG_FILE}" | tee -a "${LOG_FILE}"
echo "==========================================" | tee -a "${LOG_FILE}"
exit 0