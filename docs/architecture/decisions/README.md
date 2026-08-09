# DECISIONS Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [Adaptive Execution Controller (v0.15.0 Phase 2) — Design](AdaptiveExecutionController.md) | **Status:** Slice A implemented — advice is computed and recorded, and **no execution pipeline |
| [Alerting and Service Objectives](Alerting_Service_Objectives.md) | This guide defines baseline service indicators, starter objectives, alert routing, and runbook |
| [Authorship Is Not Permission](AuthorshipIsNotPermission.md) | **Status:** decided and implemented for reports (v0.17.0) and datasets (v0.18.0). |
| [Billion-Row Operator Certification (v0.15.0 Phase 4) — Design](BillionRowOperatorCertification.md) | **Status:** Candidate implementation complete for the v0.15.0 Phase 4 scope; 1B operator-run |
| [Portal and Orchestrator Capacity Planning](Capacity_Planning.md) | Use this guide to turn an expected user base and job schedule into a starter server plan for |
| [Portal and Orchestrator Capacity Testing](Capacity_Testing.md) | Use `scripts/test-service-capacity.mjs` to measure Portal-user and Orchestrator-job capacity against |
| [Concurrent PostgreSQL and Failure Soak Certification (v0.15.0 Phase 6) — Design](ConcurrentPostgresFailureSoak.md) | **Status:** Implementation in progress; Slice A topology harness is implemented. |
| [Column & Job Data-Quality Rules — Design Specification](DataQualityRules.md) | Extend the engine's verification surface from **schema** (`EXPECT SCHEMA … ON DRIFT WARN`) and |
| [Departmental Isolation Topology](Departmental_Isolation.md) | This document defines how to run **multiple isolated ETL-SQL environments** — for example |
| [Disaster Recovery Objectives](Disaster_Recovery_Objectives.md) | This guide defines supported RPO/RTO targets, recovery-set contents, restore-drill expectations, and |
| [Enterprise Release Evidence Checklist](Enterprise_Release_Evidence_Checklist.md) | Status: Prepared checklist, not release evidence. |
| [Enterprise Release Gates](Enterprise_Release_Gates.md) | This document is the release-gate checklist for the enterprise policy, monitoring, HA, recovery, and |
| [Enterprise Security Review Packet](Enterprise_Security_Review_Packet.md) | Status: Prepared, not signed off. |
| [Execution Transparency and Fallback Coverage (v0.15.0 Phase 5) — Design](ExecutionTransparencyAndFallbacks.md) | **Status:** Phase 5 implementation complete for the current v0.15 native/pushdown/external surfaces; |
| [HA Topology and Failure Certification](HA_Topology_Failure_Certification.md) | This guide defines the supported Portal and Orchestrator deployment topologies, the readiness |
| [Host Utilization Time Series & Capacity Reporting — Implementation Plan](HostUtilizationAndCapacityPlanning.md) | - **`NodeCapacityMonitor.Capture()`** (`ETL-SQL.Orchestrator/Scheduling/NodeCapacityMonitor.cs`) |
| [Performance Regression Quality (v0.15.0 Phase 3) — Design](PerformanceRegressionQuality.md) | **Status:** Implemented for v0.15.0 Phase 3. |
| [Design Strategy: First-Class Web Script Editing in the Portal](PortalEditorStrategy.md) | As ETL-SQL scales into enterprise farms (multiple orchestrators/portals) and SaaS/multi-tenant |
| [Row-Level Security via Injected Identity — Reference Specification](RowLevelSecurity.md) | Let report authors write row-filtering predicates keyed on **who is running the report** and **what |
| [SME Secret Management and Administration Hardening (v0.15.0 Phase 7) - Design](SMESecretManagementAdministrationHardening.md) | **Status:** Draft for implementation planning. |
| [Design Spec: Smart Snippets and Schema-Aware Code Generation](SmartSnippetsSpec.md) | This document outlines the design and workflow for **Smart Snippets** in ETL-SQL. It details how slash commands (like `/merge` and `/upsert`) can i... |
| [Design Spec: Unified Notebook & Script Execution (Virtual Cells and Checkpoints)](UnifiedNotebookScriptExecution.md) | This document specifies the design for unifying the `.etlnb` (ETL-SQL Notebook) execution controller with plain-text `.etlsql` and `.rptsql` script... |
| [Design Spec: Job, Schedule, and Alerting Refactor](job_schedule_notification.md) | This document outlines the architectural changes for establishing a unified, many-to-many scheduling |

> **Note:** Per-release evidence files (code reviews, performance results, flaky-test logs) live in [`releases/`](../../releases/README.md) alongside their corresponding release notes, not here. This folder contains architecture decisions, design specs, and operational certifications that are not scoped to a single release.
