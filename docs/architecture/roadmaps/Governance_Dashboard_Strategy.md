# Portal Governance Dashboard Strategy

**Status:** Candidate roadmap work
**Date:** 2026-07-23
**Scope:** Portal usability and script-first governance workflows for production `.etlsql` and `.rptsql` assets.

---

## Why This Matters

The current Portal Governance surface has powerful lineage, stewardship, protected-data, audit, and impact search modes. Those search tools should remain, but they are too detailed as the first thing a data steward sees.

The next usability step is a focused dashboard that answers the steward's operational questions before they search:

- Are all production scripts owned and stewarded?
- Are all protected fields tagged?
- Which ETL scripts touch protected data?
- Which reports display protected data?
- Which scripts changed since steward review?
- Which scripts are below the governance threshold?
- Which glossary terms are missing or inconsistent?
- Which findings were ignored or accepted as risk?

The unit of governance should be the production script or report asset. Lineage rows, tags, protected-data findings, glossary checks, published versions, and steward decisions should roll up to those assets.

---

## Product Model

For an estate with 10 production scripts, such as 5 ETL scripts and 5 reports, every production asset should have a visible governance posture:

- **Required metadata:** `@owner`, `@steward`, `@contact`, and `@domain`.
- **Protected-data metadata:** `@pii`, `@phi`, `@pci`, `@sensitive`, `@classification`, and related tags where applicable.
- **Quality and trust metadata:** `@quality`, `@certification`, `@trusted`, or equivalent report metadata.
- **Version awareness:** latest published hash/version, latest steward review, and whether findings predate or follow the current version.
- **Computed posture:** governance score, automatic badges, open findings, ignored findings, and accepted risks.

The dashboard goal should be clear: 100% of production scripts are governed, reviewed, and explainable.

---

## Dashboard Experience

The default Governance view should become a dashboard with two scopes:

- **My steward work** - assets and findings assigned to the current steward.
- **All governance work** - every production asset, grouped by steward, domain, score, badge, or finding type.

The dashboard should show KPI tiles and queue previews:

- **Governed assets** - count and percent at or above the configured score threshold.
- **Below threshold** - production scripts requiring follow-up.
- **Missing metadata** - assets missing required ownership, steward, contact, domain, classification, or quality metadata.
- **Protected data** - ETL scripts touching protected fields and reports displaying protected fields.
- **Changed since review** - assets whose latest published version has not been reviewed.
- **Glossary review** - inconsistent or unmapped terminology.
- **Ignored / accepted risk** - findings intentionally suppressed or accepted, with audit history.

Each KPI must drill into evidence: script path, report name, latest version/hash, failing rule, expected metadata, current tags, lineage rows, protected-data finding, glossary term, and steward decision history.

The existing deep Lineage Explorer should remain available as a fallback mode with its current table/source/file/tag/job search, saved views, CSV export, and graph/table toggle.

---

## Governance Module and Pages

Governance should be a standalone Portal module, similar in spirit to report and orchestrator surfaces. A deployment should be able to install only the governance experience if desired, without requiring the full report library UI.

Standalone governance install requirements:

- Governance routes, APIs, services, assets, navigation, health checks, and configuration must live behind an explicit module boundary.
- The module can depend on shared Portal identity, audit, configuration, script execution, lineage catalog, and storage abstractions.
- It must not require report-library navigation to render or operate, though it can link to reports when the report module is installed.
- Cross-module links should degrade gracefully when reports, orchestrator, or designer surfaces are not installed.
- Module enablement should be configurable so a server can expose Governance-only, Reports-only, Orchestrator-only, or combined Portal experiences.

Initial page set:

- **Overview Dashboard** - KPI tiles, score posture, protected-data exposure, changed-since-review, glossary review, and steward/all-governance scopes.
- **Task Workqueue** - open findings, assigned steward work, all findings, filters, ignore false positive, accept risk, reopen, and evidence drill-in.
- **Audit & Decisions** - ignored findings, accepted risks, review notes, badge assignments, certification changes, glossary activation changes, and policy-level changes.
- **Badge Management** - steward-assigned badges such as `Reviewed`, `Trusted`, `Certified`, and `Accepted Risk`, with explanation and audit history.
- **Glossary** - add, update, delete, import, export, activate, deactivate, and review terminology, aliases, approved calculations, alternate definitions, and required metadata.
- **Settings** - thresholds, enabled checks, policy levels, active glossary artifact, role/permission summary, and module health.
- **Lineage Explorer** - the existing deep search and graph/table explorer preserved as an advanced investigation page.

