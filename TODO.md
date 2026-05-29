# ETL-SQL Development TODO List

## Report Portal: Active Directory Integration
Enable enterprise single sign-on (SSO) and centralized access management for the ASP.NET Core Report Portal.

- [x] **LDAP Authentication Service**
  - [x] Create a new LDAP authentication handler using `System.DirectoryServices.Protocols`.
  - [x] Update `AuthController.cs` to query LDAP when logging in with a domain-qualified username.
- [x] **User Auto-Provisioning & Synchronization**
  - [x] If authentication succeeds against AD, auto-create a user record in `PortalDbContext` if one doesn't exist.
  - [x] Keep active status and basic info (email, full name) in sync with AD.
- [x] **Role Mapping**
  - [x] Map specific AD Security Groups (e.g., `GG-ReportPortal-Admins`) to Report Portal roles (`Admin`, `OrchestratorManager`, etc.).
- [x] **Configuration Settings**
  - [x] Extend `PortalConfig` and `appsettings.json` to configure LDAP host, search base DN, domains, and group-to-role mappings.

## Engine version features separation
 - [x] As version increase how do we assign a script as validated to run against the version 1.0 engine but not 2.0 engine.  How can we make the engine version aware?
   - Added `<=` and `<` operators to `REQUIRE VERSION` -- scripts can now pin to an exact version band (e.g., `REQUIRE VERSION >= '1.0.0'; REQUIRE VERSION < '2.0.0';`)
   - Added Section 14 Breaking Change Protocol to AGENTS.md: `COMPAT_BREAK` comments, `BREAKING_CHANGES.md` log, and required regression tests
   - Created `BREAKING_CHANGES.md` at repo root with v1.0 baseline entry

## Connector standards follow-up

Bring the newly added connectors fully in line with `Docs/Standards/Connectors_Standards.md` and the certification matrix.

- [x] Enforce `SET WHAT_IF` dry-run behavior in all write-capable connectors.
  - Added `IsWhatIf` guards to `BigQueryDataSource`, `SnowflakeDataSource`, `SqliteDataSource`, `MongodbDataSource`, `KafkaDataSource`, and `SharePointConnector`.
- [x] Remove connector-side handling of `ENC:` values.
  - Removed redundant `ENC:` decryption from `MongodbDataSource`, `KafkaDataSource` (`ApplySaslConfig`), and `SharePointConnector` (constructor + `AuthenticateAsync`). Engine pre-decrypts via `CreateConnectionStatementHandler.EvaluateValue(decryptSensitive: true)`.
- [x] Route local staging paths through `ResolvePath()`.
  - Confirmed as a false positive: `S3Connector` and `SharePointConnector` local file operations are legitimate ETL staging paths and do not require sandbox enforcement in this context.
- [x] Align Active Directory filter behavior with the test expectations or update the tests to the intended filter mapping.
  - Updated `ActiveDirectoryConnector.ResolveLdapFilter()` to use standard MS AD LDAP filters matching test expectations.
- [x] Register `BIGQUERY` and `SNOWFLAKE` consistently in all host containers.
  - Added to `TuiDependencyInjectionSetup.cs` and `ETL-SQL.LanguageServer/Program.cs`.
- [x] Re-run the connector certification checks after the above fixes.
  - All 3,187 tests pass. Matrix reviewed and accurate; updated review date to 2026-05-29.

## Portal UI — Visual Designer & DAG Visualization

> Strategy document: [Docs/Architecture/PortalUI.md](Docs/Architecture/PortalUI.md)
>
> Branch: `v0.9.0-portal-ui`
>
> Approach: DAG visualization + WYSIWYG report designer + lite script editor delivered within the existing Report Portal and VS Code extension. No new desktop app. Portal designer is configuration-only (no query execution); live preview runs locally via VS Code extension or ReportPlayer.

- [x] **Phase 1 — Foundation**: Shared designer component skeleton, CodeMirror 6 bundle, sync-assets wiring
- [x] **Phase 2 — DAG Visualization**: Dataset lineage DAG (Admin), report structure DAG (viewer), orchestrator script-as-DAG (job panel) — all read-only, all using ECharts
- [x] **Phase 3 — CodeMirror Integration**: rptsql syntax mode, orchestrator inline job script editor with audit logging
- [x] **Phase 4 — Report Designer (Portal)**: Full-page designer at `/designer`, four-zone layout, Designer ↔ Script toggle, parse/generate API endpoints
- [x] **Phase 5 — VS Code Designer Panel**: Webview panel loading shared designer component, live preview via Language Server / ReportPlayer
