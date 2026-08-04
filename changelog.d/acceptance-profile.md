### Added

- `scripts/Invoke-AcceptanceProfile.ps1` — seeds a small, reproducible acceptance profile into a
  running Portal and smoke-tests it. A folder, a self-contained report, and one user per role
  (Viewer, Publisher, DataSteward, OrchestratorManager).

  Everything goes through the public HTTP API, which is the point: the same script runs against
  `dotnet run`, a container, or a deployed environment, so "it passed locally" and "it passed in the
  image" become statements about the same checks rather than two scripts that happen to share a
  name. It needs nothing installed on the target.

  The profile is deliberately small. An acceptance dataset that takes ten minutes to seed is one
  people stop seeding, and a large one hides the failure it was meant to reveal among rows nobody
  reads.

  It is idempotent — re-running reports what already exists rather than failing or duplicating —
  handles the forced first-run password change automatically, and exits `0`/`1`/`2` for
  passed/failed/unreachable so a pipeline can tell "the Portal is down" from "the Portal is wrong".

  Publishing a report needs the `.rptsql` file under the Portal's script root, which an HTTP client
  cannot arrange. Pass `-ScriptRootPath` where the root is reachable and the script writes it;
  where it is not, the report is **skipped rather than failed**, because a check that fails for
  something the script itself said it could not set up is noise.

  Documented at `docs/administration/portal/acceptance-profile.md`, including the first-run
  configuration an empty Portal refuses to start without.
