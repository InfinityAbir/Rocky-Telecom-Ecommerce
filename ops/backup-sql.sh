#!/usr/bin/env bash
# Daily SQL Server backup (PRD NFR). Run from the repo root on the Docker host; schedule with cron:
#   15 2 * * *  cd /srv/rocky-telecom && ./ops/backup-sql.sh >> ops/backup.log 2>&1
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; . ./.env; set +a

stamp=$(date +%Y%m%d-%H%M%S)
file="/var/opt/mssql/backups/RockyTelecom-${stamp}.bak"

docker compose exec -T sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SQL_SA_PASSWORD" \
  -Q "BACKUP DATABASE [RockyTelecom] TO DISK = N'${file}' WITH COMPRESSION, CHECKSUM, INIT;"

# Prove the backup is readable before trusting it.
docker compose exec -T sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SQL_SA_PASSWORD" \
  -Q "RESTORE VERIFYONLY FROM DISK = N'${file}' WITH CHECKSUM;"

# Keep 14 days locally; copy ops/backups off the host as well.
find ops/backups -name 'RockyTelecom-*.bak' -mtime +14 -delete
echo "backup ok: ${file}"
