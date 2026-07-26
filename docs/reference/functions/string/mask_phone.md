# MASK_PHONE

Masks a phone number for governance and reporting output, keeping only the last four digits.

## Syntax

```sql
MASK_PHONE(phone)
```

## Parameters

- **phone** - Phone number to mask. Formatting characters are ignored.

## Returns

Returns a `VARCHAR` in the form `***-***-NNNN`:

| Input | Output |
| :--- | :--- |
| `(555) 867-5309` | `***-***-5309` |
| `+1 555 867 5309` | `***-***-5309` |
| `123` | `***-***-****` |

## Null Behavior

Returns `NULL` when `phone` is `NULL`.

## Remarks

- All non-digit characters are stripped before masking, so any input formatting is accepted.
- A value with fewer than four digits returns the fully masked constant `***-***-****`.
- **The output shape is always US-style** regardless of the input's country or length, so an
  international number is reformatted as it is masked. Only the final four digits are meaningful.
- This is **presentation masking for reports and diagnostics, not a security control.** It does not
  remove the underlying value from the source.

## Examples

```sql
SELECT MASK_PHONE(mobile_number) AS mobile
FROM #contacts;
```

```sql
-- Redact contact details in an exported call log
SELECT
  call_id,
  MASK_PHONE(from_number) AS caller,
  MASK_PHONE(to_number)   AS recipient,
  duration_seconds
FROM #call_log;
```

## References

- [Functions](../README.md)
- [MASK_EMAIL](mask_email.md)
- [MASK_SSN](mask_ssn.md)
