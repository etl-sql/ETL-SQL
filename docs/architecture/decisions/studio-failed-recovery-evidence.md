# Studio Degraded Editor and Failed Session Recovery

**Tasks:** ST38–ST41. **Status:** completed for the fixtures below on 2026-10-13.

The evidence uses Chromium against real Portal and desktop Kestrel hosts. Browser routing injects
module failures, an unanswered session request, HTTP 500 and disconnection. Successful retries use
the actual host assets and API responses. Authentication expiry uses a correctly signed access
token whose expiry is an hour in the past, with the user's real refresh session retained.

## Degraded editor: ST38 and ST39

`FailedEditorModuleKeepsTwoBuffersAndRecoversWithoutReload` runs once per host:

- Blocking the CodeMirror bundle produces a named basic textarea and a visible Retry action.
- Two document buffers are edited and switched; each retains its own text and dirty state.
- Formatting is disabled. A visual/helper write is refused with an explanation and leaves the
  buffer unchanged, without submitting a patch.
- Cursor-based planning explains its unavailability instead of silently assuming line one.
- A second failed Retry leaves the existing textarea editable and preserves its text.
- During a successful Retry, the module response is held while the author types, switches tabs
  and edits the other buffer. Both latest buffers survive replacement with CodeMirror.
- Formatting becomes available again. Recovery needs no page reload.

The shared loader clears rejected promises and changes the module URL on subsequent attempts, so
the browser cannot reuse a failed import. Editor replacement is prepared separately, then reads
the current document when ready. Existing basic editing remains usable while loading. Repeated
Retry clicks share one mount, and stale editor states cannot overwrite the recovered buffers.

Canonical visual, pipeline and governance helper writes are unavailable while degraded; typing,
tab switching and the existing save paths remain available. Formatting and diagnostic repairs
retain their existing degraded-mode guards. This fixture certifies text preservation, not retention
of a CodeMirror undo history across a failed initial load.

The `degraded-editor` UI sandbox fixture and
`Studio_DegradedEditorStoryRetriesWithTheLatestTypedText` exercise the same loader and Retry
controls using mock host data. They supplement the real-host journeys.

## Bounded Portal startup: ST40

`PortalStartupFailureIsBoundedAndRetryUsesRealServerCapabilities` covers three independent cases:
an unanswered `/api/studio/session`, HTTP 500, and transport disconnection.

Session and initial catalog reads share a 15-second deadline and AbortSignal. A timeout explains
the deadline and offers **Retry Connection**. Failure creates no Studio instance or Run control;
there is no fallback capability grant. Retry reloads the requested page and initializes Studio
from the real server's capability set, which the test compares to the mounted instance.

The deadline covers session and catalog loading. These tests do not certify a stalled module
download or every host outage. The module fixture above covers a rejected download.

## Expiry, re-authentication and drafts: ST41

`StartupAccessExpiryAndFailedRefreshRecoverTheAuthenticatedUsersKeptDraft` first edits a catalog
report and waits for the existing draft endpoint to acknowledge the kept text. It then expires
access at the next document's startup, avoiding a fixture-induced refresh during navigation.

- With a valid refresh session, the Portal rotates credentials and opens the requested report.
- When refresh is refused, credentials are cleared and the browser reaches sign-in. The same user
  signs in again, encounters a controlled startup failure, and uses Retry Connection successfully.
- In both cases the displayed identity is the authenticated user, mounted capabilities exactly
  match a fresh server response, and the existing recovery offer restores the kept text as dirty.

Identity rendering now follows successful session verification, so an expired or malformed access
token cannot stop startup before the refresh path runs or leave a stale identity label behind.
No draft store, draft API or storage policy was added.

## Validation

Eleven browser cases passed without skips: seven new real-host failure/recovery cases, the recovery
sandbox story, and the three existing crash/expiry/conflict recovery journeys. The Node mutation,
Portal HTTP authentication and authoring contract checks passed, including rejected degraded writes
and protection against late refresh replacing a new identity. Strict browser type, lint and
prepared-asset gates passed.

Local evidence: `artifacts/closed-todo-audit/section8-final-recovery.trx` and `section8-*.log`.
The executable fixtures are `StudioFailedRecoveryJourneyTests`, `StudioRecoveryJourneyTests` and
the named sandbox case in `SandboxStoryTests`.
