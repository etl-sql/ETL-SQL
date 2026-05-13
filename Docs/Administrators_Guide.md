# ETL-SQL Administrator's Guide

Welcome to the ETL-SQL Enterprise Suite. This guide covers the installation, configuration, and maintenance of ETL-SQL in production environments.

---

## 1. Architecture Overview

ETL-SQL is a distributed platform consisting of three primary components:
- **Workstation SDK**: CLI/TUI tools used by developers to author scripts.
- **Orchestrator Service**: A background service that schedules and executes jobs across the network.
- **Report Portal**: A web-based dashboard for managing reports and viewing snapshots.

---

## 2. Production Installation

### Windows
1. Run the `ETL-SQL-Enterprise-v0.6.0.msi` installer.
2. Select the features you wish to install.
3. The services will be registered as `ETL-SQL-Orchestrator` and `ETL-SQL-Portal`.
4. Services are configured to start automatically under the `LocalSystem` account.

### Linux
1. Install the `.deb` or `.rpm` package:
   ```bash
   sudo dpkg -i etl-sql_0.6.0_amd64.deb
   ```
2. Enable and start the services:
   ```bash
   sudo systemctl enable etl-sql-orchestrator
   sudo systemctl start etl-sql-orchestrator
   sudo systemctl enable etl-sql-portal
   sudo systemctl start etl-sql-portal
   ```

---

## 3. Security & Secret Management

ETL-SQL uses a "Zero-Trust" configuration model. Sensitive values (passwords, JWT secrets) are stored encrypted in the `appsettings.json` file.

### 3.1 Encrypting Secrets
To encrypt a string for use in configuration:
```bash
ETL-SQL encrypt "my-secret-password" --pass "YourMasterKey"
```
Or use the machine-bound key (recommended for local services):
```bash
ETL-SQL encrypt "my-secret-password"
```

### 3.2 Setting up the JWT Secret
The Report Portal requires a secure 256-bit JWT secret. You can generate and install one automatically:
```bash
ETL-SQL config setup-jwt --update
```
> [!CAUTION]
> Record the plain-text secret printed by this command in a secure location (e.g., a password manager). It cannot be recovered from the encrypted configuration if the machine key is lost.

---

## 4. HTTPS & SSL Configuration

Both the Orchestrator and Portal support HTTPS via Kestrel.

### 4.1 Configuring Certificates
Update your `appsettings.json` (or `appsettings.Production.json`):
```json
"Kestrel": {
  "Endpoints": {
    "Https": {
      "Url": "https://*:5002",
      "Certificate": {
        "Path": "C:\\Certs\\etl-sql.pfx",
        "Password": "ENC:ENCRYPTED_PFX_PASSWORD"
      }
    }
  }
}
```

---

## 5. Multi-Node Networking

If the Report Portal and Orchestrator are running on different servers:
1. **Orchestrator**: Ensure port `5100` (HTTP) or the configured HTTPS port is open in the firewall.
2. **Portal**: Configure the Orchestrator URL in one of two ways:

   *Via `appsettings.json` or environment variable (applied at startup):*
   ```json
   "Portal": {
     "Orchestrator": {
       "ApiUrl": "http://orchestrator-server:5100",
       "ApiKey": "your-shared-secret"
     }
   }
   ```
   Or as environment variables:
   ```
   Portal__Orchestrator__ApiUrl=http://orchestrator-server:5100
   Portal__Orchestrator__ApiKey=your-shared-secret
   ```

   *Via the Admin UI (applied immediately, no restart needed):*
   Log in as Admin → **Admin → Settings → Orchestrator Connection** → enter the URL and API key → **Save**. The portal writes a `portal-orchestrator.json` sidecar file alongside the portal database. UI-saved values take precedence over environment variables.

### 5.1 Orchestrator API Key

In production, protect the Orchestrator's management HTTP endpoints with a shared API key. The portal sends the key in an `X-Orchestrator-Key` request header on every proxied call.

**On the Orchestrator Service** (`appsettings.json` or environment variable):
```json
{
  "Orchestrator": {
    "ApiKey": "your-shared-secret",
    "ScriptRoot": "/opt/etl/scripts"
  }
}
```
Or: `Orchestrator__ApiKey=your-shared-secret`

`ScriptRoot` is the directory the portal's job-creation script browser is scoped to. It defaults to the Orchestrator's working directory if not set.

If `ApiKey` is left empty the management endpoints are open — acceptable only on isolated internal networks.

### 5.2 Same-Host Start

If the Portal and Orchestrator run on the **same Windows host**, the portal can use the Windows `ServiceController` API to start the Orchestrator service when it is offline. Enable this with:

```json
"Portal": {
  "Orchestrator": {
    "SameHost": true
  }
}
```

When `SameHost = true`, a **Start** button appears in the Orchestrator tab when the service is offline. On separate-server deployments leave `SameHost = false` (the default); the portal will prompt the operator to start the service on its host machine.

### 5.3 OrchestratorManager Role

The Orchestrator tab in the Report Portal is gated by the `OrchestratorAccess` policy (`Admin` OR `OrchestratorManager`). Assign `OrchestratorManager` to operations staff who need to schedule and monitor jobs but should not have access to the full Admin panel (users, groups, audit log).

Assign the role via **Admin → Users → Edit User → Role: Orchestrator Manager**. See the [Report Portal Administrator's Guide](./ReportPortal_Administrators_Guide.md) for full details.

---

## 6. Maintenance & Backup

### 6.1 Database Backups
- **Portal DB**: Located at `data/portal.db` (SQLite). Simply copy this file while the service is stopped or use `VACUUM INTO` for online backups.
- **Orchestrator DB**: Located at `data/orchestrator.db`.

### 6.2 Logs
Logs are stored by default in:
- Windows: `%PROGRAMFILES%\ETL-SQL\logs`
- Linux: `/var/log/etl-sql/`

---

## 7. Session Management & Notebooks

ETL-SQL provides a specialized **Interactive Mode** designed for iterative development in VS Code Notebooks or REPL environments.

### 7.1 Interactive Mode (`SET INTERACTIVE_MODE`)
When enabled, the engine modifies its behavior to support re-runnable script cells:
- **Idempotent DDL**: `CREATE CONNECTION` and `CREATE DATASET` statements behave as `CREATE OR ALTER`. This prevents "Object already exists" errors when re-executing a cell.
- **Clean Expansion**: Column expansion (`SELECT *`) prioritizes non-aliased names to reduce noise in interactive data exploration.
- **Default State**:
    - **VS Code Notebooks**: Automatically enabled (`ON`).
    - **CLI/Orchestrator**: Automatically disabled (`OFF`).

To manually toggle this behavior in a script:
```sql
SET INTERACTIVE_MODE ON;
```
