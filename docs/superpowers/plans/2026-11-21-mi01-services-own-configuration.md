# MI01 Services Own Their Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The MSI runs no custom actions. The Orchestrator and Portal create and migrate their own configuration on first start, and upgrades keep operator edits.

**Architecture:** Every host layers `appsettings.defaults.json` (MSI only) → installer registry values → `appsettings.json` (operator) → `appsettings.secrets.json`. A first-start bootstrap in `ETL-SQL.Core`, run by both services under a machine-wide mutex, moves and generates secrets, slims a v0.22.0 operator file once, and writes the first-run sign-in. The MSI removes the old product after `InstallExecute` and never overwrites `appsettings.json`.

**Tech Stack:** .NET 10 (Microsoft.Extensions.Configuration, System.Text.Json.Nodes, Microsoft.Win32.Registry, System.Security.AccessControl), WiX 3.14, PowerShell 7, xUnit.

**Spec:** `docs/releases/v0.23.0-mi01-services-own-configuration-design.md`

## Global Constraints

- No `CustomAction` with `BinaryKey`, `JScriptCall`, `VBScriptCall`, `Script` or `DllEntry` in `Installer.wxs`.
- `MajorUpgrade Schedule="afterInstallExecute"`.
- The `AppSettings` component keeps Id `AppSettings`, File Id `appsettings_json`, target `INSTALLFOLDER\appsettings.json`, `Guid="*"`, and gains `NeverOverwrite="yes"`.
- Registry key: `HKLM\Software\ETL-SQL\Install` (64-bit view). Value names: `InstallFolder`, `OrchSurface`, `StewardSurface`, `ReportPortal`, `Reporting`, `Designer`, `Scheduling`, `Operations`, `OrchestratorDatabasePath`.
- Mutex name: `Global\ETL-SQL-InstalledConfiguration`.
- Generated secrets: 32 random bytes, base64. First-run password: same shape as the script (24 base64 chars with `/`→`x`, `+`→`y`, then `9!`); sign-in file `first-run-password.txt` = `user\r\npassword\r\n`, UTF-8 no BOM.
- Protected files (secrets, sign-in): created through a handle opened with the final ACL: SYSTEM (`S-1-5-18`) and Administrators (`S-1-5-32-544`) FullControl, inheritance protected.
- Slimming marker: `Installer:SlimmedFrom = "0.22.0"` in `appsettings.secrets.json`.
- Logs and exceptions from the bootstrap carry fixed messages only, never a setting value.
- Install paths, profiles, service names, ports unchanged. DEB unchanged.
- Stage files by explicit list; never stage `tests/ETL-SQL.Tests/Scale/ScratchOperatorAllocProfile.cs`. Keep each file's existing line endings.

## Review Focus

1. A developer checkout or container on a machine that also has the MSI installed must not pick up the installed registry values or run the bootstrap: both are gated on `InstallFolder` equal to the host's base directory (Task 1, Task 3 tests).
2. An operator who lists their own `Security:ApprovedSafeZones` must keep them *and* the install folder: the registry entry uses index `65535`, which never collides (Task 1 test).
3. Both services starting at once on a fresh install must end with one API key and one signing secret that both read (Task 3 concurrency test).
4. Slimming runs exactly once: an operator who later sets a value equal to a v0.22.0 default keeps it (Task 4 test).
5. An operator file with comments or trailing commas (legal for .NET JSON configuration) must not stop the service (Task 3 test).

---

### Task 1: Installer settings layers and the registry source

**Files:**
- Create: `src/ETL-SQL.Core/Common/InstallerRegistry.cs`
- Create: `src/ETL-SQL.Core/Common/InstallerRegistryConfigurationSource.cs`
- Modify: `src/ETL-SQL.Core/Common/ConfigurationExtensions.cs` (add `DefaultsFileName`, `AddInstallerLayers`)
- Modify: `src/ETL-SQL.App/App/DependencyInjectionSetup.cs:66-69`, `src/ETL-SQL.TUI/App/TuiDependencyInjectionSetup.cs:63-65`, `src/ETL-SQL.LanguageServer/Program.cs:74-76`, `src/ETL-SQL.Orchestrator.Service/Program.cs:67-72`, `src/ETL-SQL.Portal/Program.cs:44-45`
- Test: `tests/ETL-SQL.Tests/Core/InstallerSettingsLayersTests.cs`

