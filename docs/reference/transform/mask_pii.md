# MASK_PII

Applies declarative PII masking across one or more columns (`REDACT_EMAIL`, `MASK_LAST_4`, `HASH_SHA256`, `PERTURB_DATE`) while preserving `NULL` semantics and never leaking unmasked values to logs, errors, or lineage.

## Syntax

```sql
TRANSFORM #target [FROM #source]
USING MASK_PII (
    COLS = 'col1, col2',
    METHOD = 'REDACT_EMAIL' | 'MASK_LAST_4' | 'HASH_SHA256' | 'PERTURB_DATE',
    [SALT = 'SECRET:pii/hash_salt',]
    [MASK_CHAR = '*',]
    [MAX_DAYS = 15,]
    [SEED = 42]
);
```

## Options

- **COLS** — Comma-separated list of columns to mask (required).
- **METHOD** — Masking strategy (required):
  - **`REDACT_EMAIL`** — Keeps the first character of the local part and masks the rest before `@` (e.g. `alice@example.com` -> `a***@example.com`).
  - **`MASK_LAST_4`** — Masks all characters except the last 4 (values of 4 characters or fewer are masked in full).
  - **`HASH_SHA256`** — Replaces values with lowercase hex SHA-256 (optionally prepended with `SALT`).
  - **`PERTURB_DATE`** — Shifts date values by a non-zero offset within `[-MAX_DAYS, +MAX_DAYS]` (cryptographically random by default, or deterministic when `SEED` is supplied).
- **SALT** — Optional salt prepended before hashing in `HASH_SHA256` (supports `'SECRET:...'`, `'ENC:...'`, or variables).
- **MASK_CHAR** — Optional single masking character for `REDACT_EMAIL` and `MASK_LAST_4` (default: `'*'`).
- **MAX_DAYS** — Positive integer maximum day shift for `PERTURB_DATE` (default: `15`).
- **SEED** — Optional integer seed for deterministic `PERTURB_DATE` testing.

## Examples

```sql
TRANSFORM #masked
FROM #customers
USING MASK_PII (
    COLS = 'Email',
    METHOD = 'REDACT_EMAIL'
);

TRANSFORM #masked
USING MASK_PII (
    COLS = 'Ssn',
    METHOD = 'MASK_LAST_4'
);
```

## References

- [TRANSFORM Reference](README.md)
- [TRANSFORM Statement](../statements/dml/transform.md)
- [Syntax Index](../../syntax-index.md)
