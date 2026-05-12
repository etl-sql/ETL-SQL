# ETL-SQL Migration Guide (v0.7.0)

This document describes breaking changes between ETL-SQL releases and provides upgrade instructions for each version transition.

> [!NOTE]
> ETL-SQL is currently pre-1.0 (v0.7.0). No major breaking migration paths have been established yet. This document will be populated with each release that introduces breaking syntax or behavioral changes. Check back here before upgrading.

---

## How to Use This Guide

---

## v0.7.0 (May 2026)

### Breaking Changes

#### 1. Visibility Property Migration
The legacy `HIDDEN = ON | OFF` syntax has been deprecated and removed. All report objects (Pages, Containers, Visuals) now use the `VISIBLE` property.

- **Old Syntax**: `CREATE VISUAL myChart AS ... WITH(HIDDEN = ON);`
- **New Syntax**: `CREATE VISUAL myChart AS ... WITH(VISIBLE = OFF);`

**Upgrade Path**: Run a search-and-replace on your `.rptsql` files:
- Replace `HIDDEN = ON` with `VISIBLE = OFF`
- Replace `HIDDEN = OFF` with `VISIBLE = ON`

#### 2. Portal JWT Secret Enforcement
The Report Portal now requires a minimum 32-character secret for `Portal:Jwt:Secret`. The portal will refuse to start if the secret is missing or too short.

**Upgrade Path**: Generate a new secret using the engine:
```sql
GENERATE JWT_SECRET;
```
Then update your `appsettings.json` or environment variables.

### New Capabilities
- **`GO` Batch Separator**: Support for multi-batch script execution.
- **`QUALIFY` Clause**: Filter results based on window function outputs without a CTE.
- **Interactive Reporting**: Collapsible containers, cross-visual actions, and shared datasets are now production-ready.

Each section below covers one version-to-version upgrade. Read **only the section(s) matching your upgrade path**. If you are upgrading across multiple versions (e.g. 0.3.0 → 0.7.0), apply each section in order.

For a full list of what changed in each release, see [CHANGELOG.md](../CHANGELOG.md).

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

## Upgrading to v0.5.0

### Breaking: `BULK INSERT HEADER=ON` removed — use `FIRSTROW=2`

**What changed:** The `HEADER=ON` option was removed from `BULK INSERT`. It was silently ignored in most cases and created confusion with the `FLATFILE` connection option of the same name. Use `FIRSTROW=2` to skip a CSV header row.

**Before (pre-v0.5.0):**
```sql
BULK INSERT #dest FROM 'C:\Data\file.csv' WITH (FORMAT='CSV', HEADER=ON);
```

**After (v0.5.0+):**
```sql
BULK INSERT #dest FROM 'C:\Data\file.csv' WITH (FORMAT='CSV', FIRSTROW=2);
```

**Find-and-replace pattern:**
- Search: `HEADER=ON` (inside `BULK INSERT ... WITH(...)` blocks only)
- Replace: `FIRSTROW=2`
- Scope: `.etlsql` files

**Is automated migration possible?** Yes — simple find-and-replace within `BULK INSERT` blocks.

---

### Breaking: Docker syntax updated — `USE DOCKER` replaced by verb-based commands

**What changed:** The `USE DOCKER alias ACTION` syntax was replaced by dedicated verb-based statements for clarity and consistency with the `VERB NOUN` convention.

**Before (pre-v0.5.0):**
```sql
USE DOCKER mssql_db START;
USE DOCKER mssql_db STOP;
USE DOCKER mssql_db CLOSE;
```

**After (v0.5.0+):**
```sql
START DOCKER mssql_db;
STOP DOCKER mssql_db;
CLOSE DOCKER mssql_db;
```

**Find-and-replace pattern:**
- `USE DOCKER {name} START;` → `START DOCKER {name};`
- `USE DOCKER {name} STOP;` → `STOP DOCKER {name};`
- `USE DOCKER {name} PAUSE;` → `PAUSE DOCKER {name};`
- `USE DOCKER {name} CLOSE;` → `CLOSE DOCKER {name};`
- Scope: `.etlsql` files

**Is automated migration possible?** Yes — regex replace.

> [!NOTE]
> The legacy `USE DOCKER` syntax is still accepted by the parser for backward compatibility. Migration is recommended but not required at this time.

---

## Upgrading to v0.7.0

### Deprecated: `HIDDEN = ON/OFF` property
**What changed:** The `HIDDEN` property for Pages, Containers, and Visuals has been deprecated and replaced by the standardized `VISIBLE = ON/OFF` property.

**Before (pre-v0.7.0):**
```sql
CREATE PAGE MyPage WITH (HIDDEN = ON);
```

**After (v0.7.0+):**
```sql
CREATE PAGE MyPage WITH (VISIBLE = OFF);
```

**Find-and-replace pattern:**
- Search: `HIDDEN = ON` -> `VISIBLE = OFF`
- Search: `HIDDEN = OFF` -> `VISIBLE = ON`

---

### Improved: `FOR` loop implicit start
**What changed:** `FOR` loops now support an implicit start value of `1`.

**Before (pre-v0.7.0):**
```sql
FOR @i = 1 TO 10 ...
```

**After (v0.7.0+):**
```sql
FOR @i = 10 ... -- assumes 1 TO 10
```

---

## Planned Future Breaking Changes

The following changes are under consideration for upcoming versions. They are not yet scheduled, but listed here so you can write forward-compatible scripts now.


---

*For questions about migrating a specific script pattern, open a [GitHub Discussion](https://github.com/AmericanSuperstar/ETL-SQL/discussions).*