**Interfaces:**
- Produces:
  - `public interface IInstallerRegistry { IReadOnlyDictionary<string, string>? ReadInstallValues(); }` — null when the key is absent or not Windows.
  - `public sealed class WindowsInstallerRegistry : IInstallerRegistry` (`Instance` static); opens `HKLM\Software\ETL-SQL\Install` with `RegistryView.Registry64`.
  - `public static string? InstallerRegistry.MatchingInstallFolder(IReadOnlyDictionary<string,string>? values, string baseDirectory)` — returns the folder when it equals `baseDirectory` (full path, trailing separators trimmed, OrdinalIgnoreCase), else null.
  - `public const string ConfigurationExtensions.DefaultsFileName = "appsettings.defaults.json";`
  - `public static IConfigurationBuilder AddInstallerLayers(this IConfigurationBuilder builder, string baseDirectory, IInstallerRegistry? registry = null)` — inserts, in this order, an optional reload-on-change JSON source for `DefaultsFileName` and the registry source, immediately **before the first** `appsettings*.json` source (excluding the secrets file); appends both when there is none.
  - Registry source mapping (only when `MatchingInstallFolder` is non-null): `Portal:Surface` from `OrchSurface`/`StewardSurface`/`ReportPortal` with the truth table in `configure-portal-jwt.ps1:232-258` (only when at least one of the three is present); `Portal:Modules:{Reporting,Designer,Scheduling,Operations}` = `"true"`/`"false"` from `"1"`; `Portal:Orchestrator:DatabasePath` from `OrchestratorDatabasePath`; `Security:ApprovedSafeZones:65535` = install folder without trailing separator.

- [ ] **Step 1: Write the failing tests** in `InstallerSettingsLayersTests` with a fake `IInstallerRegistry`:
  - `DefaultsSitUnderAppSettingsAndSecrets`: defaults `{"A":"d","B":"d","C":"d"}`, appsettings `{"B":"o","C":"o"}`, secrets `{"C":"s"}` → `A=d, B=o, C=s`.
  - `WebApplicationDefaultsGetLayersBeforeTheirAppSettings`: a builder that already has `appsettings.json` and `appsettings.Development.json` sources → defaults index < appsettings index.
  - `SurfaceTruthTable` (Theory over all 7 rows + `0/0/0 → All`).
  - `ModulesAndDatabasePathMap`.
  - `InstallFolderJoinsOperatorSafeZones`: appsettings `ApprovedSafeZones ["C:\\a","C:\\b"]` + registry → bound `string[]` is `["C:\\a","C:\\b","C:\\Program Files\\ETL-SQL"]`.
  - `OperatorValueBeatsRegistry`: appsettings `Portal:Surface = "ReportOnly"`, registry says All → `ReportOnly`.
  - `OtherFolderGetsNoRegistryValues`: registry `InstallFolder` ≠ base dir → no `Portal:Surface` key.
