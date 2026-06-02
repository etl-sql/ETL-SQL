# ETL-SQL Utility Scripts

This directory contains build, test, utility, and release packaging scripts for the **ETL-SQL** engine. To support developers across Windows, Linux, and macOS, core developer workflows are provided in both PowerShell (`.ps1`) and Bash (`.sh`) versions.

---

## 1. Quick Reference Table

| Script Name | Language | Platform | Description |
| :--- | :--- | :---: | :--- |
| **[`build-debug.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/build-debug.ps1)** / **[`build-debug.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/build-debug.sh)** | PowerShell / Bash | Cross-platform | Builds the .NET solution, VS Code UI (Vite), extension TypeScript compiler, and runs extension unit tests. |
| **[`test-smoke.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/test-smoke.ps1)** / **[`test-smoke.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/test-smoke.sh)** | PowerShell / Bash | Cross-platform | Executes targeted minimal smoke tests divided into specific categories (Core, Security, Reporting, Portal). |
| **[`test-lane.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/test-lane.ps1)** / **[`test-lane.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/test-lane.sh)** | PowerShell / Bash | Cross-platform | The test suite gateway to run individual test lanes (smoke, fast, engine, portal, integration, perf, release, full, benchmarks, slt) with optional coverage mapping. The `perf` lane runs tagged engine hardening tests plus the dedicated perf project; the `fast`, `portal`, and `full` lanes also run the Node lineage UI smoke test. |
| **[`Get-TestLaneInventory.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/Get-TestLaneInventory.ps1)** | PowerShell | Cross-platform | Generates a static Markdown or JSON inventory of discovered xUnit tests by lane, category trait, and project. |
| **[`Test-SltCorpus.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/Test-SltCorpus.ps1)** / **[`Test-SltCorpus.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/Test-SltCorpus.sh)** | PowerShell / Bash | Cross-platform | Runs the SqlLogicTests corpus suite and pipes console logs + TRX file output to a timestamped folder in `slt_results/`. |
| **[`Parse-SltResults.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/Parse-SltResults.ps1)** | PowerShell | Windows / macOS / Linux | Parses TRX files from an SLT run to output a clean color-coded summary of passes, skips, and failed stack traces. |
| **[`Test-AllSamples.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/Test-AllSamples.ps1)** / **[`test-all-samples.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/test-all-samples.sh)** | PowerShell / Bash | Cross-platform | Discovers and executes all samples (`*.etlsql` and `*.rptsql`) in the `samples/` folder to validate engine runtime backward compatibility. |
| **[`Compare-Benchmarks.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/Compare-Benchmarks.ps1)** / **[`compare-benchmarks.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/compare-benchmarks.sh)** | PowerShell / Bash | Cross-platform | Compares BenchmarkDotNet JSON results against a checked-in baseline file and returns an exit code of 1 if regression exceeds a given threshold. |
| **[`Test-ScaleCertification.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/Test-ScaleCertification.ps1)** / **[`test-scale-certification.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/test-scale-certification.sh)** | PowerShell / Bash | Cross-platform | Runs the scale certification test suite (Smoke/Standard/Stress/Provider tiers) and produces JSON and Markdown reports with per-scenario metrics. |
| **[`Test-PreRelease.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/Test-PreRelease.ps1)** / **[`test-pre-release.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/test-pre-release.sh)** | PowerShell / Bash | Cross-platform | Runs the local-first pre-release validation gate with resumable phases, optional Docker/Standard-scale/installer checks, and JSON/Markdown reports under `release-validation/`. |
| **[`sync-assets.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/sync-assets.ps1)** / **[`sync-assets.js`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/sync-assets.js)** | PowerShell / JavaScript | Cross-platform | Synchronizes canonical shared browser assets from `src/ETL-SQL.ReportRuntime` to dependent shell host directories (VS Code extension, portal, player). |
| **[`generate-syntax-index.js`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/generate-syntax-index.js)** | JavaScript | Cross-platform | Regenerates the canonical token inventory appendix inside `Docs/Syntax_Index.md` from `LanguageMetadata.cs`. |
| **[`install.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/install.ps1)** / **[`install.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/install.sh)** | PowerShell / Bash | Cross-platform | Boostrapping workstation installers that download, unpack, and register the ETL-SQL SDK to the user's `PATH`. |
| **[`Master-Release.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/Master-Release.ps1)** | PowerShell | Windows | Orchestrator script to validate tools, compile release binaries, execute smoke tests, build the UI, bundle cross-platform packages, and construct installer setups. |
| **[`publish_release.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/publish_release.ps1)** | PowerShell | Windows | Publishes the CLI applications for different platforms (Windows, Linux, macOS) targeting x64 and arm64 architectures. |
| **[`build_msi.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/build_msi.ps1)** | PowerShell | Windows | Bundles Windows published binaries into an MSI setup package using the WiX candle/light compilation toolset. |
| **[`build_linux_packages.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/build_linux_packages.sh)** | Bash | Linux / WSL | Packages published binaries into Debian (`.deb`) and RedHat (`.rpm`) distributions using standard linux packaging utilities. |
| **[`build_mac_dmg.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/build_mac_dmg.sh)** | Bash | macOS | Compiles a macOS application bundle and outputs a signed, notarized DMG mount disk image. |
| **[`build_vsix.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/build_vsix.ps1)** / **[`build_vsix.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/build_vsix.sh)** | PowerShell / Bash | Cross-platform | Builds and packages the VS Code extension bundle into a publish-ready `.vsix` file using `vsce`. |
| **[`publish_vsix.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/publish_vsix.ps1)** / **[`publish_vsix.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/publish_vsix.sh)** | PowerShell / Bash | Cross-platform | Packages a platform-targeted `.vsix` with bundled native binaries for a specific runtime identifier (win-x64, linux-x64, osx-x64, osx-arm64). |
| **[`generate-third-party-inventory.js`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/generate-third-party-inventory.js)** | JavaScript | Cross-platform | Scans npm and NuGet locks to build the third-party dependency licensing inventory report. |
| **[`test_sdk_build.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/test_sdk_build.ps1)** / **[`test_sdk_build.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/test_sdk_build.sh)** | PowerShell / Bash | Cross-platform | Verifies compilation of self-contained single-file SDK CLI tools and runs validation smoke tests on them. |
| **[`Set-Version.ps1`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/Set-Version.ps1)** / **[`set-version.sh`](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/set-version.sh)** | PowerShell / Bash | Cross-platform | Updates the version string across all canonical locations (build props, manifests, docs, scripts). Does not modify `CHANGELOG.md`. |

