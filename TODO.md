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
