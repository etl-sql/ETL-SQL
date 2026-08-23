# Troubleshooting: Connections, Credentials & Security Sandbox

This guide addresses authentication conflicts, credential encryption (`ENC:`), and zero-trust filesystem sandbox boundaries.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## 1. `CREATE CONNECTION` Fails with "Authentication Conflict"

### Problem
Executing `CREATE CONNECTION` throws `ConnectionAuthConflictException` or triggers linter rule `ConnectionAuthConflictRule`.

### Cause
Mutually exclusive authentication parameters were supplied in the connection declaration.

### Common Conflicts & Solutions
- **SQL Server (`MSSQL`)**:
  - `TRUSTED_CONNECTION=TRUE` combined with `USER=...` or `PASSWORD=...`.
  - *Fix*: Use either Windows Integrated Authentication (`TRUSTED_CONNECTION=TRUE`) OR SQL Authentication (`USER=...`, `PASSWORD=...`), never both.
- **SFTP (`SFTP`)**:
  - `KEY_FILE='...'` combined with `PASSWORD='...'`.
  - *Fix*: Choose either private key authentication OR password authentication.

```sql
-- ❌ CONFLICT:
CREATE CONNECTION bad_conn AS MSSQL(SERVER='sql01', DATABASE='Sales', TRUSTED_CONNECTION=TRUE, USER='sa');

-- ✓ CORRECT (SQL Auth):
CREATE CONNECTION good_conn AS MSSQL(SERVER='sql01', DATABASE='Sales', USER='sa', PASSWORD='SECRET:sql_pass');
```

---

## 2. Decrypting `ENC:` Strings at Runtime

### Problem
Connection fails with `DecryptionException` or `Invalid master password` when using an encrypted string (`'ENC:...'`).

### Cause
The script did not declare the active session master password before evaluating the `ENC:` credential.

### Solution
Supply `USE PASSWORD = '...'` before creating connections with encrypted strings:

```sql
-- 1. Set the session master password
USE PASSWORD = 'mySessionSecret';

-- 2. Encrypted string will now be decrypted transparently
CREATE CONNECTION secured_db AS MSSQL('ENC:U2FsdGVkX1+abc123...');
```

---

## 3. Environment-Specific Connection Switching (`CREATE SETS`)

### Problem
How do I configure a pipeline script to run against Dev, Staging, and Production databases without hardcoding passwords?

### Solution
Use `CREATE SETS` and `USE SETS` to define named configuration profiles:

```sql
CREATE SETS !DEV  BEGIN @server = 'dev-sql01',  @pwd = 'dev_pass'  END;
CREATE SETS !PROD BEGIN @server = 'prod-sql01', @pwd = 'SECRET:prod_pass' END;

-- Activate target environment
USE SETS !DEV;

CREATE CONNECTION db AS MSSQL(SERVER = @server, DATABASE = 'Sales', PASSWORD = @pwd);
```

---

## 4. `SecurityException`: Operation Outside Approved Safe Zone

### Problem
File operations (`COPY FILE`, `DELETE FILE`, `READ FLATFILE`) throw a `SecurityException: Path is outside approved safe zones`.

### Cause
The Zero-Trust Sandbox prevents scripts from operating on root directories (`C:\` or `/`), system paths (`/etc`, `C:\Windows`), or unregistered network shares.

### Solution
- Ensure all paths are explicit and inside approved project/data folders.
- For scripts processing large batch files (>100 files), declare `SET ALLOW_FILE_OPERATIONS = <n>` inside an approved directory:
  ```sql
  SET ALLOW_FILE_OPERATIONS = 500;
  
  FOREACH @file IN FILE_LIST('C:\Data\Inbound', '*.csv')
  BEGIN
      -- Process files within safe boundary
  END
  ```

---

## Related Topics

- [Script Resilience and Checkpoints](../pipelines/script-resilience-and-checkpoints.md) — Safe operations and transactions.
- [Security and Secret Management](../../administration/platform/secrets.md) — Secret provider configuration.
- [Data Connectors Reference](../../reference/connectors/README.md) — Connector options and authentication patterns.
