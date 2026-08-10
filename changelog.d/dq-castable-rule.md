### Added

- **`CASTABLE AS <type>`** — an `@expect` rule asserting the value would convert, for the ingestion
  case where everything arrives as text: `CASTABLE AS DATE`, `CASTABLE AS DECIMAL(18,2)`. It runs
  the engine's own conversion, the one behind `TRY_CAST`, so a value the rule accepts cannot fail a
  later cast — the two agree by construction rather than by two implementations happening to match.

  Two things the shared converter does not do on its own, and the rule now does:

  - **A declared width is enforced.** `Cast` ignores `DECIMAL(18,2)` and `VARCHAR(50)` widths
    entirely, so without this the declaration would read as a constraint while checking only "is a
    number" and "is a string".
  - **An unknown type name is rejected at parse time.** `Cast` returns the value unchanged for a
    type it has no converter for, which would have made `CASTABLE AS BANANA` accept every row —
    a validity rule that reports clean because it never checked anything.
