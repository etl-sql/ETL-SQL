# ENCRYPT / DECRYPT
Encrypts or decrypts files on disk. Also covers `ENC:` credential values and session password management.

## ENCRYPT FILE
```sql
-- Encrypt using the active session password
ENCRYPT FILE 'output/sensitive.csv' TO 'output/sensitive.csv.enc';

-- Encrypt and delete the plaintext source
ENCRYPT FILE 'output/sensitive.csv' TO 'output/sensitive.csv.enc' WITH (
  DELETE_SOURCE = ON
);
```

## DECRYPT FILE
```sql
DECRYPT FILE 'output/sensitive.csv.enc' TO 'output/sensitive.csv';
```

## ENC: credential values
Connection passwords can be stored as pre-encrypted blobs. The `ENC:` prefix tells the engine to decrypt the value using the active session password at connect time.
```sql
USE PASSWORD = 'my-passphrase';

CREATE CONNECTION ProdDB AS MSSQL (
  SERVER   = 'sql-prod',
  DATABASE = 'Sales',
  PASSWORD = ENC:U2FsdGVkX1+abc123==
);
```

## Generating an ENC: value
```bash
# From the CLI
etlsql encrypt --password "my-passphrase" --value "db-password-here"
# Output: ENC:U2FsdGVkX1+abc123==
```

## Notes
- `USE PASSWORD` must be called before any statement that reads an `ENC:` value or an ENCRYPTED/SENSITIVE variable.
- The session password is held in memory only and is never logged or written to disk.
- Encryption uses AES-256. The `ENC:` blob includes a salt; the same plaintext produces different blobs each time.
- See: USE, CREATE CONNECTION, DECLARE