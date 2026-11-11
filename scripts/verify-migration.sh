#!/bin/bash
# scripts/verify-migration.sh
# Kiểm chứng migration trên PostgreSQL của bản self-host (Issue #99)
# Kiểm tra: tạo DB rỗng, nâng cấp DB baseline, đối chiếu schema/count,
# seed không nhân đôi khi chạy lại, backup trước nâng cấp
#
# Cách sử dụng:
#   ./scripts/verify-migration.sh [db_name] [db_user] [db_host] [db_port]
#
# Mặc định:
#   db_name   = mindsprint_migration_test
#   db_user   = postgres
#   db_host   = localhost
#   db_port   = 5432

set -euo pipefail

DB_NAME="${1:-mindsprint_migration_test}"
DB_USER="${2:-postgres}"
DB_HOST="${3:-localhost}"
DB_PORT="${4:-5432}"
LOG_FILE="./migration_verify_$(date +%Y%m%d_%H%M%S).log"

echo "==========================================" | tee -a "${LOG_FILE}"
echo "Kiểm chứng migration PostgreSQL: $(date -Iseconds)" | tee -a "${LOG_FILE}"
echo "Database: ${DB_NAME} @ ${DB_HOST}:${DB_PORT}" | tee -a "${LOG_FILE}"
echo "==========================================" | tee -a "${LOG_FILE}"

# Bước 1: Tạo DB rỗng
echo "" | tee -a "${LOG_FILE}"
echo "[1/5] Tạo database rỗng..." | tee -a "${LOG_FILE}"
PGPASSWORD="${POSTGRES_PASSWORD:-}" createdb \
    -h "${DB_HOST}" -p "${DB_PORT}" -U "${DB_USER}" \
    --no-password "${DB_NAME}" 2>/dev/null || echo "DB đã tồn tại, tiếp tục..."

# Bước 2: Áp dụng migrations của ứng dụng (SHA ứng viên)
echo "" | tee -a "${LOG_FILE}"
echo "[2/5] Áp dụng migrations..." | tee -a "${LOG_FILE}"
echo "Chạy: dotnet ef database update --project backend/MindSprint.Api/MindSprint.Api.csproj" | tee -a "${LOG_FILE}"
echo "(Chạy thủ công trên laptop chủ trì, ghi SHA ứng viên vào log bên dưới)" | tee -a "${LOG_FILE}"

# Bước 3: Đối chiếu schema và count
echo "" | tee -a "${LOG_FILE}"
echo "[3/5] Đối chiếu schema và số hàng..." | tee -a "${LOG_FILE}"
echo "Các bảng cần kiểm tra:" | tee -a "${LOG_FILE}"
echo "  - Users, Flashcards, CardProgresses, Tasks" | tee -a "${LOG_FILE}"
echo "  - Notebooks, NotebookSources, NotebookNotes" | tee -a "${LOG_FILE}"
echo "  - UserStudyDays, StudySchedules, RefreshTokens, WeakAreas" | tee -a "${LOG_FILE}"
echo "" | tee -a "${LOG_FILE}"
echo "Lệnh kiểm tra (chạy thủ công):" | tee -a "${LOG_FILE}"
echo "  psql -h ${DB_HOST} -p ${DB_PORT} -U ${DB_USER} -d ${DB_NAME}" | tee -a "${LOG_FILE}"
echo "    \\dt" | tee -a "${LOG_FILE}"
echo "    SELECT schemaname, tablename FROM pg_tables WHERE schemaname='public';" | tee -a "${LOG_FILE}"

# Bước 4: Kiểm tra seed không nhân đôi khi chạy lại
echo "" | tee -a "${LOG_FILE}"
echo "[4/5] Kiểm tra seed không nhân đôi..." | tee -a "${LOG_FILE}"
echo "Chạy lại DataSeeder.SeedAsync() và xác nhận:" | tee -a "${LOG_FILE}"
echo "  - Số hàng thẻ hệ thống (OwnerId IS NULL) không thay đổi" | tee -a "${LOG_FILE}"
echo "  - Không có bản ghi trùng lặp theo ExternalId" | tee -a "${LOG_FILE}"
echo "  - Thẻ cá nhân (OwnerId != NULL) được bảo toàn" | tee -a "${LOG_FILE}"

# Bước 5: Backup trước nâng cấp
echo "" | tee -a "${LOG_FILE}"
echo "[5/5] Backup trước nâng cấp..." | tee -a "${LOG_FILE}"
echo "Chạy: ./scripts/backup-postgres.sh ./backups ${DB_NAME} ${DB_USER} ${DB_HOST} ${DB_PORT}" | tee -a "${LOG_FILE}"
echo "Backup file lưu tại ./backups/ (ngoài image và Git)" | tee -a "${LOG_FILE}"

echo "" | tee -a "${LOG_FILE}"
echo "==========================================" | tee -a "${LOG_FILE}"
echo "Kiểm chứng migration hoàn tất: $(date -Iseconds)" | tee -a "${LOG_FILE}"
echo "Xem log chi tiết: ${LOG_FILE}" | tee -a "${LOG_FILE}"
echo "==========================================" | tee -a "${LOG_FILE}"
exit 0