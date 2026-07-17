# SET ALLOW_FILE_OPERATIONS
Overrides the runaway file-operation protection limit for the current session.

## Syntax
```sql
SET ALLOW_FILE_OPERATIONS = <n>;
```

## Parameters
- **n** — Maximum file operations allowed. Default: 100.

## Example
```sql
-- Allow more file operations for a bulk file processing script
SET ALLOW_FILE_OPERATIONS = 500;

FOREACH @file IN #file_list
BEGIN
    COPY FILE @file.source TO @file.dest;
END;
```

## Notes
- Produces an audit entry. The path must be within a Safe Zone.
- Corresponding `appsettings.json` key: `Security:MaxFileOperationsPerScript`.
- Alias: `SET MAX_FILE_OPERATIONS = n`.
- Default: 100.

## References
- [SET Commands](README.md)