The Settings page is required so opt-in behavior is visible and controllable. The Lineage Explorer remains part of the module because steward dashboard questions need a path to deep evidence.

---

## Governance Score

Each production asset should receive a configurable governance score.

Initial score inputs can come from existing Portal and lineage data:

- Required metadata completeness.
- Protected-data tag completeness.
- Report exposure of protected data.
- Glossary coverage and terminology consistency.
- Latest version reviewed by a steward.
- Freshness/staleness posture.
- Certification or quality marker.
- Open unresolved findings.
- Audit delivery health where relevant.

Suggested score behavior:

- A global score threshold controls when an asset creates a remediation finding.
- Domain-level thresholds can be added later.
- Scores should be explainable, not opaque. Every lost point should map to a failing rule.
- The dashboard should show score trend only after score snapshots are persisted.

Script-first implementation path:

- v1: compute scores from current APIs and metadata in the Portal frontend or a small Portal service.
- v2: allow an approved governance score script to emit a standard result shape such as `#GovernanceScore`.
- v3: persist score snapshots for trends by script, report, steward, domain, and folder.

---

## Remediation Findings

ETL-SQL should not replace the external task tracker. Portal should create internal governance findings for steward visibility, and remediation should be reconciled when developers update scripts and publish new versions.

Findings should be created proactively by governance scans, not by requiring stewards to search. The primary scan trigger should be developer publish: whenever a production `.etlsql` or `.rptsql` asset is published, Portal evaluates the active governance settings against the new version/hash and creates, resolves, or reopens findings. Scheduled and manual scans should exist as backstops for changed settings, changed glossary terms, imported lineage history, or operational recovery.

Typical findings:

- Production script is missing `@owner`, `@steward`, `@contact`, or `@domain`.
- Protected data appears untagged based on classifier suggestions.
- Report displays protected data without the expected classification or steward review.
- Script score is below threshold.
- Script changed since the last steward review.
- Glossary terminology is inconsistent with the approved glossary.
- Certified/trusted asset has incomplete metadata.

Finding lifecycle:

- **Open** - current version fails a rule.
- **Resolved** - a newer published version satisfies the rule.
- **Ignored false positive** - steward declares the finding invalid.
- **Accepted risk** - steward accepts the condition and records why it does not require further tracking.
- **Reopened** - a later version changes the asset or invalidates the ignore/risk decision.

Every ignore and accepted-risk decision must record user, timestamp, reason, asset identity, rule key, script path, and version/hash.

---

## Badges

Badges should be hybrid.

Automatic badges are computed from criteria and current evidence:

- `Protected Data`
- `PII`
- `PHI`
- `PCI`
- `Needs Metadata`
- `Glossary Review`
- `Changed Since Review`
- `Below Threshold`
- `Stale Lineage`
- `Displays Protected Data`

Steward-assigned badges are audited review decisions:

- `Reviewed`
- `Trusted`
- `Certified`
- `Accepted Risk`

The UI should distinguish automatic badges from steward-assigned badges. A badge details popover should explain why it appears and show the underlying rule, metadata, lineage, or steward decision.

Existing report, catalog, lineage, protected-data, and stewardship badges should remain. The new work is to synthesize them into a clear asset-level posture.

---

## Glossary and Term Coverage

Glossary should be manual, organization-owned, and script-first.

ETL-SQL should ship an optional starter glossary template rather than enforcing a default glossary. The starter template can include common protected-data and business terms such as customer, account, employee, email, phone, address, SSN, date of birth, revenue, cost, region, product, order, claim, and patient.

Organizations should be able to copy, edit, disable, or replace the starter glossary.

Glossary enforcement must be opt-in:

- A starter glossary can be visible in Portal as a template, but it should not affect scores or create findings until activated.
- An organization-owned glossary artifact must be selected before glossary checks count toward governance posture.
- Glossary checks should start in suggestion-only mode.
- Admins or governance leads can later enable score impact, remediation findings, or certification gates.
- Calculation validation should be enabled per term or domain, not globally by default.

Healthcare-style semantic terms need more than alias matching. A glossary term such as "Length of Stay" should be able to define aliases, required metadata, approved calculation patterns, alternate accepted definitions, and whether expression validation is active. If a script emits `LOS`, `length_of_stay`, or similar names, Portal can create a review finding when the lineage expression does not match an approved calculation or the script omits an explicit `calculation_method` tag.

