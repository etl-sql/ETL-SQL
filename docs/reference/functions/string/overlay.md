# OVERLAY

Replaces part of a string with another string using SQL-standard `OVERLAY` syntax.

## Syntax

```sql
OVERLAY(string PLACING replacement FROM start [FOR length])
```

## Parameters

- **string** - Source string.
- **replacement** - String to insert.
- **start** - 1-based start position.
- **length** - Optional number of characters to replace. When omitted, the replacement is inserted at `start`.

## Returns

Returns a `STRING`.

## Null Behavior

Returns `NULL` when `string`, `replacement`, or `start` is `NULL`.

## Remarks

- Use `OVERLAY` for SQL-standard replacement syntax.
- Use [`STUFF`](stuff.md) for T-SQL-style positional replacement.
- Use [`REPLACE`](replace.md) for replacing every occurrence of a substring.

## Examples

```sql
SELECT OVERLAY('Hello World' PLACING 'SQL' FROM 7 FOR 5) AS result;
```

```sql
SELECT OVERLAY(account_number PLACING '****' FROM 1 FOR 4) AS masked_account
FROM #accounts;
```

## References

- [Standard Library](../standard-library.md)
- [STUFF](stuff.md)
- [REPLACE](replace.md)
