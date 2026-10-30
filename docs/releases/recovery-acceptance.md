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
| Failed publication upload preserves package bytes | Actual workflow commands pass local partial-upload and missing-byte fixtures | Hosted draft-only upload and missing-artifact recovery rehearsal |
| Full release completes within four hours | No complete current-candidate elapsed-time proof | Freeze one candidate, collect all required certification and promotion timings, preserve successful jobs across any retry |

Execution order: finish the hosted upload rehearsal, land the reviewed recovery changes, then
measure the complete candidate path. Do not repeat successful historical rehearsals or expand
unrelated hardening to close these requirements. A source change creates a new candidate;
results from different candidates cannot be combined into release certification.

The isolated upload rehearsal uses the retained Windows package artifact from
[native run 40940345271](https://github.com/etl-sql/ETL-SQL/actions/runs/40940345271).
It never publishes a release. Acceptance requires the same source artifact ID and digest,
unchanged successful job execution timestamps, failed-job-only recovery, and downloaded draft
bytes matching the original package manifest. First-attempt failures remain part of the record.

The probe is prepared in local commit `7e17d15cb` on `rehearsal/release-upload-recovery`.
Its two added files pass actionlint, JavaScript syntax validation and diff checks; the production
Windows upload/recovery fixtures pass. Upload is pending explicit authorization after automatic
approval review rejected the GitHub push. No hosted probe result is claimed.
