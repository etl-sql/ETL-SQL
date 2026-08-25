# ROADMAPS Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [Arrow Columnar Format Strategy](arrow-columnar-strategy.md) | **Status:** Implemented for ETL-SQL 0.7.0 spill workloads |
| [ETL-SQL Connector Upgrade Notes](connector-upgrade-strategy.md) | **Status:** Implemented for ETL-SQL 0.7.0 |
| [Data Lake Connectors Strategy](data-lake-connectors-strategy.md) | **Status:** Partly implemented (BigQuery/Snowflake shipped; object-storage and open table format are strategic direction) |
| [Data Stewardship & Lineage Governance Strategy](data-stewardship-strategy.md) | **Status:** v0.17.0 core shipped; later review, quality, and external-catalog lifecycle phases remain candidate work |
| [ETL-SQL Deployment Profile and Portability Strategy](deployment-profile-strategy.md) | **Status:** Active product strategy; certification implementation is candidate work |
| [ETL-SQL Engine Architecture Separation Plan](engine-upgrade-strategy.md) | **Status:** Historical roadmap — not current implementation guidance |
| [ETL-SQL Enterprise Platform Strategy](enterprise-platform-strategy.md) | **Status:** Active product strategy |
| [Fuzzy Matching Strategy](fuzzy-matching-strategy.md) | **Status:** Phases 1–4 shipped; Phase 5 deferred |
| [Portal Governance Dashboard Strategy](governance-dashboard-strategy.md) | **Status:** Candidate roadmap work |
| [Large Dataset Handling](large-datasets.md) | **Status:** Implemented for ETL-SQL 0.7.0 |
| [Lineage & Data Governance Strategy](lineage-strategy.md) | **Status:** Active/partially stale strategy — audit before implementation |
| [ETL-SQL Portal — Development Strategy](portal-strategy.md) | **Status:** Historical roadmap — reconcile before using for implementation |
| [ETL-SQL Presentation Layer Specification](presentation-layer-strategy.md) | **Status:** Partly implemented (TUI editor, VS Code extension, and basic console output are fully realized; advanced interactive views are strategi... |
| [Strategy: ETL-SQL Query Execution Efficiency](query-execution-efficiency-strategy.md) | **Status:** Active performance strategy |
| [Release Capability Matrix](release-capability-matrix.md) | This matrix maps product claims to release evidence. Use it before tagging a release, and keep the public release notes no stronger than the strong... |
| [Release Workflow Strategy](release-workflows.md) | ETL-SQL releases are local-first while the product remains owner-controlled. The intended flow is: |
| [Report-SQL Post-Launch Strategy](report-sql-strategy.md) | **Status:** Historical roadmap/backlog — reconcile before using for implementation |
| [Script Security Strategy](script-security-strategy.md) | **Status:** Implemented (Hash-pinning policies and session-password decryption features are in production) |
| [Source Boundary Migration Plan](source-boundary-migration-plan.md) | ETL-SQL has become more consolidated, but source-tree cleanup should stay incremental. The current priority is to make ownership boundaries obvious... |
| [Subscription Parameters Strategy](subscription-parameters-strategy.md) | **Status:** Implemented (Relative date parameters, multi-value lists, and parameterized subscriptions are fully functional in the engine and portal) |
| [Test Strategy](test-strategy.md) | ETL-SQL's test suite protects a broad product surface: parser and AST behavior, engine semantics, security rules, file and connector orchestration,... |
| [Workstation and Portal Unified Script Editor Roadmap](workstation-and-portal-editor-roadmap.md) | This document defines the architecture, design patterns, and implementation plan for the unified web-based coding area of **ETL-SQL**. It aligns th... |
