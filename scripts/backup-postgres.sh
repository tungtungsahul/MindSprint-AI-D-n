#!/bin/bash
# scripts/backup-postgres.sh
# Sao lưu PostgreSQL của bản self-host (Issue #95)
# Chạy trên laptop chủ trì, mặc định http://127.0.0.1:8080
#
# Yêu cầu:
#   - pg_dump có sẵn trong PATH hoặc trong container DB
#   - Docker Compose đang chạy dịch vụ postgres
#   - Thư mục backup nằm ngoài image và Git (không commit vào repo)
#
# Cách sử dụng:
#   ./scripts/backup-postgres.sh [backup_dir] [db_name] [db_user] [db_host] [db_port]
#
# Mặc định:
#   backup_dir   = ./backups
#   db_name      = mindsprint
#   db_user      = postgres
#   db_host      = localhost
#   db_port      = 5432

set -euo pipefail

BACKUP_DIR="${1:-./backups}"
DB_NAME="${2:-mindsprint}"
DB_USER="${3:-postgres}"
DB_HOST="${4:-localhost}"
DB_PORT="${5:-5432}"
TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
BACKUP_FILE="${BACKUP_DIR}/mindsprint_db_${TIMESTAMP}.sql"
CHECKSUM_FILE="${BACKUP_FILE}.sha256"
LOG_FILE="${BACKUP_DIR}/backup_${TIMESTAMP}.log"

# Tạo thư mục backup nếu chưa có (nằm ngoài image và Git)
mkdir -p "${BACKUP_DIR}"

echo "==========================================" | tee -a "${LOG_FILE}"
echo "Bắt đầu sao lưu PostgreSQL: $(date -Iseconds)" | tee -a "${LOG_FILE}"
echo "Database: ${DB_NAME} @ ${DB_HOST}:${DB_PORT}" | tee -a "${LOG_FILE}"
echo "File xuất: ${BACKUP_FILE}" | tee -a "${LOG_FILE}"
echo "==========================================" | tee -a "${LOG_FILE}"

# Chạy pg_dump
if PGPASSWORD="${POSTGRES_PASSWORD:-}" pg_dump \
    -h "${DB_HOST}" \
    -p "${DB_PORT}" \
    -U "${DB_USER}" \
    -d "${DB_NAME}" \
    --no-password \
    --verbose \
    --format=custom \
    --file="${BACKUP_FILE}" 2>&1 | tee -a "${LOG_FILE}"; then

    echo "" | tee -a "${LOG_FILE}"
    echo "Dump thành công: $(date -Iseconds)" | tee -a "${LOG_FILE}"

    # Tính checksum SHA-256
    sha256sum "${BACKUP_FILE}" | awk '{print $1}' > "${CHECKSUM_FILE}"
    echo "Checksum: $(cat "${CHECKSUM_FILE}")" | tee -a "${LOG_FILE}"
    echo "File checksum: ${CHECKSUM_FILE}" | tee -a "${LOG_FILE}"

    # Kiểm tra file backup có kích thước > 0
    if [ ! -s "${BACKUP_FILE}" ]; then
        echo "LỖI: File backup rỗng sau khi dump!" | tee -a "${LOG_FILE}"
        exit 1
    fi

    echo "==========================================" | tee -a "${LOG_FILE}"
    echo "Sao lưu hoàn tất: ${BACKUP_FILE}" | tee -a "${LOG_FILE}"
    echo "==========================================" | tee -a "${LOG_FILE}"
    exit 0
else
    echo "" | tee -a "${LOG_FILE}"
    echo "LỖI: pg_dump thất bại tại $(date -Iseconds)" | tee -a "${LOG_FILE}"
    echo "Xem log chi tiết: ${LOG_FILE}" | tee -a "${LOG_FILE}"
    exit 1
fi