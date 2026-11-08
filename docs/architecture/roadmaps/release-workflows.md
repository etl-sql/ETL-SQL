# Release Workflow Strategy

ETL-SQL releases are local-first while the product remains owner-controlled. The intended flow is:

1. Run `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale` locally.
2. Fix failures and rerun with `-Resume`.
3. Retain the complete candidate evidence required by the release checklist.
4. Build installers locally with `-BuildInstallers`.
5. Push only after local validation passes.
6. Require exact-candidate hosted certification before tagging and publishing release artifacts.

## Release Cadence

**Through v0.17.0 the cadence was weekly.** v0.7.0 through v0.17.0 shipped on consecutive Sundays,
which suited a phase of rapid feature delivery: small diffs, short feedback loops, and little time
for a release to drift from `main`.

**From v0.18.0 the cadence moved to monthly.** The original target was **2026-08-24**.

The change reflects what the release actually costs now. A weekly release amortises the fixed
overhead — full local gate, enterprise certification on two platforms, recovery drill, HA fault
injection, packaging, CodeQL — across seven days of work. As the surface has grown, that fixed cost
stopped fitting a weekly window: the v0.17.0 gate alone runs 60–90 minutes per attempt, and each
push to `main` or a release branch triggers roughly 40 minutes of CI across Windows and Linux
runners.

Monthly also gives the verification steps room to be real rather than skipped. Several checks are
long or manual by nature — the MSI in-place upgrade test, operator-run HA soak against a live
PostgreSQL topology, scale certification on a quiesced machine. Under weekly pressure those are the
first things to get deferred, and deferral compounds: the scale-certification re-validation was
pushed out of v0.15.0, then v0.16.0, and only resolved during v0.17.0.

Cadence is a target, not a commitment. Ship when the gate is green and the evidence is collected;
a release that is not ready waits.

## Release Artifact Verification (Checksums & SBOM)

During the release publishing process (`scripts/publish-release.ps1`), the packaging runner automatically:
1. **Generates SHA-256 Checksums**: Computes the cryptographic checksum for all binary bundles and installers, exporting them to `sha256sums.txt`.
2. **Generates CycloneDX SBOM**: Invokes `scripts/generate-sbom.js` to scan central NuGet PackageReferences and npm/extension package specifications, exporting a unified JSON SBOM to `release/sbom.json`.

These verification assets must be uploaded as part of the public release draft.

