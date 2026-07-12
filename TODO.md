# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## v0.15.0 — Adaptive Execution & Extended Large-Data Certification

The v0.14.0 billion-row program established a credible but deliberately narrow certification for
streaming scan, filter, projection, low-cardinality aggregation, and spill-backed `#temp` staging.
v0.15.0 improves efficiency and concurrency without weakening bounded-memory behavior or turning
that result into an unsupported blanket billion-row claim.

> **Branch model:** integration branch is `release/v0.15.0`. Create one feature branch per item or
> phase off `release/v0.15.0` (e.g. `feat/spill-alloc-profiling`), merge back into
> `release/v0.15.0` when the feature's tests are green, and merge `release/v0.15.0` → `main` only
> at release. `main` stays at the last shipped release (v0.14.0).
>
> **Deferred to roadmap:** Phase 4 Central Security Events and Phase 5 Certification & Operations
> remain in `ROADMAP.md`; promote them only if they join this release's scope.

### Grammar-tree suggestions & SQL fuzzer hardening

Fresh-eyes review (2026-07-11) of the grammar state engine (`ETL-SQL.Analysis/Linting/Grammar`)
and the SQL fuzzer (`tests/ETL-SQL.FuzzTests`). Ordered by impact. The first batch below was
implemented and verified on 2026-07-12 (build clean; grammar + suggestion/LSP + fuzzer lanes green);
remaining unchecked items are the deferred follow-ups.

Product goal (better VS Code / TUI suggestions):
- [x] Fix the all-or-nothing suggestion filter in `GrammarLanguageService.GetSuggestionsAsync`.
  Now per-suggestion: expression positions drop statement-structural keywords but keep functions and
  operator/value keywords (`ExpressionKeywords` allowlist). *(Needs UX eyeballing in
  `tools/ui-sandbox`; the allowlist may want widening — CASE/CAST/etc. are covered, exotic ones not.)*
- [ ] Add grammar-vs-parser conformance tests: over the valid sample/help/SLT corpus assert the
  grammar tree accepts everything the production parser accepts (recall), and that grammar-accepted
  sequences parse (precision). Nothing currently measures suggestion precision/recall — the fuzzer
  does not test this.
- [ ] Add suggestion golden tests by cursor position for the main authoring workflows:
  `SELECT` clauses, joins/aliases, CTEs, DML, file operations, `CREATE CONNECTION`, report SQL
  visuals/pages/datasets, `WINDOW`/`OVER`, `QUALIFY`, control flow, jobs, and portal admin commands.
  Each case should assert both expected positive suggestions and high-noise negatives. Current tests
  cover only a few happy paths, so regressions can still make suggestions feel noisy or miss key
  next tokens.
- [ ] Make grammar suggestion failures visible in tests. `GrammarLanguageService` currently catches
  walker/filter errors and returns the base keyword list, and `TokenWalker.GetSuggestions` swallows
  custom suggestion-provider exceptions. Add a strict/test mode or diagnostics hook so grammar bugs
  cannot silently degrade back to broad suggestions.
- [ ] Track grammar coverage for suggestion states, not just parser/fuzzer states: each registered
  start node and labeled transition should be exercised by at least one suggestion test or corpus
  acceptance test. This is the bridge from "the grammar exists" to "the editor uses it reliably."
