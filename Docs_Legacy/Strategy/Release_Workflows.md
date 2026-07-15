# Release Workflow Strategy

ETL-SQL releases are local-first while the product remains owner-controlled. The intended flow is:

1. Run `.\scripts\Test-PreRelease.ps1` locally.
2. Fix failures and rerun with `-Resume`.
3. Optionally run `-IncludeDockerIntegration` and `-IncludeStandardScale`.
4. Build installers locally with `-BuildInstallers`.
5. Push only after local validation passes.
6. Tag and publish release artifacts.

## Release Artifact Verification (Checksums & SBOM)

During the release publishing process (`scripts/publish_release.ps1`), the packaging runner automatically:
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
