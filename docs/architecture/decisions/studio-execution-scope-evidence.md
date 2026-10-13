# Studio Execution Scope, Cancellation and Helper Preview Evidence

**Tasks:** ST30–ST37. **Status:** completed on 2026-10-13 for the bounded fixtures below.

The journeys use Chromium against real loopback Kestrel hosts: authenticated Portal Studio and
the desktop Workstation Editor. Portal cancellation also uses a real federated Orchestrator.
Transport loss is injected after the real server has returned its result. The UI sandbox is
additional interaction coverage; its mock rows are not execution evidence.

## Execution scope: ST30 and ST31

`StudioExecutionScopeJourneyTests.ScopeSelectionAndMutatingPrefixConsentAgreeWithTheHost`
runs once per host and proves:

- An empty buffer with no selection sends no execution request and explains the missing scope.
- Selecting the first statement returns `Chosen = 13`; the later `SELECT 99` is absent from the
  submitted selection.
- An unresolved script is explained in the Engine panel and sends no plan request.
- A prefix containing a remote `CREATE TABLE` is shown as mutating, alongside the staged `#stage`.
  Planning without consent is refused locally and sends no request.
- Accepting consent sends precisely the displayed prefix plus `EXPLAIN` of the selected query.
  Desktop returns a real plan. Portal refuses the remote prefix through its interactive allow-list;
  the panel displays that server refusal. Consent does not grant Portal write authority.

The Engine panel now forwards consent to desktop's destructive-statement guard and displays a
`success: false` execution response as a refusal instead of an empty plan.

## Cancellation and lost responses: ST32 and ST33

`StudioPipelineRunJourneyTests.StopReachesTheRunAndStudioSaysOnlyWhatWasConfirmed` submits a
saved Portal pipeline containing a 40-second wait, stops it through the authenticated proxy, and
checks the Orchestrator's `CANCELLED` history row. The run ID remains visible. It also drops a stop
request and proves that Studio reports the missing confirmation while following the eventual
`SUCCESS`, without claiming that run was cancelled.

`DesktopStopCancelsAnExecutingServerRunAndRetainsUnconfirmedClientOutcome` waits until the real
desktop host owns an active 40-second run, then presses Stop. The active run is released within the
10-second observation budget. Studio retains the run ID and correctly says client cancellation
is unconfirmed, because aborting a response is not a server acknowledgement. Desktop history keeps
the same ID for support, including runs interrupted by cancellation.

`AResponseLostAfterRealExecutionKeepsUnknownOutcomeAndDocumentOwnership` runs once per host.
The server executes `SELECT 17`, returns a result and its correlation ID, and only then is its
response dropped. The author switches to a second document before the loss is delivered. The
original document retains `Unknown` and the ID; the second document's trace and results panel do
not inherit them. Studio no longer labels this transport failure as a proven failed execution.
Portal interactive audit entries also retain the correlation ID.

## Native helper fixture: ST34 and ST35

`NativeSqlitePreviewRunsOnlyItsQueryAndAcceptsAnEmptyResult` uses the same SQLite fixture in
both hosts: a disposable database containing two `Users` rows, opened with `Mode=ReadOnly`.
Portal gets the connection through its authenticated catalog; desktop gets it from the document's
connection declaration. No credentials are embedded.

The canonical query helper submits `EXECUTE <alias> BEGIN … END` and proves `sqlite_version()`
executes in SQLite's native context. A nonempty query returns both rows. A query matching no rows
is shown as a successful zero-row result. A 4,096-row cross join remains within the host's output
row limit (desktop default 100; Portal maximum 1,000). A `DELETE` preview is refused, and a direct
database read confirms both rows remain. The parent document's 40-second wait and later query
are absent from the submitted preview.

The desktop helper also caps serialized result rows at 1 MiB, including array delimiters and row
separators. A single 1.2-million-character SQLite result is omitted and marked capped; two
600,000-character rows return only one. The helper displays the cap warning and the number of rows
actually sampled. An omitted oversized row is not presented as a successful empty query. This is
a result-row budget, not a limit on the entire HTTP response including diagnostics and metadata.

Native helper support is deliberately bounded to direct SQLite connections opened read-only and
one SELECT accepted by the read-only query validator. Other dialects, Gateway bindings, parameters,
output tables and write statements are refused. This evidence does not certify those cases.
The ordinary Portal interactive allow-list still refuses arbitrary remote blocks.

## Explicit staged context: ST36 and ST37

`EnginePreviewUsesExplicitVariableAndStageContextAndRefusesUnavailablePredecessors` runs in
each host. The helper's **Include declared variables and read-only staging above line** control
selects the explicit prefix:

```sql
DECLARE @minimum INT = 2;
SELECT 2 AS Id INTO #stage;
```

Previewing `SELECT Id FROM #stage WHERE Id >= @minimum` returns exactly `Id = 2`. The later
statement is not executed. Adding a `WAITFOR` predecessor makes context unavailable, explains why,
and executes nothing. Turning context off does not silently reconstruct the stage or variable.

The shared `HelperPreviewPolicy` validates the entire attempt before execution, permits only
nonsensitive variable declarations and read-only temp staging in its explicit prefix, and limits
the attempt to 25 statements. Portal additionally enforces its catalog and statement policy;
both hosts retain their memory and timeout limits. A fresh execution session owns every attempt.

## Validation

- 11 browser tests passed with no skips: both-host scope, lost response, native and staged helper
  cases; desktop Stop; real Portal/Orchestrator Stop; and the helper-context sandbox story.
- Eight helper-policy regressions passed, including refused write targets and unavailable prefixes.
- 60 Portal policy, data-preview, authoring and generated-contract tests passed.
- Browser type and lint gates, existing query-helper/scope checks, and prepared-asset verification
  passed.
- Follow-up byte-budget checks passed in both native/staged helper fixtures and the sandbox story
  (five browser cases). Ordinary desktop execution regressions were checked after the row conversion
  change. The byte budget applies only to helper results.

Local TRX files are under `artifacts/closed-todo-audit/section7-*.trx`. The reproducible tests live
in `StudioExecutionScopeJourneyTests`, `StudioHelperPreviewJourneyTests`, `HelperPreviewPolicyTests`
and the named Portal cancellation and sandbox cases above.
