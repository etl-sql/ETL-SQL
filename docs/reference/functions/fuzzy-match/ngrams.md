# NGRAMS

Returns N-character grams from a string as rows. Use it to create blocking keys for fuzzy matching and inverted-index style joins.

## Syntax

```sql
SELECT * FROM NGRAMS(string, size)
```

## Parameters

- **string** - Text to split into grams.
- **size** - Gram length.

## Returns

Returns a table of generated gram values.

## Null Behavior

Returns no rows when `string` or `size` is `NULL`.

## Examples

```sql
SELECT *
FROM NGRAMS('hello', 2);
```

```sql
SELECT customer_id, gram.value AS name_gram
FROM #customers
CROSS APPLY NGRAMS(normalized_name, 3) AS gram;
```

## References

- [Functions](../README.md)
- [NGRAM_TOKENS](ngram_tokens.md)
- [SIMILARITY](similarity.md)