- [x] Replace the string-probe wildcard detection with an explicit `IsWildcard` flag on
  `StateTransition` — set at construction by `AddWildcardTransition`, and for the inline wildcards
  derived from the `Condition` alone (never `ContextCondition`, the old probe's fragility).
  `GrammarLanguageService` now reads `transition.IsWildcard`.
- [ ] Perf: `RunWalker` re-lexes + re-walks from Root on every keystroke (O(n²) over a typing
  session) and allocates two probe tokens per transition per call. Cache walker state per
  statement/line if suggestion latency is flagged.
- [x] Stop swallowing metadata-provider failures silently in `InjectTableSuggestionsAsync` /
  `InjectColumnSuggestionsAsync` (`catch { }`); now logged at error via `context.Logger`.

SQL fuzzer (`ParserFuzzTests` / `GrammarWalkGenerator` / `QueryMinimizer`):
- [x] Move `RunFuzzer` out of the default lane: `[Trait("Category","Fuzz")]` added, `CLAUDE.md`
  standard command now excludes `Category=Fuzz` and documents a dedicated Fuzz lane. (CI's
  `test-lane.ps1` never ran FuzzTests — only the documented full-solution `dotnet test` did.)
- [x] Make it reproducible: both `Random`s seed from `ETLSQL_FUZZ_SEED` (else `TickCount`), the seed
  is logged on entry and written into every reproducer; reproducer files are named by a stable FNV
  hash (seed-prefixed) instead of the process-randomized `StackTrace.GetHashCode()`.
- [x] Widen `AllowedStatementStarters` (16 → ~36 registered starters, incl.
  `CREATE/ALTER/DROP/EXPORT/COPY/RUN/EXECUTE/…`).
- [x] Wire up the previously-dead DDL/SHOW body generators in `TryGenerateForTransition` — now
  reachable because `CREATE/ALTER/REPLACE` are fuzzed at Root.
- [x] Add a grammar-state coverage counter (`GrammarStateTree.GetAllStates`/`GetTotalTransitionCount`
  + generator `VisitedStates`/`VisitedTransitions`); the fuzzer prints reached-vs-total states and
  transitions. (Seen at ~83% states / ~88% transitions.)
- [x] Feed generated clean queries through `GrammarStateTree.ValidateSequence` and report both
  directions separately: `grammar-rejected-parser-accepted` (recall gap) and
  `grammar-accepted-parser-rejected` (precision gap). **Finding:** the current generator produces
  parser-invalid SQL ~95% of iterations (see `grammar-generated-parser-rejected`), and the grammar
  is looser than the parser on the majority of those — a generator-fidelity gap now measurable.
- [x] Split fuzzer output into buckets: parser-crash, execution-crash, differential-correctness
  (the bug buckets that fail the run) plus parser-diagnostic and the two conformance buckets
  (informational). Replaces the single `crashCount`.
- [x] Tighten the severity net: `IsSevereCrash` no longer treats every `ExecutionException` as
  benign — with `ETLSQL_FUZZ_STRICT_EXEC=1` an `ExecutionException` off the expected-message
  allowlist counts as a bug (ordered after `ConnectionException`, which derives from it). *(Default
  off; `ExpectedExecutionMessageFragments` allowlist must be calibrated from a run before enabling.)*
- [x] Calibrate `ETLSQL_FUZZ_STRICT_EXEC` and commit the expected engine-rejection allowlist
  (6 stable fragments from a 40k dump; explicit `THROW` errors classified structurally). Strict-exec
  is opt-in for the randomized lane (new random seeds keep surfacing new benign "unknown/not-found"
  rejections that would need allowlisting, and broadly allowlisting them would mask real bugs — a
  symptom of the generator-fidelity gap above), and ON with a fixed verified seed in the CI smoke
  lane for continuous semantic-bug signal. Dump new benign messages with `ETLSQL_FUZZ_DUMP_EXEC=1`.
- [x] Stop the NoREC oracle (`VerifyNoRECParity`) from swallowing severe exceptions — severe crashes
  (NRE/cast/index) and mismatches in the rewrite path now propagate instead of being discarded.
- [x] Fix the token↔string round-trip: `QueryMinimizer` now operates on `List<Token>` (AST pruning
  still round-trips through the faithful `ToSql()`; the initial reproduction check and token-level
  delta-debugging use the raw tokens). `WriteReproducer`/`Reproduces` parse the token stream directly.
- [x] Broaden `QueryMinimizer` traversal to `CASE`/unary (`GetSubExpressions` + `ReplaceNode`) and
  strengthen `CorruptQuery` (added duplication, tail-truncation, unbalanced-paren injection).
  *(Residual: `IN`/`CAST`/subquery traversal and bit-flip mutation still open.)*
- [ ] Add a reduced, deterministic fuzzer smoke lane for CI (fixed seed, low iteration count,
  grammar coverage output, no generated repro files unless failing) and keep the longer randomized
  lane opt-in. This gives continuous signal without making normal PR validation noisy.

### v0.15.0 completion gates

- [ ] Publish before/after Gate F allocation, GC, CPU, memory, I/O, and throughput results on the same
  hardware and workload; explain any tradeoff rather than selecting only favorable metrics.
  *(Current caveat: the checked-in `certification-results/gate-f-1b/gate-f-report.json` predates the
  `AllocProfile` scenario and schema v2 source/config fingerprints. Before publishing Gate F
  performance claims or closing a release candidate that changes certified paths, rerun Gate F for
  the current commit and validate it with `Test-GateFEvidence.ps1 -RequiredScenario All`.)*
- [x] PostgreSQL sustained-load and concurrent failure-soak suites pass with documented capacity and
  recovery limits. *(Phase 6 manual-certification evidence for run
  `ha-overnight-20260711-201249` passed sustained PostgreSQL load, four-hour large-job soak,
  fault-injection recovery, and the follow-up HA startup migration-lock retest.)*
- [ ] Normal small/medium regression lanes remain green for the final release candidate.
