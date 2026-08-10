### Added

- **`BETWEEN <lower> AND <upper>`** — an `@expect` rule whose bounds are full expressions, so a
  range can be typed or relative: `BETWEEN DATEADD(DAY, -30, @RunDate) AND @RunDate`. The existing
  comparison rules accept only decimal literals and can express neither. Bounds are evaluated per
  row and compared with the engine's type-aware comparison, so dates compare as dates rather than as
  rendered text.

  A NULL bound makes the range unknown and skips the row, matching SQL's own `BETWEEN`: a rule that
  failed every row because a variable was unset would report the data as broken when the script is.

  The bound separator is found at parenthesis depth zero, so a lower bound containing its own `AND`
  — `IIF(a = 1 AND b = 2, …)` — is not cut in half.
