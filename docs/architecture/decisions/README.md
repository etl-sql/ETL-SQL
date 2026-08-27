# DECISIONS Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [Adaptive Execution Controller (v0.15.0 Phase 2) — Design](adaptive-execution-controller.md) | **Status:** Slice A implemented — advice is computed and recorded, and **no execution pipeline |
| [Alerting and Service Objectives](alerting-service-objectives.md) | This guide defines baseline service indicators, starter objectives, alert routing, and runbook |
| [Authorship Is Not Permission](authorship-is-not-permission.md) | **Status:** decided and implemented for reports (v0.17.0) and datasets (v0.18.0). |
| [Billion-Row Operator Certification (v0.15.0 Phase 4) — Design](billion-row-operator-certification.md) | **Status:** Candidate implementation complete for the v0.15.0 Phase 4 scope; 1B operator-run |
| [Portal and Orchestrator Capacity Planning](capacity-planning.md) | Use this guide to turn an expected user base and job schedule into a starter server plan for |
| [Portal and Orchestrator Capacity Testing](capacity-testing.md) | Use `scripts/test-service-capacity.mjs` to measure Portal-user and Orchestrator-job capacity against |
| [Concurrent PostgreSQL and Failure Soak Certification (v0.15.0 Phase 6) — Design](concurrent-postgres-failure-soak.md) | **Status:** Implementation in progress; Slice A topology harness is implemented. |
| [Constrained HTML Visuals](constrained-html-visuals.md) | Accepted grammar, security, isolation, interaction, budget, and fallback contract for constrained HTML visuals. |
| [Column & Job Data-Quality Rules — Design Specification](data-quality-rules.md) | Extend the engine's verification surface from **schema** (`EXPECT SCHEMA … ON DRIFT WARN`) and |
| [Departmental Isolation Topology](departmental-isolation.md) | This document defines how to run **multiple isolated ETL-SQL environments** — for example |
| [Disaster Recovery Objectives](disaster-recovery-objectives.md) | This guide defines supported RPO/RTO targets, recovery-set contents, restore-drill expectations, and |
| [Enterprise Release Evidence Checklist](enterprise-release-evidence-checklist.md) | Status: Prepared checklist, not release evidence. |
| [Enterprise Release Gates](enterprise-release-gates.md) | This document is the release-gate checklist for the enterprise policy, monitoring, HA, recovery, and |
| [Enterprise Security Review Packet](enterprise-security-review-packet.md) | Status: Prepared, not signed off. |
| [Execution Transparency and Fallback Coverage (v0.15.0 Phase 5) — Design](execution-transparency-and-fallbacks.md) | **Status:** Phase 5 implementation complete for the current v0.15 native/pushdown/external surfaces; |
| [Governed Custom Tool Runner - Adversarial Certification Evidence](governed-custom-tool-runner-certification.md) | This document records the adversarial certification evidence for the Governed Custom Tool Runner extension in ETL-SQL. The runner implements OCI-ha... |
| [HA Topology and Failure Certification](ha-topology-failure-certification.md) | This guide defines the supported Portal and Orchestrator deployment topologies, the readiness |
| [Host Utilization Time Series & Capacity Reporting — Implementation Plan](host-utilization-and-capacity-planning.md) | - **`NodeCapacityMonitor.Capture()`** (`ETL-SQL.Orchestrator/Scheduling/NodeCapacityMonitor.cs`) |
| [Native Advanced Chart Authoring](native-advanced-chart-authoring.md) | **Status:** Accepted and implemented for Phase 7 renderer-neutral layers, scales, conditions, coordinates, and facets. |
| [Object-Native Artifact Storage Contract](object-native-artifact-storage.md) | **Status:** Accepted and implemented for Platform Phase 1; defines conditional commits, fencing, reconciliation, and S3/Azure provider certification. |
| [Performance Regression Quality (v0.15.0 Phase 3) — Design](performance-regression-quality.md) | **Status:** Implemented for v0.15.0 Phase 3. |
| [Design Strategy: First-Class Web Script Editing in the Portal](portal-editor-strategy.md) | As ETL-SQL scales into enterprise farms (multiple orchestrators/portals) and SaaS/multi-tenant |
| [Row-Level Security via Injected Identity — Reference Specification](row-level-security.md) | Let report authors write row-filtering predicates keyed on **who is running the report** and **what |
| [SME Secret Management and Administration Hardening (v0.15.0 Phase 7) - Design](sme-secret-management-administration-hardening.md) | **Status:** Draft for implementation planning. |
| [SaaS Observability and Support Access Certification](saas-observability-certification.md) | This document serves as the adversarial certification evidence for **SaaS Domain 8: Audit, Observability, and Support Access**. It formally attests... |
| [Design Spec: Smart Snippets and Schema-Aware Code Generation](smart-snippets-spec.md) | This document outlines the design and workflow for **Smart Snippets** in ETL-SQL. It details how slash commands (like `/merge` and `/upsert`) can i... |
| [Design Spec: Unified Notebook & Script Execution (Virtual Cells and Checkpoints)](unified-notebook-script-execution.md) | This document specifies the design for unifying the `.etlnb` (ETL-SQL Notebook) execution controller with plain-text `.etlsql` and `.rptsql` script... |
| [Design Spec: Job, Schedule, and Alerting Refactor](job-schedule-notification.md) | This document outlines the architectural changes for establishing a unified, many-to-many scheduling |
| [Author Bookmarks](author-bookmarks.md) | **Status:** Accepted. Named parameter/page/UI state captured in the report script and replayed at runtime. |
| [Design Spec: Cascading Slicers and Atomic Parameter State](cascading-slicers-and-atomic-parameters.md) | **Status:** Accepted. Dependent slicer option sets and the atomic policy for invalidated descendant selections. |
| [Architecture Decision & Inventory: ECharts and ClearScript Retirement](e-charts-clear-script-retirement-inventory.md) | Implemented. The pre-retirement inventory and the measured footprint the native renderer replaced. |
| [Architecture Decision Evaluation: GANTT Native PlotPlan Composition](gantt-native-composition-evaluation.md) | Accepted and implemented in Phase 8 (Batch 4: Flow & Timeline). |
| [Architecture Decision Record: Native Grammar-of-Graphics Contract and Pluggable Backends](grammar-of-graphics-spec-ir.md) | **Status:** Accepted. The ChartSpec / typed chart data / PlotPlan contract every reporting backend resolves against. |
| [Architecture Decision Record: Micro-Charts, Sparklines & HTML Template Embedding](micro-charts-and-html-embedding.md) | **Status:** Accepted. Sparkline and progress micro-charts inside CARD and TABLE cells. |
| [Measured lean worker profile decision](measured-lean-worker-profile.md) | Accepted decision not to publish a dedicated worker artifact after measurement and trimming experiments. |
| [Architecture Decision & Migration Ledger: Standard Visual Catalog Migration](standard-visual-migration-ledger.md) | Implemented; Phase 8 complete. Per-visual record of the standard catalog's migration onto PlotPlan. |
| [Verified Viewer Context for Gateway PostgreSQL Resources](verified-viewer-context.md) | Separates asserted application context from delegated authentication and defines the signed envelope and PostgreSQL installation contract. |
| [Provider-Neutral Fault Certification](provider-neutral-fault-certification.md) | Provider-neutral fault certification scenarios and observations across local, Docker, and cloud adapters. |
| [ETL-SQL Studio (Report Studio, Script Editor, and Pipeline Studio Architecture)](etl-sql-studio.md) | Flagship visual authoring environment combining Report Studio (WYSIWYG dashboard builder), Script Editor, and Pipeline Studio with live data snapshots. |
