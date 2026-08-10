### Added

- **`NOT BLANK`** — an `@expect` rule rejecting empty and whitespace-only strings. Expressible
  before with a regex or by repeating the column name in `EXPR`; as its own rule the intent is
  legible in diagnostics, autocomplete and policy review. It skips NULL like every rule except
  `NOT NULL`, so `'NOT NULL, NOT BLANK'` is the full "a value is required" check.

- **`LENGTH BETWEEN <min> AND <max>`** and `LENGTH >= <n>` (with `<=`, `>`, `<`, `=`) — character
  count rules, a standard validity category in Great Expectations and Soda. Every form lowers onto
  one inclusive range, so the runtime carries a single predicate rather than one per operator, and
  a range no value can satisfy (`LENGTH BETWEEN 10 AND 5`, `LENGTH < 0`) is rejected at parse time
  rather than quarantining every row. Length is the rendered value's character count, matching
  `LEN`.

### Changed

- **An `@expect` rule the runtime does not implement now fails the statement instead of passing
  every row.** The two per-row rule switches each ended in a `default` that returned "passed", so a
  `ColumnRule` record added without its runtime arm would have reported the data clean. The two
  switches were also full copies of each other — one existed only because `EXPR` needs the
  evaluator — so they are now one predicate with a thin async wrapper for that single form.
