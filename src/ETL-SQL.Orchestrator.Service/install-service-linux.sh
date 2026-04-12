#!/usr/bin/env bash
# Installs ETL-SQL-OrchestratorService as a systemd service on Linux.
# Run as root or with sudo.

set -euo pipefail

INSTALL_DIR="${1:-/opt/etl-sql/orchestrator}"
SERVICE_NAME="etl-sql-orchestrator"
SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"

echo "Publishing Orchestrator Service to ${INSTALL_DIR} ..."
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dotnet publish "${SCRIPT_DIR}/ETL-SQL.Orchestrator.Service.csproj" \
    -c Release -o "${INSTALL_DIR}" --self-contained false

echo "Writing systemd unit file to ${SERVICE_FILE} ..."
cat > "${SERVICE_FILE}" <<EOF
[Unit]
Description=ETL-SQL Orchestrator Service
After=network.target

[Service]
Type=notify
WorkingDirectory=${INSTALL_DIR}
ExecStart=${INSTALL_DIR}/ETL-SQL-OrchestratorService
Restart=on-failure
RestartSec=10
KillSignal=SIGTERM
SyslogIdentifier=${SERVICE_NAME}

# Run as a dedicated service account if one exists
#User=etl-sql
#Group=etl-sql

[Install]
WantedBy=multi-user.target
EOF

echo "Reloading systemd daemon ..."
systemctl daemon-reload
systemctl enable "${SERVICE_NAME}"
systemctl start  "${SERVICE_NAME}"

echo ""
echo "Service installed and started."
echo "  Status:  systemctl status ${SERVICE_NAME}"
echo "  Logs:    journalctl -u ${SERVICE_NAME} -f"
