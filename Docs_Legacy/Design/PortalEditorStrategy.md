# Design Strategy: First-Class Web Script Editing in the Report Portal

As ETL-SQL scales into enterprise farms (multiple orchestrators/portals) and SaaS/multi-tenant
models, the developer profile shifts. Local developers keep VS Code and the TUI, but centralized
enterprise users and SaaS tenants often **cannot or will not install local desktop tools**. They
expect to author, lint, test, schedule, and secure pipelines entirely in the browser — inside the
portal's security boundary, using the connection catalog and secret vault that already live there.

This document sets the strategy for elevating the Portal's script editor to a first-class experience
and lays out the implementation in independently shippable, bite-size chunks.

> **Reviewer's note (2026-07-14):** An earlier draft framed this as a choice between three packaged
> options and recommended *Monaco + a per-session Language Server*. That recommendation was
> reassessed against the architecture we have actually shipped (bounded-resource governance, memory
> arbiters, leases, per-node heartbeats, connection ACLs, RLS, the audit outbox) and against the code
> already in the repo. The conclusion changed. The rationale is preserved in §2 so the pivot is
> auditable. The chosen direction is **CodeMirror 6 + stateless server-side analysis + a schema API**.

---

## 1. Verdict: yes, elevate the editor

A "blind" text box with color highlighting is not enough for a premium SaaS/enterprise product. But
we do **not** need to duplicate VS Code. The value we must deliver in the browser is:

- **Real-engine diagnostics** — the same lint/parse results a user gets in VS Code and the CLI.
- **Schema-aware autocomplete** — tables/columns from the connections they are authorized to use.
- **Safe interactive runs** — bounded, audited, run under the user's security context.

Now that secrets, the connection catalog, RLS, and Governance Core live inside the portal, authoring
*within that boundary* is a genuine feature, not a nicety — it is the secure alternative to punching
local VS Code instances through the firewall to production databases.

---

## 2. Decoupling the decision (why not Monaco/Wasm/vscode.dev)

The earlier three "options" conflated three **independent** decisions. Separating them is what makes
the right path obvious:

| Decision axis | Choices | Our pick | Why |
| :--- | :--- | :--- | :--- |
| Editor engine | CodeMirror 6 · Monaco | **CodeMirror 6** | Already shipping (`Shared/designer/codemirror`). Monaco is a rewrite of a working editor for marginal gain; CM6 supports async lint + autocomplete natively. |
| Where analysis runs | Client (Wasm) · Server | **Server** | The parser/linter is C# in `ETL-SQL.Core`/`ETL-SQL.Analysis`. Wasm ships a multi-MB .NET runtime + a second build target for a *latency* win, not a capability. |
| Session model | Persistent LSP session · Stateless request | **Stateless request** | A process-per-session C# Language Server is exactly the unbounded, noisy-neighbor, cross-tenant-isolation liability our governance model exists to prevent. |

Consequences for the packaged options in the earlier draft:

- **Monaco + per-session LSP — rejected.** Spawning a stateful `ETL-SQL.LanguageServer` **process per
  active browser session** on a shared farm contradicts bounded-resource governance; "kill after 10
  min idle" is a band-aid. Our `LanguageServer` is also a thin **stdio shell for one local VS Code
  user** (per-instance `DocumentStateStore`); it is not a multi-session server component. The reusable
  core is `ETL-SQL.Analysis`, invoked as a library — which is exactly what the stateless endpoints do.
- **vscode.dev in an iframe — deferred.** By its own description it *still* needs the C# LSP in Wasm,
  so it inherits the Wasm cost **plus** iframe/PWA/branding/licensing complexity.
