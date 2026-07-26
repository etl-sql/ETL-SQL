# MASK_SSN

Masks a Social Security Number for governance and reporting output, keeping only the last four digits.

## Syntax

```sql
MASK_SSN(ssn)
```

## Parameters

- **ssn** - Value to mask. Formatting characters are ignored.

## Returns

Returns a `VARCHAR` in the form `***-**-NNNN`:

| Input | Output |
| :--- | :--- |
| `123-45-6789` | `***-**-6789` |
| `123456789` | `***-**-6789` |
| `12` | `***-**-****` |

## Null Behavior

Returns `NULL` when `ssn` is `NULL`.

## Remarks

- All non-digit characters are stripped before masking, so any input formatting is accepted.
- A value with fewer than four digits returns the fully masked constant `***-**-****`.
- The function does not validate that the input is a real SSN; it masks whatever digits it finds.
- **Last-four disclosure is still identifying in combination with other attributes.** Retaining the
  final four digits alongside a name, date of birth, or ZIP code can be sufficient to re-identify an
  individual — mask or drop those columns too when publishing.
- This is **presentation masking for reports and diagnostics, not a security control.** It does not
  remove the underlying value from the source. Tag the column as protected data and rely on the
  policy-enforced stewardship features where access itself must be restricted.

## Examples

```sql
SELECT MASK_SSN(tax_id) AS tax_id
FROM #employees;
```

```sql
-- Produce a reviewable extract without exposing full identifiers
SELECT
  employee_id,
  MASK_SSN(tax_id) AS tax_id,
  department
FROM #employees;
```

## References

- [Functions](../README.md)
- [MASK_EMAIL](mask_email.md)
- [MASK_PHONE](mask_phone.md)
