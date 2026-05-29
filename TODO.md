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

## UI

Create a cross-platform UI for creating ETL-SQL and Reports.  This would be similar to TUI in that it ships with the product and does not require anything else like VS Code.

- [ ] Cross-platform UI (Avalonia UI?  I'm open here, doesn't even need to be c#)
- [ ] Users create a DAG, visualizing pills containing steps and connecting them by arrows.  Connections and options would be set in a window pane.
- [ ] On a separate tab users can also drag and drop chart elements to create reports, visualizing a Power BI-esc reporting page where they can set do a more WYSIWYG design of the report.  Options would be set in a pane on the right hand side
- [ ] Script editor would be included to allow the user to type scripts.
- [ ] Buttons to go back and forth between script and UI view.  Create script takes what you built and shows the script view.  Update UI takes script and builds UI.  Some guardrails are needed.  Must have a PAGE defined in the report, can't be in the middle of creating an object like CONNECTION, VISUAL, etc.  Each statement must be runnable.
- [ ] What it is not is a query builder
- [ ] Output is saved as a etl-sql script, nothing else.  We are script-first.

Questions:
- What fails?
- What is a bad idea?
- What works?
- What will be difficult?
- Why should we not do this?
- Why should we do this?
