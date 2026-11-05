#!/usr/bin/env bash
# Restore a backup into a SCRATCH database to prove it works (never over production without a plan).
#   ./ops/restore-sql.sh RockyTelecom-20261105-021500.bak
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; . ./.env; set +a

name="${1:?usage: restore-sql.sh <backup-file-name>}"
src="/var/opt/mssql/backups/${name}"
sql="docker compose exec -T sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P $SQL_SA_PASSWORD"

$sql -Q "RESTORE DATABASE [RockyTelecom_restoretest] FROM DISK = N'${src}' WITH REPLACE,
  MOVE 'RockyTelecom' TO '/var/opt/mssql/data/RockyTelecom_restoretest.mdf',
  MOVE 'RockyTelecom_log' TO '/var/opt/mssql/data/RockyTelecom_restoretest.ldf';"
$sql -Q "SELECT 'products' AS t, COUNT(*) AS n FROM [RockyTelecom_restoretest].dbo.Products
         UNION ALL SELECT 'orders', COUNT(*) FROM [RockyTelecom_restoretest].dbo.Orders;"
echo "restore test ok. Drop it with: DROP DATABASE [RockyTelecom_restoretest]"
