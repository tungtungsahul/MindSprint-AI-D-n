#!/bin/bash
# scripts/restore-postgres.sh
# Khôi phục backup PostgreSQL vào DB test riêng (Issue #102)
# Phục hồi backup từ Issue #95, kiểm tra count/checksum các bảng nghiệp vụ
#
# Yêu cầu:
#   - pg_restore có sẵn trong PATH
#   - PostgreSQL test đang chạy (DB riêng, không chạm volume demo/người dùng)
#   - File backup đã tạo từ scripts/backup-postgres.sh
#
# Cách sử dụng:
#   ./scripts/restore-postgres.sh <backup_file> [db_name] [db_user] [db_host] [db_port]
#
# Mặc định:
#   db_name   = mindsprint_test
#   db_user   = postgres
#   db_host   = localhost
#   db_port   = 5432

set -euo pipefail

BACKUP_FILE="${1:?Thiếu file backup. Cách dùng: ./scripts/restore-postgres.sh <backup_file> [db_name] [db_user] [db_host] [db_port]}"
DB_NAME="${2:-mindsprint_test}"
DB_USER="${3:-postgres}"
DB_HOST="${4:-localhost}"
DB_PORT="${5:-5432}"
CHECKSUM_FILE="${BACKUP_FILE}.sha256"
LOG_FILE="./restore_$(date +%Y%m%d_%H%M%S).log"
START_TIME="$(date +%s)"

# Kiểm tra file backup tồn tại
if [ ! -f "${BACKUP_FILE}" ]; then
    echo "LỖI: Không tìm thấy file backup: ${BACKUP_FILE}"
    exit 1
fi

# Kiểm tra checksum nếu có
if [ -f "${CHECKSUM_FILE}" ]; then
    echo "Kiểm tra checksum file backup..."
    EXPECTED="$(cat "${CHECKSUM_FILE}")"
    ACTUAL="$(sha256sum "${BACKUP_FILE}" | awk '{print $1}')"
    if [ "${EXPECTED}" != "${ACTUAL}" ]; then
        echo "LỖI: Checksum không khớp! File backup có thể bị hỏng."
        echo "  Mong đợi: ${EXPECTED}"
        echo "  Thực tế:  ${ACTUAL}"
        exit 1
    fi
    echo "Checksum OK: ${ACTUAL}"
else
    echo "Cảnh báo: Không tìm thấy file checksum, bỏ qua kiểm tra."
fi

echo "==========================================" | tee -a "${LOG_FILE}"
echo "Bắt đầu khôi phục PostgreSQL: $(date -Iseconds)" | tee -a "${LOG_FILE}"
echo "File backup: ${BACKUP_FILE}" | tee -a "${LOG_FILE}"
echo "Database đích: ${DB_NAME} @ ${DB_HOST}:${DB_PORT}" | tee -a "${LOG_FILE}"
echo "==========================================" | tee -a "${LOG_FILE}"

# Tạo DB mới nếu chưa có
echo "Tạo/kiểm tra database ${DB_NAME}..."
PGPASSWORD="${POSTGRES_PASSWORD:-}" createdb \
    -h "${DB_HOST}" -p "${DB_PORT}" -U "${DB_USER}" \
    --no-password "${DB_NAME}" 2>/dev/null || true

# Chạy pg_restore
if PGPASSWORD="${POSTGRES_PASSWORD:-}" pg_restore \
    -h "${DB_HOST}" \
    -p "${DB_PORT}" \
    -U "${DB_USER}" \
    -d "${DB_NAME}" \
    --no-password \
    --verbose \
    --clean \
    --if-exists \
    "${BACKUP_FILE}" 2>&1 | tee -a "${LOG_FILE}"; then

    echo "" | tee -a "${LOG_FILE}"
    echo "Restore thành công: $(date -Iseconds)" | tee -a "${LOG_FILE}"

    # Đo RTO (thời gian khôi phục thực)
    END_TIME="$(date +%s)"
    RTO_SECONDS=$((END_TIME - START_TIME))
    echo "Thời gian khôi phục (RTO): ${RTO_SECONDS} giây" | tee -a "${LOG_FILE}"

    echo "==========================================" | tee -a "${LOG_FILE}"
    echo "Khôi phục hoàn tất: ${DB_NAME}" | tee -a "${LOG_FILE}"
    echo "==========================================" | tee -a "${LOG_FILE}"
    exit 0
else
    echo "" | tee -a "${LOG_FILE}"
    echo "LỖI: pg_restore thất bại tại $(date -Iseconds)" | tee -a "${LOG_FILE}"
    echo "Xem log chi tiết: ${LOG_FILE}" | tee -a "${LOG_FILE}"
    exit 1
fi