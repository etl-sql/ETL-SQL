# ETL-SQL Migration Guide

This document describes breaking changes between ETL-SQL releases and provides upgrade instructions for each version transition.

> [!NOTE]
> ETL-SQL is currently pre-1.0 (v0.5.0). No breaking migration paths have been established yet. This document will be populated with each release that introduces breaking syntax or behavioral changes. Check back here before upgrading.

---

## How to Use This Guide

Each section below covers one version-to-version upgrade. Read **only the section(s) matching your upgrade path**. If you are upgrading across multiple versions (e.g. 0.3.0 → 0.5.0), apply each section in order.

For a full list of what changed in each release, see [CHANGELOG.md](../CHANGELOG.md).

---

## Upgrading to v0.5.0

*No breaking changes.* v0.5.0 added new features (reporting layer, REST connector, `ALTER CONNECTION`) without removing or changing existing syntax.

**Deprecations introduced in v0.5.0** (not yet removed — these will become errors in a future release):

| Deprecated | Preferred replacement | Reason |
| :--- | :--- | :--- |
| `Logger.Instance` (C# engine code only) | Injected `ILogger` from `IExecutionContext` | Static façade blocks testability |
| `SEND_EMAIL(...)` function style as primary form | `SEND EMAIL ... AT conn` SQL style | SQL style is more readable and consistent with other verb commands |
| `BULK INSERT WITH(HEADER=ON)` | `BULK INSERT WITH(FIRSTROW=2)` | `HEADER` was never a valid BULK INSERT option; silently ignored |

**Recommended script scan before upgrading:**
```bash
# Lint all scripts in your scripts directory to find deprecation warnings
ETL-SQL.exe --lint C:\Scripts\
```

---

## Upgrading to v0.4.0

*No breaking changes.* v0.4.0 introduced the TUI IDE and VS Code extension. No script syntax was affected.

---

## Template: Future Breaking Change Entry

When a breaking change is introduced, use this format:

```markdown
## Upgrading from vX.Y.Z to vA.B.C

### Breaking: [Short description]

**What changed:**
[Explain what was removed or changed, and why.]

**Before (vX.Y.Z):**
\```sql
-- old syntax
\```

**After (vA.B.C):**
\```sql
-- new syntax
\```

**Find-and-replace pattern:**
- Search: `old pattern`
- Replace: `new pattern`
- Scope: `.etlsql` and `.rptsql` files

**Is automated migration possible?**
[Yes — use `ETL-SQL.exe --migrate` / No — manual review required]
```

---

## Planned Future Breaking Changes

The following changes are under consideration for upcoming versions. They are not yet scheduled, but listed here so you can write forward-compatible scripts now.

| Change | Target version | Mitigation today |
| :--- | :--- | :--- |
| `THROW` will require message argument — bare `THROW;` (re-throw) may change to `RETHROW;` | TBD | Always include a message: `THROW ERROR_MESSAGE();` |
| `WAITFOR (SELECT ...)` — if implemented as `WAIT UNTIL (condition)` instead of overloading `WAITFOR` | TBD | Use `WHILE` + `WAITFOR DELAY` workaround (already the documented approach) |
| PBKDF2 iteration count increase — existing `ENC:` strings will need re-encryption | TBD | No action needed yet; migration tooling will be provided |

---

*For questions about migrating a specific script pattern, open a [GitHub Discussion](https://github.com/AmericanSuperstar/ETL-SQL/discussions).*
