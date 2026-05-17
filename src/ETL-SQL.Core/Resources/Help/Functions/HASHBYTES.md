# HASHBYTES
Computes a cryptographic hash of a string value.

**Category:** System

## Syntax
```sql
HASHBYTES(algorithm, string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `algorithm` | `STRING` | Hash algorithm name — see Accepted Values |
| `string` | `STRING` | The value to hash |

## Returns
`VARBINARY` — The raw binary hash digest.

## Accepted Values for `algorithm`
| Value | Output Size |
| :--- | :--- |
| `'MD5'` | 16 bytes |
| `'SHA1'` | 20 bytes |
| `'SHA256'` / `'SHA2_256'` | 32 bytes |
| `'SHA512'` / `'SHA2_512'` | 64 bytes |

## Remarks
- MD5 and SHA1 are included for legacy compatibility. Prefer SHA256 or SHA512 for new implementations.
- For row change detection (CDC), use [`CHECKSUM`](CHECKSUM.md) instead — it is faster and returns an INT.

## Example
```sql
SELECT HASHBYTES('SHA256', email) AS email_hash FROM #users;
SELECT HASHBYTES('MD5', CONCAT(first_name, last_name)) AS name_hash FROM #people;
```

## See Also
- [Standard Library — §9. Hashing & Checksums](../../../../../Docs/Reference/Standard_Library.md#9-hashing--checksums)
- Related: [`CHECKSUM`](CHECKSUM.md), [`BINARY_CHECKSUM`](BINARY_CHECKSUM.md)
