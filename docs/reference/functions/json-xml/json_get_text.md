# JSON_GET_TEXT
One JSON access step returned **as text** — strings come back unquoted. This is the function behind
the `->>` operator (PostgreSQL/MySQL/SQLite style).

**Category:** JSON

## Syntax
```sql
JSON_GET_TEXT(json, key_or_index)
json ->> key_or_index          -- operator form (preferred)
```

## Parameters
- **json** (`STRING`/`JSON`) — the JSON document (object or array).
- **key_or_index** — a string field name (for objects) or an integer index (for arrays; negative
  counts from the end). May be any expression, including a variable.

## Returns
`STRING` — the selected value as text: strings unquoted, numbers/booleans as their literal text,
JSON `null` as `NULL`, objects/arrays as their raw JSON text. `NULL` when the key is missing, the
index is out of range, or the input is not valid JSON.

## Remarks
- Use `->` / `JSON_GET` for intermediate steps (keeps the value as JSON so steps chain); use `->>`
  for the final step where you want the plain value.
- Null-propagating by design — combine with `??` for defaults: `doc ->> 'qty' ?? '0'`.

## Example
```sql
SELECT doc ->> 'name' AS name FROM #people;               -- Alice (no quotes)
SELECT doc -> 'items' ->> 0 AS first_item FROM #orders;   -- first array element as text
SELECT CAST(doc ->> 'qty' AS INT) * price AS total FROM #orders;
```

References:
- [Standard Library — §11. JSON Functions](../../../guides/getting-started.md#11-json-functions)
- [Grammar §14.6 — JSON Access Operators](../../../guides/getting-started.md)
- Related: [`JSON_GET`](json_get.md), [`JSON_VALUE`](../json-xml/json_value.md), [`JSON_QUERY`](../json-xml/json_query.md)
