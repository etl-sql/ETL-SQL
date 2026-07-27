# Release Workflow Strategy

ETL-SQL releases are local-first while the product remains owner-controlled. The intended flow is:

1. Run `.\scripts\Test-PreRelease.ps1` locally.
2. Fix failures and rerun with `-Resume`.
3. Optionally run `-IncludeDockerIntegration` and `-IncludeStandardScale`.
4. Build installers locally with `-BuildInstallers`.
5. Push only after local validation passes.
6. Tag and publish release artifacts.

## Release Cadence

**Through v0.17.0 the cadence was weekly.** v0.7.0 through v0.17.0 shipped on consecutive Sundays,
which suited a phase of rapid feature delivery: small diffs, short feedback loops, and little time
for a release to drift from `main`.

**From v0.18.0 the cadence moves to monthly.** The next release targets **2026-08-24**.

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

GitHub Actions should remain a final packaging or publication helper, not the first validation environment. Heavier workflows are kept as dormant templates until hosted runner minutes are worth spending.

## Dormant Workflow Templates

Workflow templates live under `.github/workflow-templates/`. GitHub does not execute them from that location. To enable one later, copy it into `.github/workflows/` deliberately.

Suggested future activation order:

1. `manual-release-validation.yml` — manual smoke/fast/coverage validation.
2. `manual-docker-certification.yml` — manual Docker connector certification.
3. `manual-scale-certification.yml` — manual Standard-scale certification.
4. `local-validated-release.yml` — release packaging after a local validation report exists.

Do not enable all of these at once. Start with the cheapest manual validation workflow, confirm runtime cost, then add the heavier workflows only if they are worth the hosted minutes.
