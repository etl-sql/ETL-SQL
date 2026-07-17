# SET REGEX_MATCH_TIMEOUT
Sets the execution duration cap in milliseconds for regex evaluations to prevent denial-of-service from catastrophic backtracking.

## Syntax
```sql
SET REGEX_MATCH_TIMEOUT = <n>;
```

## Parameters
- **n** — Timeout in milliseconds. Default: 1,000.

## Example
```sql
-- Increase timeout for complex patterns on large text
SET REGEX_MATCH_TIMEOUT = 5000;

SELECT REGEX_MATCH(long_text, '(\w+\s*)+pattern') AS found FROM #docs;

SET REGEX_MATCH_TIMEOUT = 1000;
```

## Notes
- Protects against catastrophic backtracking in poorly constructed regular expressions.
- Corresponding `appsettings.json` key: `Security:RegexMatchTimeoutMs`.
- Default: 1,000 ms.

## References
- [SET Commands](README.md)
