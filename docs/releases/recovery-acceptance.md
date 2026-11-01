# Release recovery acceptance

The current priority is a release within four hours, ordinary scoped PR validation within
30 minutes, and recovery that preserves successful work for an unchanged candidate.
Queueing, execution and time waiting for an operator must be reported separately.

| Requirement | Evidence | Remaining work |
| :--- | :--- | :--- |
| Scoped PR within 30 minutes | [PR 48 CI](https://github.com/etl-sql/ETL-SQL/actions/runs/40872610604): 19m21s, browser-test scope | Measure other path families; this is not a full-CI timing claim |
| Broad proposal timing | [PR 51 CI](https://github.com/etl-sql/ETL-SQL/actions/runs/41267322023): 26m31s; [native packages](https://github.com/etl-sql/ETL-SQL/actions/runs/41267322024) finish last at 49m18s from proposal run creation | The 30-minute target is not met for this package-affecting PR; release-only lanes and later version changes need their own evidence |
| Windows browser mitigation stability | [Proposal CI](https://github.com/etl-sql/ETL-SQL/actions/runs/41267322023), [second inventory](https://github.com/etl-sql/ETL-SQL/actions/runs/41268947571), [third inventory](https://github.com/etl-sql/ETL-SQL/actions/runs/41270889034): three complete first-attempt inventories at `fbcbccf85` | Does not establish the cause of every historical timeout |
| MSI diagnosis without rebuilding | [Retained-input Portal diagnostic](https://github.com/etl-sql/ETL-SQL/actions/runs/41271704106): original installer artifact `13061996390`, no build or certification receipt | Earlier stalls remain unexplained; this tests retained 0.21.0 bytes, not the 0.22.0 upgrade path |
| Windows publishing shared by MSI and native packages | [Shared producer and MSI](https://github.com/etl-sql/ETL-SQL/actions/runs/41327890827), [native Windows acceptance](https://github.com/etl-sql/ETL-SQL/actions/runs/41327890886) at `b50e6828a`: producer artifact `13093406015`; both consumers verify the same payload hash; MSI packaging takes 2m51s after one 27m35s publish | Full release timing remains unmeasured; Portal uninstall is a separate failure |
| Broad shared-payload proposal timing | [Native certification](https://github.com/etl-sql/ETL-SQL/actions/runs/41327890886) passes all four platforms in 60m59s; Intel ZIP/VSIX building takes 47m26s | The broad package-affecting PR still misses 30 minutes; Intel publishing remains the measured critical path |
| Retained 0.22.0 uninstall diagnosis | [Diagnostic 41332302124](https://github.com/etl-sql/ETL-SQL/actions/runs/41332302124): original artifact `13088976661`, fresh install/uninstall, upgrade, rollback and cleanup pass with Restart Manager shutdown disabled | The control below also passes without the override; the authoring experiment is removed. Diagnostic success is not release certification |
| Restart Manager control comparison | [Default control 41338653763](https://github.com/etl-sql/ETL-SQL/actions/runs/41338653763) uses the same script and retained artifact without the override and also passes all 32 assertions in 4m10s | Restart Manager alone does not explain the failure. Normal `88d65e72c` again stops reporting at uninstall despite the compiled property; isolate diagnostic logging and wait behavior |
| Quiet buffered polling | [Control 41341896065](https://github.com/etl-sql/ETL-SQL/actions/runs/41341896065) passes all 32 assertions in 4m08s on the same retained artifact, using ordinary buffered logging, no Restart Manager override and no diagnostic runtime callbacks | Validate bounded polling in the new normal candidate. Historical runner-loss causes remain unproven |
| Normal bounded-polling candidate recovery | [MSI 41343571100, attempt 2](https://github.com/etl-sql/ETL-SQL/actions/runs/41343571100/attempts/2) at `2ff552ae9` passes after failed-job-only recovery. Portal passes all 32 assertions in 5m15s; receipt retains installer pair `13099299211`. Producer, MSI build and workstation preserve their original successful execution times | Attempt one lost runner communication after upgrade start and remains part of the record. Polling has not established a runner-loss fix. This PR run proves recovery on unchanged inputs; publication requires eligible frozen release-branch certification |
| Exact candidate input comparison | [Observed diagnostic 41358524551](https://github.com/etl-sql/ETL-SQL/actions/runs/41358524551) uses script `2ff552ae9` and retained artifact `13099299211`, passing all 32 assertions in 4m38s without rebuilding. [Quiet control 41357097184](https://github.com/etl-sql/ETL-SQL/actions/runs/41357097184) on the same artifact exceeded both runtime deadlines and ended cancelled after ordinary and forced cancellation requests | Quiet runtime disables callbacks, so callbacks alone do not explain the stall. Observed success does not isolate logging, timing or runner effects and creates no certification receipt. The normal retry above passes, so these controls do not establish a deterministic cause |
| Latest broad package timing | [Native 41343571086](https://github.com/etl-sql/ETL-SQL/actions/runs/41343571086) passes all four platforms at `2ff552ae9`; observed job span is 80m39s, with Intel ZIP/VSIX building taking 61m04s | The package-affecting PR still misses 30 minutes. This is a development measurement, not complete release certification or a four-hour release result |
| MSI-only local validation | Isolated baseline `9c8f292cd`, one MSI XML edit, no skip flags: 62 source checks pass and the public script takes 2m25s. Windows/Linux routing fixtures cover 73 cases | The first rehearsal hit a publisher directory-move access error; its failed-check recovery and original report remain retained. Hosted timing after routing rollout is still required; policy changes in this proposal conservatively select broad validation |
| Failed browser shards preserve successful jobs | [Portal rehearsal](https://github.com/etl-sql/ETL-SQL/actions/runs/40609095598): original successful timestamps retained, recovery 19m43s | No repeat of this historical rehearsal needed |
| Failed engine group preserves other groups | [Engine rehearsal](https://github.com/etl-sql/ETL-SQL/actions/runs/40754693409): recovery 14m35s | No repeat of this historical rehearsal needed |
| Failed MSI profile reuses installer and successful profile | [MSI rehearsal](https://github.com/etl-sql/ETL-SQL/actions/runs/40760871492): original artifact 12850372732, recovery 7m33s | Runner-loss cause remains separate from successful recovery |
| Corrected DEB diagnostics survive upload | [DEB acceptance](https://github.com/etl-sql/ETL-SQL/actions/runs/40494686777): 39 installed assertions and diagnostic upload pass | Final candidate still needs its own certification |
| Failed publication upload preserves package bytes | [Hosted rehearsal](https://github.com/etl-sql/ETL-SQL/actions/runs/41144863470): both failed jobs recover in 3m58s; original artifact and successful executions retained | Final-candidate promotion remains separate |
| Promotion reuses native packages and retained operator reports | [Release checklist](release-checklist.md) and [operator gate](operator-evidence.md): local command fixtures verify original package identity, report contracts, changed-evidence rejection and unchanged-byte recovery | Hosted acceptance on the final candidate; fixtures are not certification evidence |
| Full release completes within four hours | No complete current-candidate elapsed-time proof | Freeze one candidate, collect all required certification and promotion timings, preserve successful jobs across any retry |

Execution order: land the reviewed recovery changes, then measure the complete candidate path.
The [development evidence summary](../../certification-results/release-process-development-2026-11-01.json)
retains the three browser inventories, receipt hashes and retained-input MSI diagnostic scope.
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