---

## 2. Running Core Workflows

### 2.1 Building for Local Debugging
To compile the .NET engine projects, Vite React UI components, and the VS Code TypeScript extension:

* **Windows (PowerShell):**
  ```powershell
  .\scripts\build-debug.ps1
  ```
* **Linux / macOS (Bash):**
  ```bash
  ./scripts/build-debug.sh
  ```

*To build while bypassing the extension's unit testing phase, pass `--skip-tests` (or `-SkipTests`).*

### 2.2 Executing Smoke Test Lanes
To run targeted smoke tests (Categories: Core Language, Path Security, Reporting, Portal):

* **Windows (PowerShell):**
  ```powershell
  .\scripts\test-smoke.ps1 -Lane all
  ```
* **Linux / macOS (Bash):**
  ```bash
  ./scripts/test-smoke.sh --lane all
  ```

### 2.3 Running General Test Lanes
Runs groups of tests mapped to standard pipeline stages:

* **Windows (PowerShell):**
  ```powershell
  .\scripts\test-lane.ps1 -Lane fast
  ```
* **Linux / macOS (Bash):**
  ```bash
  ./scripts/test-lane.sh --lane fast
  ```

*Supported lanes are: `smoke`, `fast`, `engine`, `portal`, `integration`, `perf`, `release`, `full`, `benchmarks`, `slt`.*

To see what each lane currently contains without running tests:

```powershell
.\scripts\Get-TestLaneInventory.ps1
.\scripts\Get-TestLaneInventory.ps1 -Format Json -OutFile test-inventory.json
```

The inventory is a static visibility report and includes fast-lane exclusion gaps. Use `test-lane.ps1` for authoritative pass/fail execution.

### 2.4 Running Local Pre-Release Validation
Run this before pushing release tags or building release installers. It is designed to catch failures locally before spending GitHub-hosted runner time.

* **Windows (PowerShell):**
  ```powershell
  .\scripts\Test-PreRelease.ps1
  ```