Implementation model:

- Store glossary definitions in a governed artifact such as `governance/glossary.etlsql` or `governance/glossary.csv`.
- Run glossary coverage checks from Portal using the normal approved script execution path.
- Emit a standard result shape such as `#GlossaryCoverage`.
- Create findings for unmapped terms, conflicting terms, noncanonical names, and likely term matches that need steward review.
- Create findings for enabled semantic-calculation checks when a term alias is detected but the lineage expression is missing, unknown, or inconsistent with the active term definition.
- Do not silently rewrite scripts. Developers reconcile glossary findings by updating script metadata and publishing a new version.

Suggested matching progression:

- v1: exact matching against tags, table names, column names, domains, classifications, and aliases.
- v2: fuzzy suggestions using ETL-SQL matching capabilities, requiring steward approval.
- v3: lineage-aware propagation of accepted terms and classifications, plus approved-calculation validation for terms that opt into it.

---

## Enablement and Policy Levels

Governance checks should be visible before they are enforceable. Default rollout:

- **Visible:** Governance dashboard, score preview, badges, and glossary templates are available.
- **Suggestion-only:** Protected-data suggestions, glossary matches, and calculation concerns appear as review candidates but do not affect scores.
- **Scored:** Activated checks affect the governance score and can create findings below threshold.
- **Certification gate:** Activated checks can block `Trusted`, `Reviewed`, or `Certified` steward badges.
- **Publish gate:** A later policy can block publishing for high-risk failures, but this should not be the initial default.

These levels should be configurable globally and, later, by domain or folder.

---

## Roles and Access Control

Governance needs explicit permissions instead of overloading only `Admin` or `Publisher`.

Candidate roles:

- **GovernanceViewer** - can view the full governance dashboard, scores, badges, glossary coverage, and findings.
- **DataSteward** - can view all governance work, manage findings assigned to them, ignore false positives, accept risk where permitted, and mark assets reviewed.
- **GovernanceManager** - can configure score thresholds, enable glossary checks, manage starter/custom glossary activation, assign stewards, and certify assets.
- **Admin** - retains full administrative override.

Candidate ACL behavior:

- Stewards should see their own queue first, but must not be blind to other stewards' work.
- Sensitive finding details should follow existing report/dataset access rules unless the user has GovernanceViewer, DataSteward, GovernanceManager, or Admin.
- Ignore and accepted-risk actions require DataSteward or higher and must be audited.
- Certification and policy-level changes require GovernanceManager or Admin.
- Running approved governance scripts from Portal should require GovernanceManager or Admin unless the script is explicitly marked safe for DataSteward execution.

If new roles are added, update user management, JWT role emission, API authorization tests, configuration export/import, and documentation.

---

## Data Model Candidates

If persistence is needed, add Portal-owned governance tables for derived workflow state, not as the primary metadata source:

- `GovernanceFinding` - current and historical findings keyed by asset, rule, and version/hash.
- `GovernanceFindingDecision` - ignore, accepted-risk, reopen, and review decisions.
- `GovernanceScoreSnapshot` - optional score history for trends.
- `GovernanceGlossaryTerm` or imported glossary snapshot - optional cached copy of the script-first glossary artifact.
- `GovernanceSettings` - threshold, policy level, active glossary artifact, and enabled checks.
- `GovernanceAssetReview` - latest steward review state by asset and version/hash.

Durable metadata should remain in `.etlsql`, `.rptsql`, and governed glossary artifacts wherever possible.

---

## Acceptance Criteria

- A steward can open Governance and immediately see their work without losing visibility into other stewards' work.
- A governance lead can see the full estate posture and drill into any failing script/report.
- Every production script and report has a score, badges, and evidence explaining both.
- Findings reconcile automatically when a developer publishes a corrected version.
- Ignored false positives and accepted risks are auditable and version-aware.
- Reports that display protected information are clearly visible.
- ETL scripts that touch, transform, mask, or move protected information are clearly visible.
- Glossary and calculation checks do not affect scores or create findings until explicitly enabled.
- Role and ACL behavior is tested for stewards, governance managers, admins, publishers, and viewers.
- The existing Lineage Explorer remains available for deep search.

---

## Non-Goals

- Building a full external task management system.
- Making Portal-only edits the primary source of governance metadata.
- Auto-fixing scripts without developer review and source control.
- Replacing external enterprise catalogs.
- Enforcing a universal business glossary for every organization.
