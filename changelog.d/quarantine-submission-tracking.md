### Fixed

- **Data-quality replay and disposition tracking had never worked.** The queue polled
  `GET /api/jobs/{id}` — the report-execution namespace, backed by `PortalExecutionJobs` — using a
  job id that came from `IJobChannel` and was never in that table. Every poll answered 404, the
  client treated the failure as transient, and it retried once a second for as long as the tab
  stayed open. No submission ever reached a terminal state on screen; the panel promising that jobs
  "remain here until their durable execution reaches a terminal state" showed "status temporarily
  unavailable" forever.

- **A submission's outcome was known only to the browser that made it.** Tracking lived in that
  browser's session storage, so closing the tab lost it. A second steward looking at the same
  quarantine target could not tell that a replay was already in flight — and the obvious next move
  is to submit another replay of the same production load.

### Added

- `GET /api/data-quality/jobs/{jobId}` resolves a submission on the namespace it actually belongs
  to, and reconciles as it reads: a non-terminal record is refreshed from the job channel and the
  outcome written back, so the answer outlives the browser that asked for it.

- Both submission paths now write a durable record to job state
  (`dq:quarantine-submission:<kind>:<target>`) — one per kind per target, bounded on purpose. The
  audit log remains the history of who submitted what; this answers the operational question, which
  is whether something is in flight against this target right now and how the last one ended.

- **A forgotten job reports `Unknown`, never `Failed`.** The in-process channel holds job state in
  memory and answers "Job not found." once the process has restarted. Passing that through would
  tell a steward their replay failed when it may well have completed, and the natural response to a
  failed replay is to run it again. `Unknown` is treated as terminal — further polling cannot
  produce an answer — and is styled as neither success nor failure, because neither was observed.
  A sandbox fixture covers that state alongside the completed one.