* **Linux / macOS (Bash):**
  ```bash
  ./scripts/test-pre-release.sh
  ```

Useful options:

```powershell
# PowerShell
.\scripts\Test-PreRelease.ps1 -Resume
.\scripts\Test-PreRelease.ps1 -Explain -IncludeSlt
.\scripts\Test-PreRelease.ps1 -Quick -IncludeSlt
.\scripts\Test-PreRelease.ps1 -IncludeSlt
.\scripts\Test-PreRelease.ps1 -IncludeDockerIntegration
.\scripts\Test-PreRelease.ps1 -IncludeStandardScale
.\scripts\Test-PreRelease.ps1 -BuildInstallers -Platforms win-x64
```

```bash
# Bash
./scripts/test-pre-release.sh --resume
./scripts/test-pre-release.sh --explain --include-slt
./scripts/test-pre-release.sh --quick --include-slt
./scripts/test-pre-release.sh --include-slt
./scripts/test-pre-release.sh --include-docker-integration
./scripts/test-pre-release.sh --include-standard-scale
./scripts/test-pre-release.sh --build-installers --platforms linux-x64
```

Reports and logs are written to `release-validation/`. Use `-Resume` / `--resume` after fixing a failed phase; the script only reuses completed phases when the source fingerprint still matches, unless `-ForceResume` / `--force-resume` is supplied.

`-Explain` / `--explain` prints the phase list without running it. `-Quick` / `--quick` skips Node, scale, Docker, and installer phases. `-IncludeSlt` / `--include-slt` adds the SQL Logic Test lane to the local release gate.

The full PowerShell plan with `-IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64` is: asset drift check; restore; NuGet dependency audit; release build; smoke lane; fast lane; sample scripts; SLT lane; VS Code npm install/audit/compile/unit tests; smoke scale certification and baseline check; Docker integration lane; standard scale certification and baseline check; publish artifacts; Windows MSI.

Windows MSI packaging requires WiX Toolset v3.x (`candle.exe` and `light.exe`). On a clean Windows CI runner, install it before `build_msi.ps1`:

```powershell
choco install wixtoolset -y --no-progress --skip-if-installed
```

### 2.5 Running SQLite Logic Tests (SLT) Corpus
Runs the SQLite Logic test suite, generating timestamped output folders with standard teed console logs and TRX test results files:

* **Windows (PowerShell):**
  ```powershell
  .\scripts\Test-SltCorpus.ps1 -CorpusOnly
  ```
* **Linux / macOS (Bash):**
  ```bash
  ./scripts/Test-SltCorpus.sh --corpus-only
  ```

*   **TRX Summarizer:** You can run `.\scripts\Parse-SltResults.ps1` in Windows PowerShell afterward to parse the resulting TRX file and print execution metrics directly to your shell.

### 2.6 Regenerating the Syntax Index Token Inventory
To refresh the generated appendix in `Docs/Syntax_Index.md` after changing `LanguageMetadata.cs`:

* **Cross-platform:**
  ```bash
  node ./scripts/generate-syntax-index.js
  ```

*To verify that the generated appendix matches the source of truth without writing the file, pass `--check`.*

---

## 3. Bumping the Version

To update the version string across all canonical locations before tagging a release:

* **Windows (PowerShell):**
  ```powershell
  .\scripts\Set-Version.ps1 -Version "0.9.0"
  ```
* **Linux / macOS (Bash):**
  ```bash
  ./scripts/set-version.sh 0.9.0
  ```

The scripts update `Directory.Build.props`, the VS Code extension manifest and lock file, `README.md`, all release scripts, user-facing docs, and architecture doc headers. After running, manually add a `## [X.Y.Z]` entry to `CHANGELOG.md`, then commit and tag when ready.

---

## 4. Web Asset Synchronization

To keep report rendering layers identical across host environments (such as the VS Code extension, Portal instance, and standalone Player), the canonical assets are hosted in `src/ETL-SQL.ReportRuntime/Resources/Shared/`. 

Never edit synced host assets directly. Edit the canonical source file first, then synchronize using:
```bash
node ./scripts/sync-assets.js
```
To verify asset synchronization in a CI build environment, run:
```bash
node ./scripts/sync-assets.js -Check
```
