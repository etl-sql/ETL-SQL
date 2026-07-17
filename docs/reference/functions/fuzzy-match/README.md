# Fuzzy Matching Functions

Fuzzy matching functions compare strings by similarity, phonetics, and edit distance.

## Functions

- [DIFFERENCE](difference.md) - Returns a Soundex similarity score between two strings.
- [DMETAPHONE](dmetaphone.md) - Returns the primary Double Metaphone phonetic key for a string.
- [DMETAPHONE_ALT](dmetaphone_alt.md) - Returns the alternate Double Metaphone phonetic key for a string.
- [LEVENSHTEIN](levenshtein.md) - Computes the Levenshtein edit distance between two strings.
- [METAPHONE](metaphone.md) - Returns the English phonetic code (Metaphone key) of a string.
- [NGRAM_TOKENS](ngram_tokens.md) - Returns 3-character grams from normalized tokens in a string. Use this for fuzzy-join blocking keys.
- [NGRAMS](ngrams.md) - Returns N-character grams from a string as rows. Use it to create blocking keys for fuzzy matching and inverted-index style joins.
- [NORMALIZE](normalize.md) - Preprocesses a string to eliminate surface variation before similarity scoring.
- [SIMILARITY](similarity.md) - Returns a normalized similarity score between two strings using the specified algorithm.
- [SOUNDEX](soundex.md) - Returns the Soundex phonetic encoding of a string.

## References

- [Functions](../README.md)
- [Functions](../README.md)
- [Syntax Index](../../../syntax-index.md)
