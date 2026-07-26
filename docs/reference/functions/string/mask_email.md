# MASK_EMAIL

Masks the local part of an email address for governance and reporting output.

## Syntax

```sql
MASK_EMAIL(email)
```

## Parameters

- **email** - Email address to mask.

## Returns

Returns a `VARCHAR` with the local part reduced to its first and last character:

| Input | Output |
| :--- | :--- |
| `jonathan@example.com` | `j***n@example.com` |
| `ab@example.com` | `*@example.com` |
| `not-an-email` | `***@***.com` |

## Null Behavior

Returns `NULL` when `email` is `NULL`.

## Remarks

- **The domain is preserved in full.** Where the domain itself is identifying (a single-tenant or
  personal domain), masking the local part alone is not sufficient de-identification.
- A local part of two characters or fewer collapses to `*` so the mask cannot be trivially reversed.
- A value with no `@` is treated as unparseable and returns the constant `***@***.com`, which means
  the output does not distinguish a malformed address from a missing one.
- This is **presentation masking for reports and diagnostics, not a security control.** It does not
  remove the underlying value from the source, and it is not reversible-safe tokenization. For
  policy-enforced protection, tag the column and use the protected-data stewardship features.

## Examples

```sql
SELECT MASK_EMAIL(contact_email) AS contact
FROM #customers;
```

```sql
-- Share a support-volume extract without exposing full addresses
SELECT
  MASK_EMAIL(requester_email) AS requester,
  COUNT(*) AS ticket_count
FROM #tickets
GROUP BY MASK_EMAIL(requester_email);
```

## References

- [Functions](../README.md)
- [MASK_PHONE](mask_phone.md)
- [MASK_SSN](mask_ssn.md)
