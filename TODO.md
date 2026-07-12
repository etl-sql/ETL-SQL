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

### Enterprise Phase 3 hardening closeout

The main Phase 3 enterprise policy-authority and operation-boundary enforcement work is already
implemented. This closeout list tracks the remaining hardening and retained evidence needed before
calling the enterprise continuation fully current.

- [x] Complete handle-based or equivalent race-resistant `DELETE`, `MOVE`, and `RENAME`
  operations on supported platforms; add link/junction substitution tests at each mutation
  boundary.
- [x] Extend connect-time DNS re-pin, redirect re-authorization, and proxy-bypass controls beyond
  the REST connector to all policy-governed outbound HTTP/network clients, including SharePoint,
  Report Portal, Orchestrator, remote policy/vault access, discovery, and probe paths.
- [ ] Run and retain the deferred performance lane plus Windows and Linux enterprise certification
  evidence, including path/link races, DNS rebinding, redirects, connector aliases, and standalone
  behavior. *(Bundled into the Phase 6 operator runbook/evidence plan via
  `scripts/Test-EnterpriseHardeningCertification.ps1`; run once on Windows and once on Linux/WSL
  for the same run id.)*

### Phase 6: Concurrent, PostgreSQL, and failure soak certification

Design: [ConcurrentPostgresFailureSoak.md](Docs/Design/ConcurrentPostgresFailureSoak.md)

- [x] Run sustained PostgreSQL-backed Portal/Orchestrator load at representative report/job/history
  counts and concurrent execution levels; measure pool saturation, query latency, scheduler fairness,
  lease behavior, and database growth rather than inferring HA performance from SQLite tests.
  *(CI-smoke evidence exists under `certification-results/postgres-ha-soak/ha-agent-20260711-01/`.
  Manual-certification evidence exists under
  `certification-results/postgres-ha-soak/ha-overnight-20260711-201249/`: Portal and Orchestrator
  sustained-load lanes passed at concurrency 1/10/25 with 0% errors and no SQLite contention.)*
- [x] Add multi-hour concurrent large-job soaks covering mixed scan, spill, join, and sort workloads
  under shared memory and disk budgets, including cancellation at each spill phase.
  *(CI-smoke evidence exists under `certification-results/ha-large-job-soak/ha-agent-20260711-01/`.
  Manual-certification evidence exists under
  `certification-results/ha-large-job-soak/ha-overnight-20260711-201249/`: the four-hour
  mixed scan/spill/sort/join/aggregate scenario and cancellation-at-phase checks passed.)*
- [x] Inject disk-full/low-space, slow disk, corrupt or incomplete extent, process crash, restart,
  orphan cleanup, and temp-root exhaustion; verify bounded recovery with no leaked grants, handles,
  extents, or silently duplicated/lost mutations.
  *(CI-smoke evidence exists under
  `certification-results/ha-fault-injection/ha-agent-20260711-01/`. Manual-certification evidence
  exists under `certification-results/ha-fault-injection/ha-overnight-20260711-201249/`: all ten
  destructive/manual HA recovery scenarios passed with cleanup checks.)*
- [ ] Review and fix the recovered Portal startup migration race captured in
  `.ha-soak-runs/ha-overnight-20260711-201249/diagnostics/20260712-053400/docker-compose-logs.txt`
  (`42P07: relation "AuditOutboxMessages" already exists`). The topology recovered and both portal
  nodes were up at diagnostic capture, but concurrent HA startup should avoid noisy failed migration
  attempts.

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
- [ ] Replace the string-probe wildcard detection (`___wildcard_test_*___`) with an explicit
  `IsWildcard` flag on `StateTransition`; probing is fragile against `ContextCondition`. *(Deferred:
  invasive — 59 inline wildcards in `DefaultGrammar` would each need the flag.)*
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
- [ ] Add a grammar-state coverage counter so "N iterations" carries a defensible reached-vs-total
  number and any remaining dead branches show up.
- [x] Tighten the severity net: `IsSevereCrash` no longer treats every `ExecutionException` as
  benign — with `ETLSQL_FUZZ_STRICT_EXEC=1` an `ExecutionException` off the expected-message
  allowlist counts as a bug (ordered after `ConnectionException`, which derives from it). *(Default
  off; `ExpectedExecutionMessageFragments` allowlist must be calibrated from a run before enabling.)*
- [x] Stop the NoREC oracle (`VerifyNoRECParity`) from swallowing severe exceptions — severe crashes
  (NRE/cast/index) and mismatches in the rewrite path now propagate instead of being discarded.
- [ ] Fix the token↔string round-trip: the fuzzer parses the generated `List<Token>` but the
  minimizer/NoREC path re-lexes a space-joined string, so synthetic single-token identifiers like
  `src.Users` re-lex differently and repros may not reproduce. Operate on the original tokens.
- [x] Broaden `QueryMinimizer` traversal to `CASE`/unary (`GetSubExpressions` + `ReplaceNode`) and
  strengthen `CorruptQuery` (added duplication, tail-truncation, unbalanced-paren injection).
  *(Residual: `IN`/`CAST`/subquery traversal and bit-flip mutation still open.)*

### v0.15.0 completion gates

- [ ] Publish before/after Gate F allocation, GC, CPU, memory, I/O, and throughput results on the same
  hardware and workload; explain any tradeoff rather than selecting only favorable metrics.
  *(Current caveat: the checked-in `certification-results/gate-f-1b/gate-f-report.json` predates the
  `AllocProfile` scenario and schema v2 source/config fingerprints. Before publishing Gate F
  performance claims or closing a release candidate that changes certified paths, rerun Gate F for
  the current commit and validate it with `Test-GateFEvidence.ps1 -RequiredScenario All`.)*
- [ ] PostgreSQL sustained-load and concurrent failure-soak suites pass with documented capacity and
  recovery limits, and the normal small/medium regression lanes remain green.