The active CI, MSI certification and release workflows now provide hosted validation and packaging.
Local validation remains required by the release checklist. The v0.22.0 work in
[`TODO.md`](../../../TODO.md#1-release-reliability-v0220-ships-after-this) moves toward reusable candidate artifacts
and an enforced publication gate. MSI certification now has a build job followed by independent
workstation/Portal jobs consuming one SHA/hash-bound installer pair. Failed-job retries can reuse
that pair; hosted recovery rehearsal is still required. Release calls that workflow and promotes
its certified MSI artifact ID when no eligible pre-tag certification exists. A successful release-branch
run for the same commit may supply its retained artifact, bound by a gate receipt to both passed
profiles and the current previous-release version. Discovery rejects failed/in-progress candidate
runs and invalid receipts. Attestation verifies the downloaded draft MSI against the input
manifest before publication. Packaging for other targets proceeds independently. macOS Intel and
Apple Silicon ZIP/VSIX builds run on separate native runners, with neither cancelled when the other
fails. Both gate publication; the existing optional Intel DMG is separate. Enforcing all
checklist evidence and the hosted reuse/retry rehearsal remain future work.
Release also requires the exact candidate's CI and CodeQL jobs before packaging and rechecks them
before publication. Their retained manifest includes original job attempts and failed-attempt
history; failed-job retries reuse successful work. Full local/operator evidence enforcement remains
open, so this hosted manifest alone does not certify the complete release.
Regular CI now consumes the impact map. Full CI certification calls that implementation with every
lane enabled. Release drivers reuse a complete regular push when all required jobs passed, otherwise
request or resume the full workflow; request journaling avoids duplicate dispatch after network
uncertainty. Hosted rollout and timing proof remain outstanding.
See the [release checklist](../../releases/release-checklist.md#recovering-a-failed-msi-certification)
for the current recovery procedure.

## Dormant Workflow Templates

Workflow templates live under `.github/workflow-templates/`. GitHub does not execute them from that location. To enable one later, copy it into `.github/workflows/` deliberately.

These templates are historical starting points, not the inventory of active hosted validation:

1. `manual-release-validation.yml` — manual smoke/fast/coverage validation.
2. `manual-docker-certification.yml` — manual Docker connector certification.
3. `manual-scale-certification.yml` — manual Standard-scale certification.
4. `local-validated-release.yml` — release packaging after a local validation report exists.

Reconcile a template with the active workflows and release checklist before enabling it, so it
does not duplicate certification already running elsewhere.

## Release dry run

`release.yml` dispatched on a release branch with `dry_run: true`, `tag: vX.Y.Z` and
`candidate_sha` (the branch HEAD) rehearses the release without releasing. It validates that
exact commit instead of a tag and writes to its own draft, `dry-run-vX.Y.Z-<run id>`: every
`gh release` command targets `DRAFT_TAG`, which equals the tag in a real release. It selects
and verifies the certified packages, uploads them, checks the draft bytes, then a final job
deletes that draft whatever happened, refusing anything that is not a dry-run draft. It never
tags, publishes, attests provenance (attestations are permanent public records) or publishes
to the Marketplace. Workstation and operator evidence steps are skipped; both are human
submissions being removed or rescoped. A dry run has its own concurrency group, so it never
queues a real release. `scripts/checks/test-release-dry-run.mjs` pins these guarantees.

## Nightly release-branch execution

`nightly-release-scheduler.yml` runs at 06:23 UTC from the default branch. It discovers
`release/vX.Y.Z` branches newer than the latest published stable release and requests
`nightly-release.yml` on each branch, passing its observed HEAD SHA. Draft and prerelease
releases do not retire an active branch. A branch with a queued or running nightly is skipped.
The requested workflow rejects a moved branch before starting expensive jobs.

The nightly workflow is a release rehearsal. It starts the runs a release collects, each as its
own `workflow_dispatch` run on the branch: Release Certification (full CI, including all Portal
browser shards), MSI (both deployment profiles), DEB (fresh install, upgrade, rollback) and
native packages (Windows and both macOS architectures). It waits for them, then runs the
release's own collectors against them: CI and CodeQL evidence, certified Windows and macOS
packages, and the certified MSI and DEB finders. The final job's summary gives one pass/fail and
names the failing step. A run whose runner was never acquired (the candidate supervisor's
`runner-not-acquired` signature) gets its failed jobs rerun once; if that fails the same way the
verdict says infrastructure, not the candidate. When the collectors pass, a last job starts the
release dry run (below) on the same candidate and waits for it. No job may exceed 180 minutes, so the wait spans two jobs; each job finds
the runs this rehearsal started, so a rerun resumes rather than starting duplicates
(`scripts/nightly-rehearsal.mjs`). The rehearsal never tags, drafts or publishes. Because the runs
are the same kind a release collects, a green rehearsal on an unchanged candidate is the release's
pre-tag evidence; operator evidence is still submitted separately.

The DEB workflow builds a package pair once and retains its SHA and hashes. A separate disposable
Ubuntu runner verifies the artifact before installing it. Retrying only the failed installation
job consumes the original artifact ID. A 20-minute process watchdog fits inside a 23-minute step
and 35-minute job limit. Console output and heartbeat messages stream while diagnostic artifacts
are retained on failure. Installation backups are excluded from uploaded evidence.

Roll out the scheduler and dispatch workflow through a default-branch PR, with the called
workflows and scripts present on active release branches. GitHub schedules run from the default
branch and dispatch workflows must exist there. See [GitHub workflow triggers](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow).
Hosted scheduling, DEB installation and failed-job recovery must be demonstrated before closing
the corresponding TODO items.
