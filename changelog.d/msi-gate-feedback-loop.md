### Changed

- **The MSI upgrade gate no longer needs a 26-minute CI run to find a typo.** Its first real
  execution failed on pure logic — a multi-value read that turned a comparison into an array filter
  — after twenty-odd minutes of downloading a previous release and building an installer. Nothing
  about that bug needed an MSI, elevation, or an install.

  - Non-elevated logic moved to `scripts/MsiUpgrade.Helpers.ps1`, side-effect free on load.
  - **`Test-MsiUpgrade.ps1 -StaticChecksOnly`** runs the upgrade contract — same `UpgradeCode`,
    ascending `ProductVersion` — with no elevation and no install, in about a second, on any
    machine. The workflow runs it as its own step before the install sequence, so a failing log says
    which half broke.
  - The push trigger is **path-filtered** to the installer, its scripts and `Directory.Build.props`.
    A documentation change previously paid the full 26 minutes for nothing.

  This matters more than convenience: the elevated half has no local path on Windows Home, where
  Windows Sandbox and Hyper-V are unavailable. Pushing everything testable out of it is what makes
  the script maintainable at all.

### Added

- `MsiUpgradeHelperTests` pins the guard for the class of bug that broke the gate: a property read
  resolving to zero or several values now throws with the values printed, instead of returning
  something a later `-ne` silently mis-handles. Mutation-verified by disabling the guard.

### Known

- If the MSI job becomes a **required status check**, it needs a companion always-succeeds job. A
  path-filtered workflow reports *skipped* rather than *success*, and a required check that never
  reports will block every unrelated pull request. Recorded in `TODO.md` beside the setting itself.