- [ ] **Step 2: Run** `dotnet test tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj --filter "FullyQualifiedName~InstallerSettingsLayersTests"` — expect compile failure.
- [ ] **Step 3: Implement** the three types and `AddInstallerLayers` (insertion mirrors `AddInstalledSecrets`'s source scan).
- [ ] **Step 4: Wire the hosts.** Each host calls `.AddInstallerLayers(baseDir)` after its `appsettings.json` source and before `AddInstalledSecrets`. Portal: call it right before `AddInstalledSecrets` (the `WebApplication` defaults already hold `appsettings.json`). Orchestrator: drop its duplicate `SetBasePath/AddJsonFile` only if `WebApplication`'s own source points at the same file; otherwise keep it.
- [ ] **Step 5: Run** the filter → PASS; also `--filter "FullyQualifiedName~InstalledSecretsConfigurationTests"` → PASS.
- [ ] **Step 6: Commit** `Layer appsettings.defaults.json and installer registry values under appsettings.json (MI01)`.

### Task 2: Direct settings readers use the same layers

**Files:**
- Modify: `src/ETL-SQL.Core/Common/ConfigurationExtensions.cs` (add `BuildInstalledSettings`)
- Modify: `src/ETL-SQL.App/App/BackupRestoreService.cs:143-150` and `:604-612`, `src/ETL-SQL.App/App/DataPurgeService.cs:310-313`
- Test: `tests/ETL-SQL.Tests/App/BackupRestoreServiceTests.cs`, `tests/ETL-SQL.Tests/App/DataPurgeServiceTests.cs`

**Interfaces:**
- Consumes: `AddInstallerLayers` (Task 1).
- Produces: `public static IConfiguration ConfigurationExtensions.BuildInstalledSettings(string directory, IInstallerRegistry? registry = null)` = optional `appsettings.json` + `AddInstallerLayers` + `AddInstalledSecrets`, no environment variables.

- [ ] **Step 1: Write failing tests:** `InPlaceRestore_ResolvesPathsFromTheDefaultsFile` (target has `appsettings.defaults.json` holding `Portal:DatabasePath`, `appsettings.json` = `{}` → restore resolves that path) and `PurgeData_FindsPathsDeclaredOnlyInDefaults` (same shape for `admin purge-data --install-dir`).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~BackupRestoreServiceTests|FullyQualifiedName~DataPurgeServiceTests"` → the two new tests FAIL.
- [ ] **Step 3: Implement** `BuildInstalledSettings`; replace the three `new ConfigurationBuilder().AddJsonFile(...appsettings.json...)` sites for an install folder with it. The backup-extract side (`dataExtract`) stays a plain read of the backed-up file. The dedicated-tenant path (`:53`) is unchanged.
- [ ] **Step 4: Run** the filter → all PASS.
- [ ] **Step 5: Commit** `Resolve install-folder settings through the defaults and registry layers (MI01)`.

### Task 3: First-start bootstrap (secrets and sign-in)

**Files:**
- Create: `src/ETL-SQL.Core/Common/ProtectedInstallFile.cs`
- Create: `src/ETL-SQL.Core/Common/InstalledConfigurationBootstrap.cs`
- Test: `tests/ETL-SQL.Tests/Deploy/InstalledConfigurationBootstrapTests.cs`

**Interfaces:**
- Consumes: `InstallerRegistry.MatchingInstallFolder`, `ConfigurationExtensions.InstalledSecretPaths`, `InstalledSecretsFileName`.
- Produces:
  - `[SupportedOSPlatform("windows")] internal static class ProtectedInstallFile { public static void Write(string path, byte[] bytes); }` — stages `path + "." + guid + ".tmp"` created via `FileSystemAclExtensions.Create(FileInfo, FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, security)` with the Global Constraints ACL plus the current identity; replaces with `File.Replace` (or moves when absent); reapplies the final ACL; deletes the temp on failure.
  - `public sealed record InstalledBootstrapOptions(string BaseDirectory, string? RegisteredInstallFolder, string MutexName = @"Global\ETL-SQL-InstalledConfiguration");`
  - `[Flags] public enum InstalledBootstrapResult { Skipped = 0, Ran = 1, SecretsMoved = 2, SecretsGenerated = 4, Slimmed = 8, FirstRunWritten = 16 }`
  - `public static class InstalledConfigurationBootstrap { public static InstalledBootstrapResult Ensure(string baseDirectory); public static InstalledBootstrapResult Ensure(InstalledBootstrapOptions options); }` — the first overload reads `WindowsInstallerRegistry`. Throws `InvalidOperationException("ETL-SQL installed configuration could not be prepared.")` (inner exception kept for the log, message fixed) on any failure.
- Order inside the mutex: read both files (JSON object roots only; `JsonDocumentOptions { CommentHandling = Skip, AllowTrailingCommas = true }`) → move secrets → (Task 4 slimming hook) → generate missing → write secrets file → rewrite operator file only if changed (temp + `File.Replace`, keeps its ACL; UTF-8 no BOM, indented) → write sign-in file.

- [ ] **Step 1: Write failing tests** (Windows-only facts, temp install dir, unique mutex name per test):
  - `NotInstalledHere_TouchesNothing` (registered folder differs / null → `Skipped`, no files created).
  - `FreshInstall_GeneratesEverySecretIntoTheProtectedFile`: `appsettings.json` `{}`, defaults present → secrets file holds non-empty `Portal:Jwt:Secret`, `Portal:Dataset:AtRestKey`, `Orchestrator:ApiKey`, `Orchestrator:IdentitySigningSecret`; `Portal:Orchestrator:ApiKey`/`IdentitySigningSecret` equal the Orchestrator ones; each decodes to 32 bytes; ACL has exactly the two SIDs (+ creator removed after final apply) and is protected.
  - `FreshInstall_WritesOneTimeSignIn` (no `portal.db`) and `ExistingPortalDb_WritesNoSignIn`; `RetryReusesSavedPassword`.
  - `SecretsInAppSettings_MoveAndLeaveEmptyValues` (string, array `PreviousSecrets`, both present → secrets-file value wins; operator file keeps `""`/`[]`; other operator keys and `UpgradeSentinel` untouched).
  - `SecondRun_RegeneratesNothing` (byte-identical secrets file, result has no `SecretsGenerated`).
  - `TwoConcurrentRuns_AgreeOnSharedSecrets` (two threads `Ensure` the same dir with the same mutex name → one `Orchestrator:ApiKey`, mirrored).
  - `OperatorFileWithCommentsAndTrailingCommas_IsAccepted`.
  - `NonObjectRoot_FailsWithoutChangingAnything` (Theory: `[]`, `"x"`, `null`, `{` ) — fixed message, files byte-identical.
  - `LockedSecretsFile_FailsAndKeepsKeys` and `FailedSecretWrite_PublishesNoSignIn` (ported from `InstallerConfigurationScriptTests:97,201`).
  - `MessagesNeverContainValues`: failing run's exception chain `.Message`s don't contain a planted secret.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~InstalledConfigurationBootstrapTests"` → compile failure.
- [ ] **Step 3: Implement** `ProtectedInstallFile` and `InstalledConfigurationBootstrap`.
- [ ] **Step 4: Run** the filter 3 times → PASS every time (concurrency test).
- [ ] **Step 5: Commit** `Generate and move installed secrets on first service start (MI01)`.

### Task 4: One-time slimming of a v0.22.0 operator file

**Files:**
- Create: `src/ETL-SQL.Core/Common/Resources/appsettings.v0.22.0.json` (exact bytes of `git show v0.22.0:src/appsettings.json`)
- Modify: `src/ETL-SQL.Core/ETL-SQL.Core.csproj` (EmbeddedResource, LogicalName `ETL_SQL.appsettings.v0.22.0.json`)
- Modify: `src/ETL-SQL.Core/Common/InstalledConfigurationBootstrap.cs`
- Test: `tests/ETL-SQL.Tests/Deploy/InstalledConfigurationBootstrapTests.cs`

**Interfaces:**
- Produces: `internal static bool InstalledConfigurationBootstrap.RemoveLeavesEqualTo(JsonObject operatorFile, JsonObject shippedDefaults)` — removes every property whose value is a scalar or array `JsonNode.DeepEquals` to the same path in the defaults; recurses into objects; removes objects left empty; returns whether anything changed. Runs after the move step, only when the secrets file has no `Installer:SlimmedFrom`; afterwards the marker is set on every bootstrap that ran (fresh installs included).

- [ ] **Step 1: Write failing tests:**
  - `V022File_KeepsOnlyRealEdits`: operator file = the embedded v0.22.0 file with `Portal:Surface="ReportOnly"`, an extra `UpgradeSentinel:Value="kept"` and `Security:ApprovedSafeZones` = default + install folder → result has exactly those three paths; result has `Slimmed`; marker set.
  - `SlimmingRunsOnce`: run, then write a value equal to a v0.22.0 default into the operator file, run again → value kept.
  - `FreshInstall_SetsMarkerWithoutSlimming` (`{}` → marker set, no `Slimmed`).
- [ ] **Step 2: Run** the filter → the three FAIL.
- [ ] **Step 3: Implement** and add the resource (copy with `git show v0.22.0:src/appsettings.json > ...` from PowerShell using `-Encoding` that preserves the bytes; verify with `git hash-object` equals `git rev-parse v0.22.0:src/appsettings.json`).
- [ ] **Step 4: Run** the filter → PASS.
- [ ] **Step 5: Commit** `Slim a kept v0.22.0 appsettings.json to the operator's edits, once (MI01)`.

### Task 5: Services run the bootstrap

**Files:**
- Modify: `src/ETL-SQL.Orchestrator.Service/Program.cs` (before `builder.Configuration` setup), `src/ETL-SQL.Portal/Program.cs` (before `AddInstallerLayers`)
- Test: `tests/ETL-SQL.Tests/Deploy/InstalledConfigurationBootstrapTests.cs` (`SourceCheck_BothServicesEnsureBeforeBuildingConfiguration`: reads both `Program.cs` files and asserts `InstalledConfigurationBootstrap.Ensure(` appears before `AddInstalledSecrets(`)

**Interfaces:**
- Consumes: `InstalledConfigurationBootstrap.Ensure(string)` (Task 3).

- [ ] **Step 1: Write the source-check test; run → FAIL.**
- [ ] **Step 2: Call** `if (OperatingSystem.IsWindows()) InstalledConfigurationBootstrap.Ensure(AppContext.BaseDirectory);` in both, before configuration is built; a throw ends startup (the Windows SCM records the failure; the message is fixed).
- [ ] **Step 3: Run** the test → PASS; `dotnet build ETL-SQL.slnx -c Release` → 0 errors, 0 new warnings.
- [ ] **Step 4: Commit** `Run the installed-configuration bootstrap at Orchestrator and Portal start (MI01)`.

### Task 6: MSI copies files and registers services only

**Files:**
- Create: `src/ETL-SQL.Installer/operator-appsettings.json` (`{}` + newline)
- Modify: `src/ETL-SQL.Installer/Installer.wxs`, `scripts/build-msi.ps1:145-151`, `scripts/Test-InstallerSources.ps1:55,74-76`, `scripts/checks/test-installer-sources.mjs`
- Delete: `src/ETL-SQL.Installer/configure-portal-jwt.ps1`, `src/ETL-SQL.Installer/installer-actions.js`, `scripts/checks/test-installer-actions.mjs` (and its entry wherever checks are listed: grep `test-installer-actions`)
- Modify: `tests/ETL-SQL.Tests/Deploy/InstallerConfigurationScriptTests.cs` — delete the Windows-script tests; keep `BothInstallersAndRestore_...` comparing `InstalledSecretPaths` with `build-linux-packages.sh` (drop the `.ps1` leg) and the DEB tests.
- Create: `scripts/checks/test-installer-no-custom-actions.mjs`

**Interfaces:**
- Consumes: registry value names (Global Constraints); `DefaultsFileName` (Task 1).

- [ ] **Step 1: Write** `test-installer-no-custom-actions.mjs` asserting on `Installer.wxs`: no `<CustomAction` carrying `BinaryKey|JScriptCall|VBScriptCall|Script|DllEntry`; no `<Binary `; `MajorUpgrade` has `Schedule="afterInstallExecute"`; `Component Id="AppSettings"` has `NeverOverwrite="yes"` and its File Id `appsettings_json` with `Name="appsettings.json"`; a component installs `appsettings.defaults.json`; registry key `Software\ETL-SQL\Install` with all nine value names; a `RegistrySearch` for each of the seven choice properties. Run with Node 22 → FAIL.
- [ ] **Step 2: Edit `Installer.wxs`:** remove the five custom actions, their sequence rows, `PREVIOUS_SETTINGS`, `Binary`, `PortalJwtScript` (and its `ComponentRef`s) and stale comments; `AppSettings` → `Source="operator-appsettings.json" Name="appsettings.json" NeverOverwrite="yes"`; add `AppSettingsDefaults` (`Guid="*"`, `File Id="appsettings_defaults_json" Source="$(var.PublishDir)\appsettings.json" Name="appsettings.defaults.json"`) referenced wherever `AppSettings` is; add registry components (one with `InstallFolder` + `OrchestratorDatabasePath` = `[WindowsFolder]System32\config\systemprofile\AppData\Local\etlsql-data\etlsql.db` in the Orchestrator and Portal features, one with the seven choice values in the Portal features; each with an explicit fixed GUID and a registry KeyPath); `Property` + `RegistrySearch` (`Root="HKLM" Win64="yes" Type="raw"`) for each choice property, keeping today's default `Value`s.
- [ ] **Step 3: Update** `build-msi.ps1` (no change needed if it still copies `src/appsettings.json` into `$BuildDir`; confirm `operator-appsettings.json` resolves from the installer dir) and the source checks (`Test-InstallerSources.ps1`, `test-installer-sources.mjs`) to drop the deleted files.
- [ ] **Step 4: Run** `node scripts/checks/test-installer-no-custom-actions.mjs`, `node scripts/checks/test-installer-sources.mjs`, `pwsh -File scripts/Test-InstallerSources.ps1`, and `pwsh -File scripts/build-msi.ps1` (local WiX build) → all succeed; `dotnet test ... --filter "FullyQualifiedName~InstallerConfigurationScriptTests"` → PASS.
- [ ] **Step 5: Commit** `MSI: drop the script custom actions; operator-owned appsettings.json and late removal (MI01)`.

### Task 7: Certification proves the upgrade

**Files:**
- Create: `scripts/checks/Compare-MsiComponentGuids.ps1`
- Modify: `scripts/Test-MsiPortalUpgrade.ps1` (`Assert-Setting` near `:178-190`; upgrade steps `:378-393`)

**Interfaces:**
- Produces: `Compare-MsiComponentGuids.ps1 -Previous <msi> -Current <msi>` — reads `SELECT Component, ComponentId FROM Component` from both through the `WindowsInstaller.Installer` COM object; exits 1 listing every component Id present in both with a different `ComponentId`; components new or dropped are reported, not failed.

- [ ] **Step 1: Implement** the compare script; run it locally against the v0.22.0 MSI (`gh release download v0.22.0 -p '*.msi'` into scratchpad) and the Task 6 build → exit 0. If not 0, stop: approach A's premise failed — report to the owner (fallback B).
- [ ] **Step 2: Edit `Test-MsiPortalUpgrade.ps1`:** `Assert-Setting` resolves `appsettings.defaults.json` → `appsettings.json` → `appsettings.secrets.json`; first upgrade step `upgrade: component GUIDs match the previous release` calls the compare script; add `upgrade: the previous operator file is kept` (the `UpgradeSentinel` check already covers values — add that `appsettings.defaults.json` exists and `configure-portal-jwt.ps1` does not), `upgrade: installer choices recorded` (registry `InstallFolder` equals `$installDir`); fresh install: `fresh: appsettings.json is the operator template` (parses to an object with no secret values).
- [ ] **Step 3: Run** `pwsh -File scripts/checks/Test-MsiBackupEvidence.ps1` and the workflow checks (`node scripts/checks/check-workflows.mjs`, `node scripts/checks/test-workflows.mjs`) → PASS. The MSI run itself is proven on GitHub (Task 9).
- [ ] **Step 4: Commit** `MSI certification: component GUIDs and the kept operator file (MI01)`.

### Task 8: Docs and records

**Files:**
- Modify: `TODO.md` (MI01 → `[x]` with evidence once Task 9 is green), `ROADMAP.md` (slice 1 status), `CHANGELOG.md` (`[0.23.0]` Changed), `BREAKING_CHANGES.md` (operators: full defaults now in `appsettings.defaults.json`; edit `appsettings.json` as before; COMPAT note), `docs/releases/v0.23.0.md`, and the Windows install/administration page that tells operators to edit `appsettings.json` (grep `appsettings.json` under `docs/administration`, `docs/getting-started`).

- [ ] **Step 1:** Update the docs; run `node scripts/release-preflight.mjs` (or the repo's docs/link check named in AGENTS.md) → PASS.
- [ ] **Step 2: Commit** `Document operator-owned appsettings.json and the defaults file (MI01)`.

### Task 9: Push and certify

- [ ] **Step 1:** Local lanes: `dotnet test tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj --filter "FullyQualifiedName~Installer|FullyQualifiedName~InstalledSecrets|FullyQualifiedName~BackupRestore|FullyQualifiedName~DataPurge"` and `dotnet test tests/ETL-SQL.Portal.Tests --filter "FullyQualifiedName~UpgradePathDrill"` → 0 failed. Run the `slopwatch` skill on the change.
- [ ] **Step 2:** Ask the owner to push (batch rule). Node 22 first on PATH: `$env:PATH = "$PWD\artifacts\section1\toolchain\node-v22.23.3-win-x64;$env:PATH"`.
- [ ] **Step 3:** Watch `gh run list --branch release/v0.23.0`: CI, CodeQL, MSI In-Place Upgrade (fresh, upgrade from v0.22.0, rollback via `admin restore`), Native, DEB, Supervisor all green. Then tick MI01 in TODO.md with the run ids (Task 8 Step 1 follow-up commit).
