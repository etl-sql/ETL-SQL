<!-- ============================================================
     ETL-SQL Release Notes Template
     Copy this file to vx.y.z.md and fill in each section.
     Delete the HTML guidance comments as you go.
     See README.md for the full authoring guide.
     ============================================================ -->

# ETL-SQL vx.y.z

**Released:** YYYY-MM-DD
<!-- One-line theme: what is the single sentence someone remembers? -->

---

## Release Summary

<!-- 2-4 sentences framing the release for a busy reader who will skim this and nothing else.
     Name the 2-3 headline capabilities and the user problems they solve.
     Example: "v0.14.0 makes ETL-SQL production-ready at billion-row scale by shipping a
     columnar execution engine with a process-wide RAM governor, and adds Row-Level Security
     so shared reports automatically filter data by the viewer's identity." -->

---

## ⚠️ Breaking Changes & Required Actions

<!-- List any change that could break existing scripts, configs, or deployments.
     For each item include:
     - What changed
     - Why it changed
     - What the user must do (exact commands, config edits, or migration steps)
     If there are no breaking changes, write "None in this release." and keep the section
     so readers always know where to look. -->

- None in this release.

---

## ⏳ Deprecations

<!-- List features being phased out. For each:
     - What is deprecated
     - What replaces it
     - When it will be removed (version or date)
     If none, write "None in this release." -->

- None in this release.

---

## Highlights

<!-- The 2-5 most important features of this release. Each one gets a subsection.
     For every highlight:
     1. Open with the USER PROBLEM it solves (not the technical implementation).
     2. Explain the solution at a level an administrator or power user would appreciate.
     3. Include a code example or config snippet if it clarifies usage.
     4. Reference the design doc if one exists: [Design](../Design/FeatureName.md).
     Audience tags: [Script Authors] [Report Consumers] [Administrators] [Evaluators] -->

### Feature Name

**Audience:** Script Authors · Administrators

<!-- Problem → Solution → Detail → Example → Design reference -->

```sql
-- Example usage
```

> **Design context:** See [DesignDoc.md](../Design/DesignDoc.md) for architectural rationale.

---

## Improvements

<!-- Enhancements to existing capabilities. Group by area if there are many.
     Use past tense. Quantify where possible ("2× faster", "40% less memory"). -->

### Language & Engine
- 

### Report Portal & Visualization
- 

### VS Code Extension & IDE
- 

### Connectors
- 

---

## Performance

<!-- Measurable performance changes. Always quantify.
     Reference the certification tier or benchmark if applicable. -->

- 

---

## Security

<!-- Security fixes, hardening, or compliance changes.
     Reference CVEs, design docs, or the specific guardrail.
     If a vulnerability was patched, describe the risk and the fix. -->

- 

---

## Bug Fixes

<!-- Notable fixes. Frame as "what works now" rather than "what was broken."
     Minor fixes can be summarized; critical fixes get their own line. -->

- 

---

## Known Issues

<!-- Honest disclosure of shipped limitations or workarounds.
     If none, write "No known issues at release time." -->

- No known issues at release time.

---

## Upgrade Guide

<!-- Step-by-step instructions for upgrading from the previous version.
     Include config changes, database migrations, file moves, etc.
     For straightforward releases, a simple "standard upgrade" note is fine. -->

Standard upgrade — replace binaries and restart services. No configuration changes required.

<!-- If more complex:
1. Stop all ETL-SQL services.
2. Run `etl-sql admin migrate-database` to apply schema changes.
3. Update `appsettings.json` with new keys: `...`
4. Restart services.
5. Verify with `etl-sql doctor --profile full`.
-->

---

## Install

Download the platform asset for your environment:

| Platform | Asset |
| :--- | :--- |
| Windows (x64) | `ETL-SQL-vx.y.z-x64-Setup.msi` or win-x64 ZIP |
| Linux (amd64) | `etl-sql_x.y.z_amd64.deb` or linux-x64 ZIP |
| macOS (Apple Silicon) | osx-arm64 ZIP |
| macOS (Intel) | osx-x64 ZIP (best-effort) |
| VS Code | Bundled `.vsix` extension |

Verify checksums against `sha256sums.txt` published with the release assets.

---

## Resources

- [Full Changelog](../../CHANGELOG.md) — exhaustive developer-oriented change log
- [Administrator's Guide](../Administrators_Guide.md) — production deployment and configuration
- [User Manual](../User_Manual.md) — getting started and scripting patterns
- [Report SQL Guide](../Report_SQL_Guide.md) — dashboard and visualization authoring

<!-- Add version-specific links if applicable:
- [Migration Guide for vx.y.z](../Migrations/vx.y.z.md)
- [FAQ for vx.y.z](../FAQ/vx.y.z.md)
-->
