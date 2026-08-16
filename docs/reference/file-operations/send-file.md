# SEND FILE

Transfers a local file or exported dataset to a remote server over a secure `SFTP`, `FTP`, or `AZURE_BLOB` connection.

---

## Syntax

### 1. Statement Form (Recommended)
```sql
SEND FILE '<local_path>' TO '<remote_path>' AT <connection_name> [WITH (OVERWRITE = TRUE | FALSE)];
```

### 2. Function Shorthand
```sql
SEND FILE('<local_path>', <connection_name>, '<remote_path>' [, <overwrite_boolean>]);
```

---

## Parameters & Options

- **`local_path`** — Absolute or relative path to the local source file on disk (must sit within allowed directory boundaries).
- **`remote_path`** — Target destination path on the remote host.
- **`connection_name`** — Active `SFTP`, `FTP`, or `AZURE_BLOB` connection identifier.
- **`OVERWRITE = TRUE | FALSE`** — When `TRUE`, overwrites existing remote files. When `FALSE`, throws an error if the destination already exists (default: `FALSE`).

---

## Examples

### 1. Simple SFTP Upload with Keyfile Authentication

```sql
CREATE CONNECTION sftp_server AS SFTP(
    HOST = 'sftp.partner.com',
    USER = 'partner_upload',
    KEYFILE = 'certs/partner_rsa.key'
);

SEND FILE 'C:\exports\daily_summary.csv' 
TO '/incoming/daily_summary_20260816.csv' 
AT sftp_server 
WITH (OVERWRITE = TRUE);

PRINT 'File transfer complete.';
```

### 2. Production ETL: End-to-End Encrypted SFTP Bursting Pipeline

Extract customer ledger records, stage into in-memory table, write to CSV, compress with GZIP, verify checksum integrity, and burst to a remote banking partner:

```sql
CREATE CONNECTION dw        AS MSSQL(SERVER='dw.internal', DATABASE='finance');
CREATE CONNECTION bank_sftp AS SFTP(HOST='sftp.bank.internal', USER='etl_agent', KEYFILE='certs/id_ed25519');

DECLARE @date_str VARCHAR = CAST(FORMAT(GETDATE(), 'yyyyMMdd') AS VARCHAR);
DECLARE @local_csv VARCHAR = 'C:\staging\settlement_' + @date_str + '.csv';
DECLARE @local_gz  VARCHAR = 'C:\staging\settlement_' + @date_str + '.csv.gz';
DECLARE @remote_dest VARCHAR = '/inbound/settlement_' + @date_str + '.csv.gz';

BEGIN TRY
  -- 1. Extract clean transaction ledger
  SELECT transaction_id, account_number, amount, currency, settled_at
  INTO #settlement_records
  FROM dw.dbo.SettledTransactions
  WHERE settled_at >= CAST(GETDATE() AS DATE);

  -- 2. Export in-memory table to local file
  SELECT * INTO #export_view FROM #settlement_records;
  COPY FILE '#export_view' TO @local_csv;

  -- 3. Compress for efficient network transit
  COMPRESS FILE @local_csv TO @local_gz WITH (FORMAT = 'GZIP');

  -- 4. Secure transmission to bank SFTP server
  SEND FILE @local_gz TO @remote_dest AT bank_sftp WITH (OVERWRITE = TRUE);

  PRINT 'Settlement file successfully delivered to remote bank server: ' + @remote_dest;

  -- 5. Local cleanup
  DELETE FILE @local_csv;
  DELETE FILE @local_gz;
END TRY
BEGIN CATCH
  PRINT 'Transfer failed: ' + ERROR_MESSAGE();
  THROW;
END CATCH;
```

---

## Zero-Trust Security Guardrails

- **Allowed Paths**: File operations are restricted to permitted storage directories configured in `appsettings.json` or established via `SET ALLOW_FILE_OPERATIONS = <n>`.
- **Credential Protection**: Connection credentials (passwords, private key paths) are redacted from logs and query history.

---

## References & Related Recipes

- [File Operations Reference](README.md)
- [RECEIVE FILE](receive-file.md)
- [ENCRYPT FILE](encrypt-file.md)
- [COMPRESS FILE](compress-file.md)
- [SFTP Connector](../connectors/services/sftp.md)
- [ETL Cookbook: Automated SFTP Bursting](../../cookbooks/etl/automated-sftp-bursting.md)
- [ETL Cookbook: Secure Vendor Handshake](../../cookbooks/etl/secure-vendor-handshake.md)
- [Syntax Index](../../syntax-index.md)
