### Added

- **`NOT IN (<list>)` and `NOT MATCHES <regex>`** — negative membership and pattern `@expect` rules,
  for the placeholders an upstream system writes when it does not know (`'UNKNOWN'`, `'N/A'`) and
  for content that must never reach a rendered surface. Both were expressible with `EXPR`; as named
  rules the intent carries into lineage, diagnostics and policy review. Great Expectations makes the
  same pair available.

  Each parses through the same code as its positive form, so an invalid regex or a `NULL` in the
  list is rejected in either direction rather than only when written positively. `SET CASE_SENSITIVE`
  applies unchanged.
