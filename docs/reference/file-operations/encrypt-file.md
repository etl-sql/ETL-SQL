# ENCRYPT / DECRYPT FILE

Encrypts or decrypts local files and staged datasets on disk using AES-256 or PGP. Also governs session-level credential encryption (`ENC:` strings) and secure password management.

---

## Syntax

### 1. File Encryption
```sql
ENCRYPT FILE '<source_file_path>' TO '<target_encrypted_path>' 
[WITH (
  DELETE_SOURCE = ON | OFF,
  ALGORITHM = 'AES256' | 'PGP',
  KEYFILE = '<public_key_path>',
  PASSWORD = '<passphrase>'
)];
```

### 2. File Decryption
```sql
DECRYPT FILE '<encrypted_file_path>' TO '<target_plaintext_path>'
[WITH (
  DELETE_SOURCE = ON | OFF,
  KEYFILE = '<private_key_path>',
  PASSWORD = '<passphrase>'
)];
```

---

## `ENC:` Encrypted Credentials & Session Passwords

Connection credentials and sensitive parameters can be stored as pre-encrypted blobs prefixed with `ENC:`. Setting a session password via `USE PASSWORD` unlocks these credentials in-memory at connection time:

```sql
-- Establish session decryption context
USE PASSWORD = 'SECRET:SessionMasterKey';

-- The engine automatically decrypts the ENC: blob during connection initialization
CREATE CONNECTION prod_db AS MSSQL(
    SERVER   = 'sql01.internal',
    DATABASE = 'Finance',
    PASSWORD = 'ENC:U2FsdGVkX1+abc123=='
);
```

---

## Examples

### 1. Symmetric File Encryption (AES-256)

```sql
USE PASSWORD = 'SECRET:FileEncryptionPassphrase';

-- Encrypt sensitive staging CSV and purge plaintext source
ENCRYPT FILE 'C:\staging\payroll.csv' 
TO 'C:\staging\payroll.csv.enc' 
WITH (DELETE_SOURCE = ON);

-- Decrypt back to plaintext when needed
DECRYPT FILE 'C:\staging\payroll.csv.enc' 
TO 'C:\staging\payroll.csv';
```

### 2. Production ETL: Secure Vendor Data Drop with Public Key Encryption

Extract sensitive partner records, stage to CSV, compress with GZIP, encrypt using the vendor's public PGP key, transmit via SFTP, and safely delete intermediate files:

```sql
CREATE CONNECTION dw        AS MSSQL(SERVER='dw.internal', DATABASE='analytics');
CREATE CONNECTION sftp_drop AS SFTP(HOST='sftp.partner.com', USER='partner_agent', KEYFILE='certs/sftp_id.rsa');

DECLARE @local_csv VARCHAR = 'C:\staging\partner_feed.csv';
DECLARE @local_gz  VARCHAR = 'C:\staging\partner_feed.csv.gz';
DECLARE @local_pgp VARCHAR = 'C:\staging\partner_feed.csv.gz.pgp';

BEGIN TRY
  -- 1. Extract and sanitize partner data
  SELECT customer_id, first_name, last_name, HASHBYTES('SHA256', ssn) AS ssn_hash, reward_balance
  INTO #partner_stage
  FROM dw.dbo.CustomerRewards
  WHERE is_active = 1;

  -- 2. Export table to CSV
  SELECT * INTO #export_view FROM #partner_stage;
  COPY FILE '#export_view' TO @local_csv;

  -- 3. Compress FIRST to maximize entropy reduction
  COMPRESS FILE @local_csv TO @local_gz WITH (FORMAT = 'GZIP');

  -- 4. Encrypt with partner's public PGP key and remove unencrypted archive
  ENCRYPT FILE @local_gz TO @local_pgp WITH (
    ALGORITHM = 'PGP',
    KEYFILE = 'certs/partner_public.asc',
    DELETE_SOURCE = ON
  );

  -- 5. Transmit encrypted archive to remote drop zone
  SEND FILE @local_pgp TO '/inbound/partner_feed.csv.gz.pgp' AT sftp_drop WITH (OVERWRITE = TRUE);

  PRINT 'Encrypted feed delivered successfully.';

  -- 6. Cleanup local files
  DELETE FILE @local_csv;
  DELETE FILE @local_pgp;
END TRY
BEGIN CATCH
  PRINT 'Secure vendor drop failed: ' + ERROR_MESSAGE();
  THROW;
END CATCH;
```

---

## Security Guardrails & Order of Operations

- **Order of Operations**: **Always compress BEFORE encrypting.** Compressing already-encrypted data is ineffective because cryptographic encryption maximizes entropy. The linter issues a warning if compression is executed after encryption.
- **In-Memory Password Isolation**: Session passwords set by `USE PASSWORD` are held strictly in memory and are automatically redacted from error traces, query plans, and audit outboxes.

---

## References & Related Recipes

- [File Operations Reference](README.md)
- [COMPRESS FILE](compress-file.md)
- [SEND FILE](send-file.md)
- [RECEIVE FILE](receive-file.md)
- [ETL Cookbook: Secure Vendor Handshake](../../cookbooks/etl/secure-vendor-handshake.md)
- [ETL Cookbook: Automated SFTP Bursting](../../cookbooks/etl/automated-sftp-bursting.md)
- [Syntax Index](../../syntax-index.md)
