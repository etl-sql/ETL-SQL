# MASK_PII

Applies declarative PII masking across one or more columns (`REDACT_EMAIL`, `MASK_LAST_4`, `HASH_SHA256`, `PERTURB_DATE`) while preserving `NULL` semantics and never leaking unmasked values to logs, errors, or lineage.

## Syntax

```sql
TRANSFORM #target [FROM #source]
USING MASK_PII (
    COLS = 'col1, col2',
    METHOD = 'REDACT_EMAIL' | 'MASK_LAST_4' | 'HASH_SHA256' | 'PERTURB_DATE',
    [SALT = @hash_key,]
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
  - **`HASH_SHA256`** — Replaces values with a lowercase hex HMAC-SHA256 token keyed by `SALT` (required). The same value and key always give the same token, so joins across masked tables still work; without the key the token cannot be recomputed from a guessed value.
  - **`PERTURB_DATE`** — Shifts date values by a non-zero offset within `[-MAX_DAYS, +MAX_DAYS]` (cryptographically random by default, or deterministic when `SEED` is supplied).
- **SALT** — Secret key for `HASH_SHA256` (required for that method). Pass an `ENC:` value or a sensitive variable; `TRANSFORM` options do not resolve `SECRET:` or `${ENV}` references, so those are refused rather than used as literal text. Keep the key out of the script text: anyone with it can test a guessed value against the tokens.
- **MASK_CHAR** — Optional single masking character for `REDACT_EMAIL` and `MASK_LAST_4` (default: `'*'`).
- **MAX_DAYS** — Positive integer maximum day shift for `PERTURB_DATE` (default: `15`).
- **SEED** — Optional integer seed for deterministic `PERTURB_DATE` testing. Anyone who knows the seed and row order can undo the shift, so do not use `SEED` on data you share.

## What masking does and does not hide

- `REDACT_EMAIL` keeps the first character and the whole domain.
- `MASK_LAST_4` keeps the last four characters; on short values (a 5-digit postal code) that is most of the value.
- `PERTURB_DATE` hides the exact date, not the approximate one (within `MAX_DAYS`).
- `HASH_SHA256` is pseudonymization: equal inputs give equal tokens, so frequency and linkage remain visible.

Masked output is still personal data under most regimes when it can be linked back; treat the masked
table with the same access controls unless your data-protection review says otherwise.

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

-- Keyed pseudonymous tokens; @hash_key holds an ENC: value
TRANSFORM #tokens
FROM #customers
USING MASK_PII (
    COLS = 'Email, Phone',
    METHOD = 'HASH_SHA256',
    SALT = @hash_key
);
```

## References

- [TRANSFORM Reference](README.md)
- [TRANSFORM Statement](../statements/dml/transform.md)
- [Syntax Index](../../syntax-index.md)
