# FILE Operations
File-level management commands for copying, moving, renaming, deleting, compressing, encrypting, and decrypting individual files.

Syntax:
  COPY     FILE 'src_path' TO 'dest_path';
  MOVE     FILE 'src_path' TO 'dest_path';
  RENAME   FILE 'old_path' TO 'new_name';
  DELETE   FILE 'path';
  COMPRESS FILE 'src_path' TO 'dest.zip';
  ENCRYPT  FILE 'src_path' TO 'dest_path' PASSWORD('passphrase');
  DECRYPT  FILE 'src_path' TO 'dest_path' PASSWORD('passphrase');

```sql
-- Archive yesterday's report
DECLARE @src VARCHAR = 'C:\reports\daily.csv';
DECLARE @arc VARCHAR = 'C:\archive\daily_' + FORMAT(DATEADD(DAY,-1,GETDATE()),'yyyyMMdd') + '.csv';

COPY FILE @src TO @arc;
COMPRESS FILE @arc TO @arc + '.gz';
DELETE FILE @arc;

-- Encrypt a sensitive export before transfer
ENCRYPT FILE 'C:\export\payroll.csv' TO 'C:\export\payroll.enc' PASSWORD(@enc_key);
SEND FILE 'C:\export\payroll.enc' TO 'secure/payroll.enc' AT PartnerSFTP;
DELETE FILE 'C:\export\payroll.enc';
```

References:
- [Specialized Operations](../../../../../Docs/Reference/Specialized_Operations.md)
