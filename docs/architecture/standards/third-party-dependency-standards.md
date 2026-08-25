# Third-Party Dependency Standards

This document establishes the official licensing requirements, evaluation criteria, and inventory guidelines for adding or updating third-party libraries (NuGet, npm, or static assets) in the **ETL-SQL** codebase.

---

## 1. FOSS-Only Licensing Policy

ETL-SQL requires a clean, audit-compliant licensing tree.

- **Rule**: All third-party dependencies (direct and transitive) must use free and open-source software (FOSS) licenses approved by the Open Source Initiative (OSI).
- **Approved Licenses**: MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, MPL-2.0, EPL-2.0, and LGPL/GPL-compatible licenses that match our distribution constraints.
- **Strictly Forbidden**: Do not add dependencies that carry proprietary, source-available-only, non-commercial, trial, paid, freemium-gated, or revenue-conditioned licenses.
- **License Preservation**: You must preserve all license headers and copyright banners in bundled JavaScript, CSS, fonts, and images.

---

## 2. NuGet Package Evaluation Checklist

Before running `dotnet add package`, complete this four-step evaluation:

1. **License Check**: Is the package license OSI-approved? If no, **STOP**—the package cannot be added.
2. **Maintenance Status**: Has the project seen a commit or release within the last 12 months? Do not add abandoned packages.
3. **Transitive Dependencies**: Run `dotnet list package --include-transitive` after adding in a local scratch branch. Do any transitive dependencies conflict with existing packages or violate our FOSS policy?
4. **Necessity**: Can the requirement be met in under 50 lines of standard C# or Base Class Library (BCL) code? If yes, write the code inline instead of taking a dependency.

---

## 3. One Library Per Domain

To prevent dependency bloat and binary size inflation, we enforce a strict **one library per domain** policy. Do not add redundant libraries that perform the same role as an already-approved library.

| Domain | Approved Library | Prohibited Libraries |
| :--- | :--- | :--- |
| **JSON Serialization** | `System.Text.Json` (BCL) | `Newtonsoft.Json` (Json.NET) |
| **Logging Runtime** | `Serilog` | `NLog`, `log4net`, raw console loggers |
| **Session Cache** | `Microsoft.Data.Sqlite` | Full EF Core (when used for engine internals) |
| **PGP Cryptography** | `PgpCore` | Custom wrappers around raw BouncyCastle |

---

## 4. Updates Required Upon Adding a Dependency

If a package passes the evaluation, you must update the following inventory files:

- **`Directory.Packages.props`**: Register the version of the package here to maintain centralized package management.
- **`THIRD-PARTY-NOTICES.md`**: Add the formal copyright attribution and license text.
- **`THIRD-PARTY-INVENTORY.md`**: Register the package name, version, license type, and purpose in the dependency matrix.

---

## References

- [Engine Coding Standards](engine-coding-standards.md)
- [Source Boundary Standards](source-boundary-standards.md)
