# Deployment and First-Run Setup

## 1. Deployment

The Portal is an ASP.NET Core 10 web application (`ETL-SQL-Portal`). It uses **SQLite** by
default for single-node deployments and **PostgreSQL** for load-balanced HA deployments. It serves
both the REST API and the static web UI from the same process.

### 1.1 Prerequisites

- .NET 10 Runtime
- Write access to the directories configured for the database, report scripts, and snapshots
- Network access to any data sources the report scripts query
- (Optional) ETL-SQL Orchestrator Service running on the same host or reachable through its HTTP API,
  for background dataset refresh and scheduled report/subscription work

### 1.2 Windows (NSSM)

```powershell
nssm install ETL-SQL-Portal "dotnet" "ETL-SQL-Portal.dll"
nssm set ETL-SQL-Portal AppDirectory "C:\ETL-SQL\Portal"
nssm set ETL-SQL-Portal AppStdout    "C:\Logs\portal.log"
nssm set ETL-SQL-Portal AppStderr    "C:\Logs\portal-error.log"
nssm set ETL-SQL-Portal AppEnvironmentExtra "ASPNETCORE_ENVIRONMENT=Production"
nssm start ETL-SQL-Portal
```

### 1.3 Linux (systemd)

```ini
[Unit]
Description=ETL-SQL Portal
After=network.target

[Service]
ExecStart=/opt/etlsql/portal/ETL-SQL-Portal
WorkingDirectory=/opt/etlsql/portal
Environment=ASPNETCORE_ENVIRONMENT=Production
Restart=always
User=etlportal

[Install]
WantedBy=multi-user.target
```

### 1.4 Reverse Proxy (Recommended for Production)

The portal listens on HTTP by default. Put it behind **nginx** or **IIS ARR** and terminate TLS at the proxy. Set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so the app sees the correct client IP in audit logs.

---


## 3. First-Run Setup

On first start the portal:

1. Runs any pending EF Core migrations, creating the configured Portal database schema.
2. Creates the three default roles: `Admin`, `Publisher`, `Viewer`.
3. If no `admin` user exists, creates one with username `admin`; by default it sets
   `MustChangePassword = true`. The
   temporary password comes from `Portal:FirstRun:AdminPassword` (or the `Portal__FirstRun__AdminPassword`
   environment variable). When unset, startup fails closed before creating the account; the Portal never
   writes a generated administrator password to application logs.

**You must log in as `admin` immediately and change the password.** The portal enforces this by default — no API calls will succeed until the password has been changed. `Portal:FirstRun:MustChangePassword=false` is intended only for disposable automation environments that generate a strong per-run admin password.

---
