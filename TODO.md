# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Active Items

No active items. Move the next actionable roadmap phase here when development begins.

---

## Verification Notes

- The non-Docker Portal run passed **226 tests** on June 14, 2026.
- The documentation, parser, and portal syntax verification run passed **60 tests**.
- The clean-server bootstrap now reconstructs reports, dataset metadata/grants, refresh jobs,
  subscriptions, and alerts and remains unchanged after a second replay.
- Subscription delivery is isolated and deduplicated per normalized recipient and trigger.
- The full Portal test project also attempted 28 Docker-backed SMTP, Orchestrator, and LDAP
  integration tests; those could not run because Docker was unavailable on the test host.
- True dual-node/process, network-partition, disk-pressure, clock-skew, and distributed workload
  fairness certification belongs to the Practical High Availability phase in `ROADMAP.md`, because
  v0.11.0 intentionally supports one active Portal process per SQLite database.
