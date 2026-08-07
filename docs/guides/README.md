# GUIDES Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [Catalog Search & Discovery Guide](catalog-search.md) | The ETL-SQL Portal includes a fuzzy, tokenized catalog search engine that allows non-technical business consumers and developers to discover report... |
| [Validating Data Quality](data-quality.md) | Schema checks tell you the *shape* of your data is right. They say nothing about whether the values |
| [Data Stewardship and Impact Analysis](data-stewardship-impact.md) | This guide is for administrators, data stewards, report publishers, and CI/CD owners who need to use ETL-SQL lineage metadata before publishing das... |
| [ETL-SQL Pipeline & Report-SQL Best Practices Guide](etl-sql-best-practices.md) | Recommended patterns, security rules, and boilerplate templates for script authors and dashboard designers. |
| [ETL-SQL FAQ & Troubleshooting Guide](faq.md) | Common questions, gotchas, and their solutions. If you're stuck, start here. |
| [ETL-SQL User Manual: Thinking in Pipelines](getting-started.md) | Welcome to ETL-SQL. This guide helps you transition from "Single Database SQL" to "Multi-Context Data Flow." It is a **narrative onboarding** — the... |
| [Logging and Performance Tuning](logging-and-performance.md) | Where ETL-SQL writes its logs, how to turn up detail when something is wrong, and the levers that |
| [ETL-SQL Migration Guide (v0.18.0)](migration-guide.md) | ETL-SQL v0.18.0 is the current release baseline. Because the app has not had a public stable release before this baseline, this guide is mainly for... |
| [ETL-SQL Notebooks (.etlnb)](notebook-guide.md) | ETL-SQL Notebooks provide a stateful, iterative environment for writing and running ETL-SQL cells directly inside VS Code. |
| [One-person quality loop](one-person-quality-loop.md) | This workflow gives one operator a source-controlled pipeline, policy, non-zero quality gate, local schedule, durable history, and two reports with... |
| [Orchestrating Pipelines & DAGs](pipelines-and-dags.md) | ETL-SQL handles pipeline coordination with normal script control flow: `RUN SCRIPT`, `PARALLEL`, `IF`, `TRY...CATCH`, scheduler jobs, and file or d... |
| [ETL-SQL Portal: User Guide](portal-user.md) | The Portal is a web application that lets you browse, run, and subscribe to reports built with Report-SQL scripts. You don't need to know ETL-SQL s... |
| [Report Ownership & Data Freshness Badges](report-badges-freshness.md) | Published reports display standardized metadata badges in the report runtime header and Portal catalog cards. Badges provide immediate visual trust... |
| [Visual Report Builder & Dashboard Designer Guide](report-builder.md) | The **Visual Report Builder & Dashboard Designer** is the integrated WYSIWYG authoring surface for ETL-SQL. It allows developers, analysts, and ste... |
| [Report-SQL Scripting Guide](report-sql.md) | Report-SQL extends ETL-SQL with dedicated statement types for building interactive dashboards: `SET REPORT TITLE`, `CREATE DATASET`, `CREATE VISUAL... |
| [ETL-SQL Sample Guide](sample-guide.md) | This guide describes the provided sample scripts in the `samples/` folder. These samples are organized into topical subfolders (for example, `01_Ba... |
| [Testing](testing.md) | For the overall lane model and cleanup guidance, see Test_Strategy.md. |
| [VS Code Extension](vscode-extension.md) | ETL-SQL ships with a dedicated VS Code extension (`src/etl-sql-vscode/`) that enhances the development experience. The extension communicates with ... |
