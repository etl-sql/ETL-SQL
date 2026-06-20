# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Active Sprint (v0.12.0 Stabilization & Release Gates)
*Establishes a stable language contract, unified open-source licensing, distribution trust, and final release gates. Focuses strictly on stabilization and security; no new features.*

- [ ] **Phase 1: Language & Manifest Freeze**
  - Publish the canonical language grammar, connector options reference, and standard library docs.
  - Define a strict deprecation policy for syntax and options.
  - Implement script compatibility test corpus and a migration-linter.
  - Implement `SHOW VERSION` and machine-readable compatibility diagnostics.
- [ ] **Phase 2: Licensing & Contribution Policies**
  - Apply the **Apache-2.0 License** consistently across all projects, extension manifests, and installers.
  - Establish the **Developer Certificate of Origin (DCO)** for external code contributions.
- [ ] **Phase 3: Distribution Trust**
  - Automate build workflows to generate SHA-256 checksums and an SBOM (Software Bill of Materials).
  - Retain test and certification reports in public release assets.
  - Implement cache-busting asset fingerprinting (inject hashes into JS/CSS URLs) in the Report Portal to prevent outdated client-side assets after upgrades.
- [ ] **Phase 4: Release Gates**
  - Verify that a clean script-to-scheduled-production workflow completes successfully without manual intervention.
  - Ensure zero credentials leak in logs, bundles, or debug dumps.
  - Reconcile OIDC/LDAP configurations with standard documentation libraries.
  - Implement automatic diagnostic redaction in `etl-sql admin support-bundle` to automatically strip query parameters, private table data, and personal data (PII) before export.
