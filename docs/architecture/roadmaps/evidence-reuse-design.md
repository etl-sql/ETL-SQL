# Evidence reuse when inputs did not change (W2 design note)

**Status:** signed off by the owner 2026-11-10, with Option 1 for packages (reuse CI lanes,
rebuild packages). Hosted CI reuse is built (2026-11-10) in
`scripts/collect-hosted-release-evidence.mjs`, with the job-to-lane map in
`scripts/hosted-release-policy.json` and tests in `scripts/checks/test-hosted-release-evidence.mjs`.
**Policy approved 2026-11-04:** a lane that passed on an earlier candidate is not rerun when the
change cannot affect it.

## The problem

Release evidence must come from the exact candidate commit. A one-line fix to the release notes
creates a new commit, and every lane runs again: full CI (about 40 minutes), MSI, DEB, native
Windows and both macOS builds (hours). Most of that work cannot be affected by the change.

## The rule

Evidence from an earlier commit **A** counts for the candidate **B** when all of these hold:

1. **A is an ancestor of B on the same release branch**, and A's run is within the 7-day window
   that release evidence already uses.
2. **The files changed from A to B cannot affect that lane.** Each changed path is classified by
   the CI impact map (`scripts/ci-impact.json`), the same map regular CI and the pre-push hook use
   today. A path no rule names falls to the catch-all rule, which selects every lane. Unknown means
   rerun.
3. **A passed that lane completely**: every job a success on A, none skipped.
4. **The lane's own definition did not change.** A change to a lane's workflow, scripts or tests
   is a change to that lane.

Native packages (MSI, DEB, Windows/macOS ZIPs, VSIX) are reused only when no changed path is marked
`nativePackages: true`. Reused bytes are promoted exactly as they are today, with their hash checks.

## What counts as an input

Inputs are not listed per lane. The impact map does the reverse: it says which lanes a path
affects. This is the same thing, and it already exists, has tests and runs on every push. Starting
narrow:

- Reusable with no rerun: planning and release docs (`TODO.md`, `CHANGELOG.md`, `docs/releases/`,
  `docs/architecture/`). This is already the map's first rule.
- New rule: release tooling (`scripts/collect-*.mjs`, the release workflow checks under
  `scripts/checks/`) reruns only Source preflight, which runs those checks. Today these paths fall
  to the catch-all and rerun everything.
- Everything else reruns the lanes the map gives it, as regular CI already does.

`docs/reference/` is **not** in the reusable set. It is the embedded runtime help, so it ships
inside the product.

## Who checks

GitHub Actions does, in the same collectors that check evidence today
(`collect-hosted-release-evidence.mjs` and the Windows, macOS, MSI and DEB collectors). The
supervisor and the drivers call the same code, so there is one rule. Each reuse is recorded in the
retained evidence JSON: the source commit A, the changed paths, the impact rule each path matched
and the lanes it kept. Anyone can audit a release from its run artifacts.

## What a wrong reuse costs

A wrong reuse ships a change no lane ran against. The ways that can happen, and the guard for each:

| Way it goes wrong | Guard |
|---|---|
| A rule in the map is too narrow | New paths fall to the catch-all (full rerun). Only the docs rule skips everything. Map changes need review. |
| A chain of reuses drifts far from a real run | Evidence always points at a real passing run on A, never at another reuse. The 7-day window caps age. |
| A lane passed on A by luck (a flake) | Same exposure as today: a pass is a pass. W5 keeps tracking flakes. |
| The release ships bytes built from A | See the decision below. |

The nightly runs everything on the branch HEAD. A wrong reuse is caught the next night at the
latest, before the release button is pressed.

## Decision: package bytes from an earlier commit (owner chose Option 1)

.NET stamps the commit into each binary's version (`0.22.0+<sha>`). If MSI, ZIP and VSIX bytes are
reused from A, the release tagged at B ships binaries that report A's commit. The provenance
attestation still names B's run.

- **Option 1 (recommended): reuse CI lanes, rebuild packages.** CI evidence is reused. Package
  certification reruns on any commit, so binaries always match the tag. A docs-only recommit
  skips CI (about 40 minutes) but still waits for the package runs.
- **Option 2: reuse packages too.** This is the fastest path. The binaries report A's commit, and
  the release notes and the evidence JSON say so.
- **Option 3: tag A instead of B for a docs-only change.** Binaries match the tag, but the
  release reads its notes from the tagged commit, so it would need to read them from B instead.

## Done when

A docs-only or release-script-only commit reruns only its affected checks, and the release run's
evidence JSON shows the reuse.

## Policy text to change after sign-off

`docs/releases/release-checklist.md` says "a source fix needs a new candidate" and that a merge
commit needs its own certification. Those lines change to point at this rule.
