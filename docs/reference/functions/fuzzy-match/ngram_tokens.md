# NGRAM_TOKENS

Returns 3-character grams from normalized tokens in a string. Use this for fuzzy-join blocking keys.

## Syntax

```sql
SELECT * FROM NGRAM_TOKENS(string)
```

## Parameters

- **string** - Source string to normalize and split into token grams.

## Returns

Returns a table of token 3-gram values.

## Null Behavior

Returns no rows when `string` is `NULL`.

## Remarks

- `NGRAM_TOKENS` is intended for candidate generation before more expensive fuzzy comparisons.
- Use [`NGRAMS`](ngrams.md) for direct N-character grams.
- Use [`NORMALIZE`](normalize.md) before custom matching logic.

## Examples

```sql
SELECT *
FROM NGRAM_TOKENS('John Smith');
```

```sql
SELECT customer_id, token.value AS blocking_key
FROM #customers
CROSS APPLY NGRAM_TOKENS(customer_name) AS token;
```

## References

- [Functions](../README.md)
- [NGRAMS](ngrams.md)
- [NORMALIZE](normalize.md)
