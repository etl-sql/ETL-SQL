# COMPRESS / DECOMPRESS
Compresses or decompresses files and directories using ZIP or GZIP format.

## COMPRESS FILE
```sql
COMPRESS FILE 'output/report.csv' TO 'output/report.csv.gz';

-- ZIP with explicit format
COMPRESS FILE 'output/report.csv' TO 'output/report.zip' WITH (
  FORMAT = ZIP
);
```

## COMPRESS DIRECTORY
```sql
COMPRESS DIRECTORY 'output/' TO 'output/archive.zip' WITH (
  FORMAT    = ZIP,
  RECURSE   = ON
);
```

## DECOMPRESS
```sql
DECOMPRESS FILE 'downloads/data.csv.gz' TO 'staging/data.csv';

DECOMPRESS FILE 'downloads/archive.zip' TO 'staging/';
```

## Options
| Option | Values | Default |
|---|---|---|
| FORMAT | ZIP \| GZIP | GZIP for single files, ZIP for directories |
| RECURSE | ON \| OFF | ON |
| OVERWRITE | ON \| OFF | OFF |

## Notes
- GZIP is for single-file compression; ZIP supports multiple files and directories.
- `DECOMPRESS FILE` to a directory path extracts all contents into that directory.
- Compressed files can be used directly with EXPORT by pointing to `.gz` destinations on supported connections (e.g., S3, SFTP).
- See: COPY, EXPORT, ENCRYPT

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
