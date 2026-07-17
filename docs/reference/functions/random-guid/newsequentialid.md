# NEWSEQUENTIALID

Generates a new sequential identifier value.

## Syntax

```sql
NEWSEQUENTIALID()
```

## Parameters

None.

## Returns

Returns a unique identifier value.

## Null Behavior

`NEWSEQUENTIALID()` takes no arguments and never returns `NULL`.

## Remarks

- Use `NEWSEQUENTIALID()` when generated identifiers should preserve insertion locality better than fully random identifiers.
- Use [`NEWID`](newid.md) when random GUID-style identifiers are preferred.
- Do not treat generated identifiers as secrets.

## Examples

```sql
SELECT NEWSEQUENTIALID() AS batch_id;
```

```sql
INSERT INTO #audit_events(event_id, event_name)
SELECT NEWSEQUENTIALID(), 'load-started';
```

## References

- [Functions](../README.md)
- [NEWID](newid.md)
