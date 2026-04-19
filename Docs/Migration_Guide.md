# ETL-SQL Migration Guide

This document describes breaking changes between ETL-SQL releases and provides upgrade instructions for each version transition.

> [!NOTE]
> ETL-SQL is currently pre-1.0 (v0.5.0). No breaking migration paths have been established yet. This document will be populated with each release that introduces breaking syntax or behavioral changes. Check back here before upgrading.

---

## How to Use This Guide

Each section below covers one version-to-version upgrade. Read **only the section(s) matching your upgrade path**. If you are upgrading across multiple versions (e.g. 0.3.0 → 0.5.0), apply each section in order.

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

## Planned Future Breaking Changes

The following changes are under consideration for upcoming versions. They are not yet scheduled, but listed here so you can write forward-compatible scripts now.


---

*For questions about migrating a specific script pattern, open a [GitHub Discussion](https://github.com/AmericanSuperstar/ETL-SQL/discussions).*
