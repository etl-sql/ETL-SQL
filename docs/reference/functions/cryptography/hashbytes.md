# HASHBYTES

Computes a cryptographic hash of a string value.

## Syntax

```sql
HASHBYTES(algorithm, string)
```

## Parameters

- **algorithm** - Hash algorithm name.
- **string** - Value to hash.

## Returns

Returns the raw binary hash digest as `VARBINARY`.

## Null Behavior

Returns `NULL` when `algorithm` or `string` is `NULL`.

## Accepted Values for `algorithm`

| Value | Output Size |
| :--- | :--- |
| `'MD5'` | 16 bytes |
| `'SHA1'` | 20 bytes |
| `'SHA256'` / `'SHA2_256'` | 32 bytes |
| `'SHA512'` / `'SHA2_512'` | 64 bytes |

## Remarks

- MD5 and SHA1 are included for legacy compatibility. Prefer SHA256 or SHA512 for new implementations.
- For row change detection, use [`CHECKSUM`](checksum.md) when a fast non-cryptographic hash is sufficient.

## Examples

```sql
SELECT user_id, HASHBYTES('SHA256', email) AS email_hash
FROM #users;
```

```sql
SELECT person_id, HASHBYTES('SHA512', CONCAT(first_name, last_name)) AS name_hash
FROM #people;
```

## References

- [Functions](../README.md)
- [CHECKSUM](checksum.md)
- [BINARY_CHECKSUM](binary_checksum.md)
