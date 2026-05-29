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
 - [ ] As version increase how do we assign a script as validated to run against the version 1.0 engine but not 2.0 engine.  How can we make the engine version aware?

## Connector standards follow-up

Bring the newly added connectors fully in line with `Docs/Standards/Connectors_Standards.md` and the certification matrix.

- [ ] Enforce `SET WHAT_IF` dry-run behavior in all write-capable connectors.
  - `BigQueryDataSource`, `SnowflakeDataSource`, `SqliteDataSource`, `MongodbDataSource`, `KafkaDataSource`, and `SharePointConnector` currently have write/destructive paths that do not consistently short-circuit on `IsWhatIf`.
- [ ] Remove connector-side handling of `ENC:` values.
  - `MongodbDataSource` still decrypts `ENC:`-prefixed options inside the connector boundary instead of relying on the engine.
- [ ] Route local staging paths through `ResolvePath()`.
  - `S3Connector` and `SharePointConnector` still use direct `File.Exists`, `File.OpenRead`, `File.Create`, and related path operations on caller-supplied local paths.
- [ ] Align Active Directory filter behavior with the test expectations or update the tests to the intended filter mapping.
  - Current implementation returns broader LDAP filters than `tests/ETL-SQL.Tests/Connectors/SharePointAndADConnectorTests.cs` expects.
- [ ] Register `BIGQUERY` and `SNOWFLAKE` consistently in all host containers.
  - They are present in Orchestrator registration, but not in the TUI and Language Server registration blocks.
- [ ] Re-run the connector certification checks after the above fixes.
  - Confirm the matrix in `Docs/Standards/Connector_Certification_Matrix.md` matches actual behavior and test coverage.
