# Strategy and Design History

This folder contains design records, implementation plans, and historical roadmaps. It is useful context, but it is not the primary product reference. For current behavior, prefer the user guides, reference docs, architecture docs, and standards.

## How to Read These Documents

| Type | Meaning | Maintenance expectation |
| :--- | :--- | :--- |
| Implemented design note | Describes a shipped design or the reasoning behind a current implementation | Keep status current and link to the matching architecture/reference docs |
| Active strategy | Describes intentional near-term work that has not fully shipped | Keep scope, status, and acceptance criteria explicit |
| Historical roadmap | Preserves planning context for work that has since shipped or changed direction | Keep only if it explains useful decisions; otherwise archive or fold into current docs |

## Current Classification

| Document | Current role | Recommended next action |
| :--- | :--- | :--- |
| [Arrow_Columnar_Strategy.md](Arrow_Columnar_Strategy.md) | Implemented design note | Keep; reconcile with current spill implementation when large-dataset docs change |
| [LargeDatasets.md](LargeDatasets.md) | Implemented design note | Keep; link from architecture/reference where spill behavior is explained |
| [Source_Boundary_Migration_Plan.md](Source_Boundary_Migration_Plan.md) | Implemented migration record with remaining boundary guidance | Keep; eventually convert remaining guidance into architecture docs |
| [Connector_Upgrade_Strategy.md](Connector_Upgrade_Strategy.md) | Implemented modernization note | Keep as design history; current syntax and contracts live in connector reference, architecture, and standards |
| [ScriptSecurity_Strategy.md](ScriptSecurity_Strategy.md) | Security design rationale | Keep; cross-check against `SECURITY.md` before releases |
| [Enterprise_Platform_Strategy.md](Enterprise_Platform_Strategy.md) | Active umbrella strategy for progressive deployment, enterprise authority, governance, HA, and isolation | Keep aligned with `ROADMAP.md`; move shipped mechanics into architecture docs |
| [Data_Stewardship_Strategy.md](Data_Stewardship_Strategy.md) | Backlog strategy for lineage-driven stewardship, tag policy, impact analysis, certification, and catalog visibility | Use as the source strategy when prioritizing a stewardship sprint |
| [FuzzyMatching_Strategy.md](FuzzyMatching_Strategy.md) | Mostly shipped design note with one deferred area | Keep; make Phase 5 explicitly post-0.7 or move it to backlog |
| [DataLake_Connectors_Strategy.md](DataLake_Connectors_Strategy.md) | Mixed shipped capability and future data-lake direction | Header now marks the split; keep shipped connector facts in reference/architecture |
| [Query_Execution_Efficiency_Strategy.md](Query_Execution_Efficiency_Strategy.md) | Active performance strategy | Keep; use benchmark results and phased implementation notes to drive v0.8 query execution work |
| [Presentation_Layer_Strategy.md](Presentation_Layer_Strategy.md) | Mixed specification and unresolved presentation backlog | Header now warns readers; reconcile current facts into presentation/TUI/VS Code architecture docs |
| [Engine_Upgrade_Strategy.md](Engine_Upgrade_Strategy.md) | Historical roadmap | Archive or rewrite as a short design note; current architecture belongs in `Docs/Architecture` |
| [Report_SQL_Strategy.md](Report_SQL_Strategy.md) | Historical roadmap/backlog | Header now warns readers; use Report-SQL guide/cookbook/syntax index for current behavior |
| [ReportPortal_Strategy.md](ReportPortal_Strategy.md) | Historical roadmap | Header now warns readers; use portal user/admin guides and architecture for current behavior |
| [SubscriptionParameters_Strategy.md](SubscriptionParameters_Strategy.md) | Historical feature plan | Header now warns readers; current behavior belongs in relative date, Report-SQL, and portal docs |
| [Lineage_Strategy.md](Lineage_Strategy.md) | Active or partially stale strategy | Header now warns readers; audit against current lineage implementation before treating as roadmap |
| [Test_Strategy.md](Test_Strategy.md) | Active operational strategy | Keep; pair with `Docs/Testing.md` |

## Cleanup Rules

- Do not document shipped syntax here as the only source of truth. Put it in `Docs/Reference` or the relevant guide.
- Do not leave completed implementation checklists as active plans.
- If a document is valuable only as history, say so at the top.
- If a document contains future work, make the status and release target explicit.
- Prefer moving stable architecture facts into `Docs/Architecture` and leaving this folder for rationale and planning context.
