# SET MAX_STRING_RESULT_SIZE
Sets the maximum length in bytes allowed for string results.

## Syntax
```sql
SET MAX_STRING_RESULT_SIZE = <n>;
```

## Parameters
- **n** — Maximum bytes. Default: 104,857,600 (100 MB).

## Example
```sql
-- Allow larger string results for XML processing
SET MAX_STRING_RESULT_SIZE = 209715200;

SET @largeXml = (SELECT xml_content FROM #documents WHERE id = 1);
```

## Notes
- A safety limit to prevent excessive memory consumption from very large string values.
- Corresponding `appsettings.json` key: `Security:MaxStringResultSize`.
- Default: 104,857,600 bytes (100 MB).

## References
- [SET Commands](README.md)
