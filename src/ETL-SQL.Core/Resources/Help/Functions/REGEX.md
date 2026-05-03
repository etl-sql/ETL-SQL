REGEX Functions
===============

Regular-expression functions for pattern matching, extraction, and substitution.
Patterns use .NET regex syntax. Matching is case-insensitive by default.

Flag characters (pass as a string literal):
  i   case-insensitive (default)
  m   multiline — ^ and $ match line boundaries
  s   singleline — . matches newline characters
  x   ignore whitespace inside the pattern

Matching and Testing
--------------------
  REGEXP_LIKE(str, pattern)              Return 1 if str matches pattern, 0 otherwise.
  REGEXP_LIKE(str, pattern, flags)       Same with explicit flags.

```sql
SELECT REGEXP_LIKE('Hello World', 'hello')         -- 1  (case-insensitive)
SELECT REGEXP_LIKE('abc123', '^[a-z]+$')           -- 0  (contains digits)
SELECT REGEXP_LIKE('Hello\nWorld', 'hello.*world', 's') -- 1  (singleline)
```

Extraction
----------
  REGEXP_SUBSTR(str, pattern)            Return the first matching substring; NULL if none.
  REGEXP_SUBSTR(str, pattern, pos)       Start scanning from character position pos (1-based).
  REGEXP_SUBSTR(str, pattern, pos, occ)  Return the nth occurrence.

```sql
SELECT REGEXP_SUBSTR('order #1042 ref #2001', '#\d+')     -- '#1042'
SELECT REGEXP_SUBSTR('order #1042 ref #2001', '#\d+', 1, 2) -- '#2001'
SELECT REGEXP_SUBSTR('no digits here', '\d+')              -- NULL
```

Substitution
------------
  REGEXP_REPLACE(str, pattern, replacement)
      Replace the first (or all, by default) matches with replacement.
  REGEXP_REPLACE(str, pattern, replacement, pos, occ, flags)
      pos: start position; occ: 0 = all occurrences, n = replace nth only.

```sql
SELECT REGEXP_REPLACE('2025-03-15', '-', '/')          -- '2025/03/15'
SELECT REGEXP_REPLACE('aabbcc', 'b+', 'X')             -- 'aaXcc'
SELECT REGEXP_REPLACE('  extra   spaces  ', '\s+', ' ') -- ' extra spaces '
```

Position
--------
  REGEXP_INSTR(str, pattern)              Return the 1-based start position of the first match; 0 if none.
  REGEXP_INSTR(str, pattern, pos, occ)   Start from pos, find nth occurrence.

```sql
SELECT REGEXP_INSTR('foo123bar456', '\d+')        -- 4
SELECT REGEXP_INSTR('foo123bar456', '\d+', 1, 2)  -- 10
SELECT REGEXP_INSTR('no digits', '\d+')            -- 0
```

Counting
--------
  REGEXP_COUNT(str, pattern)             Count the number of non-overlapping matches.
  REGEXP_COUNT(str, pattern, pos, flags)

```sql
SELECT REGEXP_COUNT('abcabc', 'a')              -- 2
SELECT REGEXP_COUNT('one two three', '\b\w+\b') -- 3
SELECT REGEXP_COUNT('aaa', 'aa')               -- 1  (non-overlapping)
```

Table-Valued Functions
----------------------
  REGEXP_MATCHES(str, pattern)
      Return a table of all matches (one row per match, column: match).

  REGEXP_SPLIT_TO_TABLE(str, pattern)
      Split str on the pattern and return one row per token (column: value).

```sql
-- Extract all numbers from a string
SELECT match FROM REGEXP_MATCHES('a1 b22 c333', '\d+')
-- Rows: '1', '22', '333'

-- Split on any whitespace
SELECT value FROM REGEXP_SPLIT_TO_TABLE('one  two   three', '\s+')
-- Rows: 'one', 'two', 'three'
```

Notes
-----
  - Captured groups are not exposed separately; the whole match is returned.
  - Use REGEXP_SUBSTR with a group-only pattern to extract a capture group indirectly.
  - For simple LIKE-style matching use PATINDEX or LIKE instead of regex.
  - See also: STRING functions — HELP FUNCTIONS STRING.
