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
| [Connector_Upgrade_Strategy.md](Connector_Upgrade_Strategy.md) | Implemented modernization note | Keep only if it adds context not already covered by connector architecture and standards |
| [ScriptSecurity_Strategy.md](ScriptSecurity_Strategy.md) | Security design rationale | Keep; cross-check against `SECURITY.md` before releases |
| [FuzzyMatching_Strategy.md](FuzzyMatching_Strategy.md) | Mostly shipped design note with one deferred area | Keep; make Phase 5 explicitly post-0.7 or move it to backlog |
| [DataLake_Connectors_Strategy.md](DataLake_Connectors_Strategy.md) | Mixed shipped capability and future data-lake direction | Split shipped connector behavior into reference docs; keep raw object-storage direction as future strategy |
| [Query_Execution_Efficiency_Strategy.md](Query_Execution_Efficiency_Strategy.md) | Active performance strategy | Keep; use benchmark results and phased implementation notes to drive v0.8 query execution work |
| [Presentation_Layer_Strategy.md](Presentation_Layer_Strategy.md) | Mixed specification and unresolved presentation backlog | Reconcile against current TUI/VS Code architecture and move open issues to tracked work |
| [Engine_Upgrade_Strategy.md](Engine_Upgrade_Strategy.md) | Historical roadmap | Archive or rewrite as a short design note; current architecture belongs in `Docs/Architecture` |
| [Report_SQL_Strategy.md](Report_SQL_Strategy.md) | Historical roadmap/backlog | Reconcile against shipped Report-SQL syntax and remove completed backlog tables |
| [ReportPortal_Strategy.md](ReportPortal_Strategy.md) | Historical roadmap | Reconcile against shipped portal docs and architecture; archive remaining planning detail |
| [SubscriptionParameters_Strategy.md](SubscriptionParameters_Strategy.md) | Historical feature plan | Reconcile with `RelativeDate_Parameters.md`, Report-SQL guide, and portal subscription docs |
| [Lineage_Strategy.md](Lineage_Strategy.md) | Active or partially stale strategy | Audit against current lineage implementation before treating as roadmap |
| [Test_Strategy.md](Test_Strategy.md) | Active operational strategy | Keep; pair with `Docs/Testing.md` |

## Cleanup Rules

- Do not document shipped syntax here as the only source of truth. Put it in `Docs/Reference` or the relevant guide.
- Do not leave completed implementation checklists as active plans.
- If a document is valuable only as history, say so at the top.
- If a document contains future work, make the status and release target explicit.
- Prefer moving stable architecture facts into `Docs/Architecture` and leaving this folder for rationale and planning context.
