STRING Functions
================

Case Conversion
---------------
  UPPER(s)                    Return s in upper case.
  LOWER(s)                    Return s in lower case.

```sql
SELECT UPPER('hello')         -- 'HELLO'
SELECT LOWER('WORLD')         -- 'world'
```

Whitespace Trimming
-------------------
  TRIM(s)                     Remove leading and trailing spaces.
  LTRIM(s)                    Remove leading (left) spaces only.
  RTRIM(s)                    Remove trailing (right) spaces only.

```sql
SELECT TRIM('  hello  ')      -- 'hello'
SELECT LTRIM('  hi')          -- 'hi'
SELECT RTRIM('hi  ')          -- 'hi'
```

Length
------
  LEN(s)                      Length in characters (excludes trailing spaces).
  LENGTH(s)                   Alias for LEN.
  CHAR_LENGTH(s)              Alias for LEN.

```sql
SELECT LEN('hello')           -- 5
SELECT LEN(NULL)              -- NULL
```

Substring Extraction
--------------------
  LEFT(s, n)                  First n characters of s.
  RIGHT(s, n)                 Last n characters of s.
  SUBSTRING(s, start, len)    Extract len characters starting at position start (1-based).
  SUBSTR(s, start, len)       Alias for SUBSTRING.

```sql
SELECT LEFT('abcdef', 3)           -- 'abc'
SELECT RIGHT('abcdef', 3)          -- 'def'
SELECT SUBSTRING('abcdef', 2, 3)   -- 'bcd'
SELECT SUBSTR('hello world', 7, 5) -- 'world'
```

Note: start is 1-based. SUBSTRING(s, 1, n) returns the first n chars.

Search and Position
-------------------
  CHARINDEX(search, target [, start])
      Return position of search in target; 0 if not found.
      Optional start: begin scanning from this position (1-based).
  INSTR(target, search)       Same as CHARINDEX but argument order reversed.
  PATINDEX(pattern, s)        Position of first match of a LIKE-style pattern in s; 0 if none.
  POSITION(search IN target)  SQL standard form; returns position or 0.

```sql
SELECT CHARINDEX('lo', 'hello')          -- 4
SELECT CHARINDEX('x', 'hello')           -- 0
SELECT CHARINDEX('l', 'hello world', 5)  -- 9  (scan starts at pos 5)
SELECT INSTR('hello', 'ell')             -- 2
SELECT PATINDEX('%[0-9]%', 'abc123')     -- 4
SELECT POSITION('lo' IN 'hello')         -- 4
```

Replace and Transform
----------------------
  REPLACE(s, old, new)        Replace every occurrence of old with new in s.
  STUFF(s, start, len, insert)
      Delete len characters at position start, then insert the string insert.
  TRANSLATE(s, from_chars, to_chars)
      Replace each character in from_chars with the corresponding char in to_chars.
  REVERSE(s)                  Reverse the character order of s.

```sql
SELECT REPLACE('hello world', 'world', 'SQL') -- 'hello SQL'
SELECT STUFF('abcdef', 2, 3, 'XY')            -- 'aXYef'
SELECT TRANSLATE('2+3*5', '+*', '-/')          -- '2-3/5'
SELECT REVERSE('abcde')                        -- 'edcba'
```

Padding and Repetition
-----------------------
  REPLICATE(s, n)             Repeat s exactly n times.
  SPACE(n)                    Return a string of n space characters.

```sql
SELECT REPLICATE('ab', 3)    -- 'ababab'
SELECT SPACE(4)              -- '    '
SELECT REPLICATE('-', 40)    -- '----------------------------------------'
```

Concatenation
-------------
  CONCAT(a, b, ...)           Concatenate values; NULLs silently treated as empty string.
  CONCAT_WS(sep, a, b, ...)   Concatenate with separator sep; NULLs are skipped.
  The + operator also concatenates strings but propagates NULL.

```sql
SELECT CONCAT('Hello', ' ', 'World')            -- 'Hello World'
SELECT CONCAT('a', NULL, 'b')                   -- 'ab'  (NULL ignored)
SELECT CONCAT_WS(', ', 'Alice', NULL, 'Bob')    -- 'Alice, Bob'
SELECT 'Hello' + ' ' + 'World'                  -- 'Hello World'
SELECT 'Hello' + NULL                           -- NULL  (+ propagates NULL)
```

String Splitting
----------------
  STRING_SPLIT(s, delim)
      Table-valued function. Returns one row per token with a column named value.
      Use in FROM or CROSS APPLY.

```sql
-- Expand a comma list
SELECT value FROM STRING_SPLIT('a,b,c', ',')
-- Rows: 'a', 'b', 'c'

-- Join split results back to main table
SELECT o.OrderID, s.value AS Tag
FROM Orders o
CROSS APPLY STRING_SPLIT(o.Tags, ',') s
```

Formatting and Conversion
--------------------------
  STR(n [, len [, dec]])
      Convert numeric n to a right-aligned string of length len with dec decimal places.
      Default len=10, dec=0.
  FORMAT(value, format_string)
      Format a value using a .NET composite format or date format string.

```sql
SELECT STR(123.456, 8, 2)             -- '  123.46'
SELECT FORMAT(1234567.89, 'N2')        -- '1,234,567.89'
SELECT FORMAT(GETDATE(), 'yyyy-MM-dd') -- '2025-03-15'
SELECT FORMAT(0.175, 'P1')             -- '17.5%'
```

Soundex and Phonetic
---------------------
  SOUNDEX(s)                  Return the 4-character Soundex code for s.
  DIFFERENCE(s1, s2)          Return 0-4 similarity score between the Soundex codes of s1 and s2.
                              4 = identical sound; 0 = completely different.

```sql
SELECT SOUNDEX('Smith')          -- 'S530'
SELECT SOUNDEX('Smythe')         -- 'S530'
SELECT DIFFERENCE('Smith', 'Smythe')  -- 4
```

Quoting and Escaping
---------------------
  QUOTENAME(s [, delimiter])
      Wrap s in brackets (default) or the specified delimiter character.
      Delimiter can be: [ ] (default), ' ', or ".
  STRING_ESCAPE(s, 'json')
      Escape special characters in s for safe embedding in a JSON string value.

```sql
SELECT QUOTENAME('My Table')           -- '[My Table]'
SELECT QUOTENAME('it''s', '''')        -- '''it''''s'''
SELECT STRING_ESCAPE('Line1\nLine2', 'json')  -- 'Line1\\nLine2'
```

Character Codes
---------------
  ASCII(s)                    Return the ASCII code of the first character of s.
  UNICODE(s)                  Return the Unicode code point of the first character of s.
  CHAR(n)                     Return the character for ASCII/Unicode code point n.

```sql
SELECT ASCII('A')             -- 65
SELECT UNICODE('€')           -- 8364
SELECT CHAR(65)               -- 'A'
SELECT CHAR(10)               -- newline character
```

Overlay
-------
  OVERLAY(s PLACING ins FROM pos FOR len)
      Replace len characters of s starting at pos with ins.
      Equivalent to STUFF but uses SQL standard syntax.

```sql
SELECT OVERLAY('Hello World' PLACING 'SQL' FROM 7 FOR 5)  -- 'Hello SQL'
```

Aggregating Strings (aggregate context)
----------------------------------------
  STRING_AGG(col, separator)
      Concatenate non-NULL values of col within a group, joined by separator.
      Use with GROUP BY or OVER. See also HELP FUNCTIONS AGGREGATE.

```sql
SELECT DeptID, STRING_AGG(Name, ', ') AS Members
FROM Employees
GROUP BY DeptID
```
