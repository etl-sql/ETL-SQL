### Fixed

- **`IN`/`NOT IN` rendered the row's value once per candidate.** The pairwise comparison converted
  both sides on every comparison, so an N-item list materialized the row's value N times per row.
  Each literal's rendered text and decimal form are now prepared once per rule, and the row's own
  text at most once per row — and only when some pair actually reaches the string path.

- **The compiled-regex cache for `MATCHES` was keyed by the rule record**, so every lookup hashed
  the whole pattern string to find an entry that never moves. It is now keyed by rule instance.

Both matter most where rows *fail*: a passing row was already close to free, but a quarantine-heavy
load runs the comparison to exhaustion on every row.

### Added

- The rule-cost harness now attaches each rule to a column whose values **satisfy** it. Measuring
  rules that reject every row measured the failure-reporting machinery — describing the failure,
  allocating the row-failure record, recording a sample — rather than the cost of having the rule.
  With that corrected, `NOT NULL`, `NOT BLANK`, `LENGTH`, `IN` and `MATCHES` all sit within ~1 MB of
  a rule-free statement over 50,000 rows; the rules that cost anything are the ones that call the
  evaluator per row (`BETWEEN` +28 MB, `EXPR` +61 MB) and `UNIQUE`, which spills (+380 MB).
