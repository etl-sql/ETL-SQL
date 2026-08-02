DECLARE creates a named variable in the current scope. Variables are scoped to the procedure, script, or block in which they are declared.

Syntax:
  DECLARE @name <TYPE> [INPUT | OUTPUT] [= <value>];

Types:
- **STRING / VARCHAR** — text
- **INT** — whole number
- **DECIMAL / FLOAT** — fractional number
- **BOOL** — TRUE or FALSE
- **DATE / DATETIME** — date/time value
- **RELDATE** — relative date expression resolved at run time (e.g. 'D-1', 'M-1')
- **JSON** — JSON value
- **XML** — XML fragment
- **MARKDOWN** — Markdown text
- **LIST** — comma-separated or array list
- **PATH** — file system path (validated and resolved via ResolvePath)
- **MINMAX** — numeric range pair (low, high)
- **SENSITIVE** — string masked in logs
- **SECRET** — like SENSITIVE; also suppressed from `eng.variables` output
- **ENCRYPTED** — value stored encrypted at rest

Modifiers:
- **INPUT** — value can be overridden from the CLI (--var @Name=value) or a calling script (RUN SCRIPT ... WITH)
- **OUTPUT** — value is passed back to the calling script after the procedure completes

```sql
-- Simple variable
DECLARE @threshold INT = 100;

-- Report input parameter (overridable by caller)
DECLARE @start RELDATE INPUT = 'M-1';
DECLARE @end   RELDATE INPUT = 'D-0';

-- Output from a procedure
DECLARE @result_count INT OUTPUT;
```

Use HELP RELDATE for the full relative-date expression syntax.

References:
- [Variables and Parameters](README.md)
