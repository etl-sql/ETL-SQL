# ETL-SQL Development Roadmap

## VS Code Sidebar Modernization (HTML/React Wrapper)

## TUI Performance & Dashboard Issues
- [ ] **Pipeline execution tree**  When running loops it should just keep restating the same node multiple times rather than print all the iterations.  That really gums up the view when it prints so much.  

---
## Architecture Documentation Gaps  ** For Claude only**

The following architecture documents are missing. Identified 2026-04-14.

### Lower Priority
- [ ] **Docker / Infrastructure Commands** — `DockerContainerManager` and `USE DOCKER` are referenced in the README but the spawn lifecycle, container polling, and session-teardown cleanup are undocumented.
- [ ] **Window Functions & Advanced Operators** — `ExternalWindowEngine` (PARTITION BY, ROW_NUMBER, RANK, etc.) supports signature-based grouping and disk-spilling for hyper-scale scenarios.

---
### Missing ETL Language Features

These are capabilities common in production ETL tools that are either absent from the language or absent from the documentation (unclear which without deeper code investigation):
- [ ] **Schema drift detection** — No mechanism to detect when a source schema (column names, types) changes between runs. Common in production ETL as a guard against upstream changes breaking a pipeline silently.

