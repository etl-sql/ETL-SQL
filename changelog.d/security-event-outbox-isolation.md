### Fixed

- **The security-event outbox was missing from the departmental isolation contract**, and its
  default is a **machine-wide** path under `LocalApplicationData` shared by every ETL-SQL process on
  the host. Two environments on one machine therefore wrote their security events into a single
  queue — a cross-environment leak of exactly the records isolation exists to keep apart, and the
  only resource in the contract whose default is *wrong* rather than merely unset.

  It is now:

  - a planned isolated resource in `GET /api/admin/environments/plan`, with the
    `ETLSQL_SECURITY_EVENT_OUTBOX_PATH` override named;
  - reported in the current-environment evidence, so an operator can see whether their own
    deployment has set it;
  - documented in `Departmental_Isolation.md` and `security-events.md`, including the co-located
    Portal/Orchestrator case;
  - pinned by a test, because a plan that lists databases and key rings while omitting this one
    reads as complete.

  Found empirically rather than by review: it was what made the browser test lane fail whenever two
  processes started back to back, one unable to open the file the other still held.
