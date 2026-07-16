# NORMALIZE
Preprocesses a string to eliminate surface variation before similarity scoring.

**Category:** Fuzzy

## Syntax
```sql
NORMALIZE(string)
NORMALIZE(string, mode)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The string to normalize |
| `mode` | `STRING` | Optional: domain-specific normalization profile |

## Returns
`STRING` — The normalized string.

## Accepted Values for `mode`
| Value | What it does |
| :--- | :--- |
| *(omitted)* | Lowercase, trim, collapse whitespace, Unicode NFC, strip control chars |
| `'COMPANY'` | Remove legal suffixes (LLC, Inc, Corp…), expand `&` → `and`, strip articles |
| `'PERSON'` | Remove titles and generational suffixes (Mr, Dr, Jr, PhD…) |
| `'ADDRESS'` | Expand directional and street-type abbreviations, remove unit designators |
| `'PHONE'` | Strip all non-digit characters; remove leading `1` if 11 digits |
| `'EMAIL'` | Lowercase and trim only |

## Example
```sql
-- Apply NORMALIZE before SIMILARITY to boost match rates 5-15%
SELECT SIMILARITY(
    NORMALIZE(a.company_name, 'COMPANY'),
    NORMALIZE(b.company_name, 'COMPANY')
) AS score
FROM #source a CROSS JOIN #reference b
WHERE SIMILARITY(
    NORMALIZE(a.company_name, 'COMPANY'),
    NORMALIZE(b.company_name, 'COMPANY')
) > 0.80;
```

## See Also
- [Standard Library — §16.1 NORMALIZE](../../../guides/getting-started.md#161-normalize--domain-aware-preprocessing)
- Related: [`SIMILARITY`](similarity.md), [`LEVENSHTEIN`](levenshtein.md)