- **Wasm client-side parser — deferred (not rejected).** Its stated weakness ("no schema autocomplete
  without network calls") is a false constraint: schema autocomplete is *always* a network call in any
  web editor, because schemas live in the catalog/DB, not the browser. Wasm's real trade is a heavy
  payload for zero-latency *local* lint. Keep it on the shelf as a future latency optimization.

**Chosen approach: CodeMirror 6 + stateless server-side analysis + a schema API.** It reuses shipping
code, is horizontally scalable (fits the LB/HA/farm model), runs inside the portal security boundary,
and honors bounded-resource governance instead of fighting it. We already prove the pattern:
`POST /api/designer/parse` runs the real C# parser statelessly over HTTP today.

---

## 3. Target architecture

```
Browser (CodeMirror 6)                         Portal Server (stateless, per-request)
──────────────────────────                     ─────────────────────────────────────────
  edit buffer
    │  debounced POST /api/designer/analyze ──▶ ETL-SQL.Analysis Linter + Core Parser
    │                                    ◀────── AnalysisDiagnostic[]  (squiggles + gutter)
    │
    │  POST /api/designer/complete ───────────▶ completion engine + cached schema snapshot
    │        (script, line, col, connRef) ◀──── CompletionItem[]      (autocomplete popup)
    │
    │  POST /api/designer/run  ───────────────▶ engine run, TOP 100 clamp, 15s timeout,
    │        (selection, connRef)               user security ctx (RLS), MemoryGrantArbiter
    │                                    ◀────── capped result grid  +  AD_HOC_RUN audit row
    │
    └─ POST /api/designer/save ───────────────▶ portal script store  (or git commit-on-behalf)
```

No persistent per-session process. Every call is authenticated, authorized against connection-use
ACLs, rate-limited, and stateless — position and connection reference travel *in the request*, not in
server-side document state.

---

## 4. Implementation — bite-size chunks

Each chunk is independently shippable and testable. Phases A→B deliver the "intelligent assistance"
promise; C→D add safe interactive execution and source control; E is polish. Reuse is called out per
chunk so we are not rebuilding what already ships.

### Phase A — Real-engine diagnostics (highest value, lowest risk)

- [x] **A1 — `POST /api/designer/analyze` endpoint.** New action alongside `DesignerController.Parse`.
  Request `{ script, dialect?, connectionRef? }`; tokenize → `Core.Parser.Parser.Parse()` →
  `LinterFactory.CreateWithAllRules(sp)` → `Linter.AnalyzeAsync(script, context)`. Map results to a
  wire shape mirroring `AnalysisDiagnostic` (`startLine/startColumn/endLine/endColumn/severity/message/
  code/source`). Authenticated, `[EnableRateLimiting]`, stateless, no process spawned. *Reuse:*
  `ETL-SQL.Analysis` (~50 rules incl. `SchemaValidationRule`, `GovernancePolicyRule`,
  `CredentialLeakRule`, `AvoidSelectStarRule`), and the `/parse` request/DI pattern.
- [x] **A2 — CodeMirror lint source.** Extend the canonical `Shared/designer/codemirror` bundle to
  include `@codemirror/lint`; add a debounced (~400 ms), cancel-on-keystroke async linter that POSTs
  the buffer to `/analyze` and maps diagnostics to CM `Diagnostic[]` (severity → squiggle + gutter
  marker + hover message). Edit the canonical asset, then `node scripts/sync-assets.js`.
- [x] **A3 — Parity test.** Golden test: a fixture script produces byte-identical diagnostics through
  `/analyze` and through the CLI `lint` path, proving the "same engine as VS Code" promise and
  guarding against drift.

### Phase B — Schema-aware autocomplete

- [x] **B1 — Shared, cached schema snapshot service.** `GET /api/designer/schema?connection=<ref>`
  returns `{ tables:[{ name, columns:[{name,type}] }] }` for a connection the caller is **authorized to
  use**. Schema is a property of the *connection/database, not the user*, so it is introspected **once
  per connection and cached server-side, then served to every editor session** — the VS Code/TUI
  local-cache model lifted to a shared server cache, instead of re-pulling per user or per keystroke.
  Design rules:
  - **The cache is shared; authorization is not.** Key the cache by the connection's *catalog identity*
    (never a user-supplied name), but run the connection-use ACL check on **every** request before
    serving from the warm cache. A user who cannot use a connection must never receive its schema.
  - **Staleness = TTL + explicit invalidate, stale-while-revalidate.** Re-pull on a TTL (minutes–hours;
    schema changes rarely), invalidate on connection edit, and expose an admin "refresh schema" action.
    Serve the cached snapshot immediately and refresh in the background when past TTL so no single user
    eats the full introspection latency. Slightly-stale autocomplete is low-harm (a brand-new column is
    briefly not suggested) — the same behavior as the local caches today.
  - **Cache location follows HA.** Start with a **per-node in-memory** cache (each portal node
    introspects once; trivial duplication, harmless cross-node window). Promote to a **shared
    DB-backed snapshot** (Postgres, fitting the existing database-backed-state pattern) only if
    introspection cost or cross-node drift becomes a real problem.

  *Reuse:* connection catalog + ACLs; the existing schema-suggestion metadata
  (`SuggestionType.Table/Column`). *Shipped:* the Portal schema endpoint resolves the shared
  connection through the Portal catalog on every request, warms the existing metadata manager's
  per-node cache under a user/connection/document-scoped URI, and returns only non-secret
  table/column metadata.
- [x] **B2 — Stateless completion endpoint + CM source.** `POST /api/designer/complete { script, line,
  column, connectionRef }` runs the existing completion/suggestion engine server-side (position in the
  request, no synced document) and returns `CompletionItem[]`. It is a **pure function over the B1
  cached snapshot** + parse position — it never introspects the database live on a completion request.
  Wire CodeMirror `@codemirror/autocomplete`
  as an async source that is context-aware (after `FROM`/`JOIN` → tables; after `alias.` → columns;
  otherwise keywords/functions). *Reuse:* `CompletionProvider` logic from `ETL-SQL.LanguageServer`,
  refactored into an `ETL-SQL.Analysis` service callable without an LSP session. *Shipped:* the
  endpoint reuses `GrammarLanguageService` directly and the shared CodeMirror bundle now includes
  `@codemirror/autocomplete`.
- [x] **B3 — Completion governance.** Suggestions expose only connections/tables the caller may use;
  never surface schema for unauthorized connections and never surface secret values. Add a test that
  an unauthorized `connectionRef` yields `403`/empty, never a schema leak.

### Phase C — Interactive "Run selection" (the genuinely risky part — govern it hard)

- [x] **C1 — `POST /api/designer/run` with server-enforced limits.** Execute one selected read-only
  `SELECT`/set-query under a server-clamped 100-row retained result cap, a 15 s timeout, and a strict
  per-run operator memory grant. *Reuse:* the engine execution path + memory arbiter; cancel via the
  request-linked execution token.
- [x] **C2 — Caller security context.** Run as the logged-in portal user: apply RLS predicates and
  identity vars (`@@CURRENT_USER`, `USER_GROUPS()`), and resolve the connection + secrets from the
  catalog/vault — **never** from client-supplied credentials. *Reuse:* RLS scan + identity var
  plumbing; `SECRET:` resolution.
- [x] **C3 — Audit every run.** Emit an `AD_HOC_RUN` audit event (actor, connection, sanitized query,
  row count, elapsed) through the durable audit outbox. *Reuse:* `AuditService` + outbox.
- [x] **C4 — Result panel.** Render a capped shared designer result grid showing the 100-row cap and
  timing. *Reuse:* `Shared` report-runtime styling.

### Phase D — Source-control write-back (only when a git backend is configured)

- [x] **D1 — Commit-on-save.** When the portal has a git backend, `POST /api/designer/save` commits on
  behalf of the user (commit author = portal identity; push via a service token), preserving the
  "source-controlled report" promise. When no git backend, save to the portal script store as today.
- [x] **D2 — Concurrency safety.** Track the base revision the edit started from; on save detect a
  changed head and surface a refresh/merge path — never silently overwrite a newer commit.
- [x] **D3 — Audit + authz.** Saving/committing is authorized (author role) and audited; secrets are
  never written into committed script text.

### Phase E — Editor UX polish (CodeMirror, not Monaco)

- [x] **E1 — First-class editing affordances.** Bracket matching, keyword/function highlight (extend
  the designer language mode), a diagnostics panel listing A1 results, and format-on-save if/when a
  formatter exists. *Shipped:* the shared workbench has bracket matching/highlighting, the diagnostics
  panel, and a protocol-style results panel; formatting remains intentionally absent until a formatter
  exists.
- [x] **E2 — Commands & keybindings.** Command palette + shortcuts for Run selection, Format, and
  Save/commit, mirroring the VS Code command names so muscle memory transfers. *Shipped:* the shared
  Portal/sandbox workbench supports `Ctrl/Cmd+Shift+P`, `Ctrl/Cmd+Enter`, and `Ctrl/Cmd+S` when save
  is available; Format appears only when a host supplies a formatter.

---

## 5. Cross-cutting guardrails (SaaS / multi-tenant)

These apply to every chunk above and are the part that most deserves scrutiny — interactive execution
in a shared tenant is where the real risk lives, not the editor engine.

1. **Design-time throttling is server-enforced.** `TOP 100`, the 15 s timeout, and the memory ceiling
   live on the server (C1); the client editor is never trusted to impose them.
2. **Every interactive run is audited and identity-bound.** `AD_HOC_RUN` under the caller's security
   context with RLS applied (C2/C3).
3. **Authorization rides existing ACLs.** Analyze/complete/run/schema all check connection-use ACLs and
   never expose a connection, schema, or secret the caller is not entitled to.
4. **Statelessness is a requirement, not an accident.** No per-session server process and no server-held
   document — this is what lets the editor scale behind the load balancer across farm nodes.

---

## 6. Explicitly deferred (revisit on evidence, not by default)

- **Wasm client-side parser** — reconsider only if measured `/analyze` round-trip latency is a real
  annoyance. It is a latency optimization for syntax-only lint, at the cost of a .NET-on-Wasm payload
  and a second compilation target.
- **Monaco swap** — reconsider only on a concrete CodeMirror 6 limitation we actually hit.
- **vscode.dev iframe** — parked; inherits the Wasm cost plus PWA/iframe/licensing complexity.
- **True incremental LSP** (live hover, go-to-definition, multi-file) — if demand appears, back it with
  a **pooled, stateless analyzer service**, never a process-per-session. The LSP protocol is fine; the
  process-per-session model is the part we are avoiding.

---

## 7. Sequencing

Ship **A → B** first: that alone converts the "blind text box" into an editor with the same
diagnostics and schema awareness as VS Code, entirely within the portal boundary, with no new
long-lived server resources. **C** unlocks interactive validation but carries the security/resource
weight, so it lands only once its guardrails (§5) are in place. **D/E** are incremental polish. Wasm
and Monaco stay on the shelf unless evidence pulls them forward.

---

### References
- [Language Server Architecture](../Architecture/LanguageServer.md)
- [Portal UI — Lite Editor & technology choices](../Architecture/PortalUI.md#5-technology-choices)
- [Report Portal architecture (auth, API, middleware)](../Architecture/ReportPortal.md)
- [Zero-Trust Operations & Security](../Standards/Connectors_Standards.md)
