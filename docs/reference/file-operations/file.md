# FILE Operations
File-level management commands for copying, moving, renaming, deleting, compressing, encrypting, and decrypting individual files.

Syntax:
```text
COPY     FILE 'src_path' TO 'dest_path' [WITH (OVERWRITE = ON|OFF, DATE_SUFFIX = 'format', SUFFIX_SEPARATOR = '_')];
COPY     FILE 'src_path' TO DIRECTORY 'dest_dir' [WITH (OVERWRITE = ON|OFF, DATE_SUFFIX = 'format', SUFFIX_SEPARATOR = '_')];
MOVE     FILE 'src_path' TO 'dest_path' [WITH (OVERWRITE = ON|OFF, DATE_SUFFIX = 'format', SUFFIX_SEPARATOR = '_')];
MOVE     FILE 'src_path' TO DIRECTORY 'dest_dir' [WITH (OVERWRITE = ON|OFF, DATE_SUFFIX = 'format', SUFFIX_SEPARATOR = '_')];
RENAME   FILE 'old_path' TO 'new_name';
DELETE   FILE 'path';
COMPRESS FILE 'src_path' TO 'dest.zip';
ENCRYPT  FILE 'src_path' TO 'dest_path' PASSWORD('passphrase');
DECRYPT  FILE 'src_path' TO 'dest_path' PASSWORD('passphrase');
```

Options:
- **OVERWRITE** - Replaces an existing destination file when `ON`.
- **DATE_SUFFIX** - Appends today's date to the destination file name before the extension using a .NET date format such as `yyyyMMdd`.
- **SUFFIX_SEPARATOR** - Separator placed between the base file name and `DATE_SUFFIX`; defaults to `_`.

```sql
-- Archive today's report as C:\archive\daily_20260722.csv on July 22, 2026
COPY FILE 'C:\reports\daily.csv'
TO DIRECTORY 'C:\archive'
WITH (DATE_SUFFIX = 'yyyyMMdd');

-- Archive yesterday's report with explicit naming when a different date is needed
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
- [Specialized Operations](../../administration/platform/README.md)
