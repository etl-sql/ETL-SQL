# Release recovery acceptance

The current priority is a release within four hours, ordinary scoped PR validation within
30 minutes, and recovery that preserves successful work for an unchanged candidate.
Queueing, execution and time waiting for an operator must be reported separately.

| Requirement | Evidence | Remaining work |
| :--- | :--- | :--- |
| Scoped PR within 30 minutes | [PR 48 CI](https://github.com/etl-sql/ETL-SQL/actions/runs/40872610604): 19m21s, browser-test scope | Measure other path families; this is not a full-CI timing claim |
| Failed browser shards preserve successful jobs | [Portal rehearsal](https://github.com/etl-sql/ETL-SQL/actions/runs/40609095598): original successful timestamps retained, recovery 19m43s | No repeat of this historical rehearsal needed |
| Failed engine group preserves other groups | [Engine rehearsal](https://github.com/etl-sql/ETL-SQL/actions/runs/40754693409): recovery 14m35s | No repeat of this historical rehearsal needed |
| Failed MSI profile reuses installer and successful profile | [MSI rehearsal](https://github.com/etl-sql/ETL-SQL/actions/runs/40760871492): original artifact 12850372732, recovery 7m33s | Runner-loss cause remains separate from successful recovery |
| Corrected DEB diagnostics survive upload | [DEB acceptance](https://github.com/etl-sql/ETL-SQL/actions/runs/40494686777): 39 installed assertions and diagnostic upload pass | Final candidate still needs its own certification |
| Failed publication upload preserves package bytes | [Hosted rehearsal](https://github.com/etl-sql/ETL-SQL/actions/runs/41144863470): both failed jobs recover in 3m58s; original artifact and successful executions retained | Final-candidate promotion remains separate |
| Full release completes within four hours | No complete current-candidate elapsed-time proof | Freeze one candidate, collect all required certification and promotion timings, preserve successful jobs across any retry |

Execution order: land the reviewed recovery changes, then measure the complete candidate path.
Do not repeat successful historical rehearsals or expand
unrelated hardening to close these requirements. A source change creates a new candidate;
results from different candidates cannot be combined into release certification.

The isolated upload rehearsal uses the retained Windows package artifact from
[native run 40940345271](https://github.com/etl-sql/ETL-SQL/actions/runs/40940345271).
It never publishes a release. Acceptance requires the same source artifact ID and digest,
unchanged successful job execution timestamps, failed-job-only recovery, and downloaded draft
bytes matching the original package manifest. First-attempt failures remain part of the record.

The probe ran at commit `7e17d15cb` on `rehearsal/release-upload-recovery`. Attempt one stopped
after one real package upload (exit 23) and rejected a missing artifact. One failed-job-only retry
passed both jobs in 238.311 seconds. The two successful setup jobs retained their original
execution timestamps. Source artifact `12928589167` retained its ID, size and SHA-256 digest;
downloaded draft bytes matched its original manifest. No build or public release occurred.

The 10,649.689-second pause before retry came from approval-review availability and operator
continuation; it is recorded separately, not removed from total elapsed time. The historical
receipt is `certification-results/release-upload-recovery-2026-10-31.json`. Raw attempts,
diagnostics and the executable audit are in `release-validation/upload-recovery-41144863470/`.
