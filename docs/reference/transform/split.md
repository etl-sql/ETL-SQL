# SPLIT

Expands delimited strings or nested JSON arrays into multiple rows (`MODE = 'ROWS'`) or numbered columns (`MODE = 'COLUMNS'`), preserving parent key columns. `EXPLODE` is a synonym for `SPLIT`.

## Syntax

```sql
TRANSFORM #target [FROM #source]
USING SPLIT (
    SOURCE_COL = 'column_name',
    [MODE = 'ROWS' | 'COLUMNS',]
    [DELIMITER = ',',]
    [JSON_PATH = '$.items',]
    [OUTPUT_COL = 'item',]
    [PREFIX = 'part_',]
    [MAX_PARTS = 4,]
    [INDEX_COL = 'ItemIndex',]
    [TRIM = 'ON' | 'OFF',]
    [KEEP_EMPTY = 'ON' | 'OFF']
);
```

## Options

- **SOURCE_COL** — Column containing delimited text or JSON (required).
- **MODE** — `'ROWS'` (default, one output row per element) or `'COLUMNS'` (appends `<PREFIX>1`, `<PREFIX>2`, … columns).
- **DELIMITER** — Delimiter string for text splitting (mutually exclusive with `JSON_PATH`).
- **JSON_PATH** — Dot-path to a JSON array (e.g. `'$'`, `'$.items'`, `'$.data.orders'`; mutually exclusive with `DELIMITER`). When `MODE = 'ROWS'` and elements are JSON objects, each object's properties become columns while preserving parent columns. An array that mixes objects and scalar values is refused rather than dropping the scalars.
- **OUTPUT_COL** — Output column name for scalar `MODE = 'ROWS'` values (default: replaces `SOURCE_COL`).
- **PREFIX** — Column prefix for `MODE = 'COLUMNS'` (default: `<SOURCE_COL>_`) or JSON object properties.
- **MAX_PARTS** — Positive integer column count for `MODE = 'COLUMNS'`. With `DELIMITER`, a value with more parts puts the rest, delimiters included, in the last column (`'a,b,c'` with `MAX_PARTS = 2` gives `a` and `b,c`). With `JSON_PATH`, an array longer than `MAX_PARTS` is refused.
- **INDEX_COL** — Optional 1-based element position column (`INT`) for `MODE = 'ROWS'`.
- **TRIM** — `'ON'` or `'OFF'` (default: `'OFF'`); trims whitespace around delimited parts.
- **KEEP_EMPTY** — `'ON'` (default) keeps a parent row with `NULL` output for `NULL`/empty inputs in `MODE = 'ROWS'`; `'OFF'` drops them.

New columns (JSON object properties, `OUTPUT_COL`, `INDEX_COL`, `<PREFIX>n`) may replace only `SOURCE_COL`. If one would overwrite another source column, for example a JSON object's own `Id` beside the parent `Id`, the statement fails; set `PREFIX`, `OUTPUT_COL` or `INDEX_COL` to a new name.

## Examples

```sql
-- Explode comma-separated tags into rows with 1-based index
TRANSFORM #tag_rows
FROM #articles
USING SPLIT (
    SOURCE_COL = 'Tags',
    MODE = 'ROWS',
    DELIMITER = ',',
    OUTPUT_COL = 'Tag',
    INDEX_COL = 'TagIndex',
    TRIM = 'ON'
);

-- Flatten nested JSON array of objects from a REST/webhook payload
TRANSFORM #order_lines
FROM #webhook_payloads
USING EXPLODE (
    SOURCE_COL = 'Payload',
    MODE = 'ROWS',
    JSON_PATH = '$.items',
    INDEX_COL = 'LineNo'
);
```

## References

- [TRANSFORM Reference](README.md)
- [TRANSFORM Statement](../statements/dml/transform.md)
- [Syntax Index](../../syntax-index.md)
