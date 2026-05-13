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
1. **Orchestrator**: Ensure port `5001` (HTTP) or `5003` (HTTPS) is open in the firewall.
2. **Portal**: Update the `Portal:Orchestrator:ApiUrl` in `appsettings.json`:
   ```json
   "Portal": {
     "Orchestrator": {
       "ApiUrl": "https://orchestrator-server:5003"
     }
   }
   ```

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
