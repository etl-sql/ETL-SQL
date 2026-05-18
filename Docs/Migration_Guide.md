# ETL-SQL Migration Guide (v0.7.0)

This document describes breaking changes between ETL-SQL releases and provides upgrade instructions for each version transition.

> [!NOTE]
> ETL-SQL v0.7.0 is the current release baseline. This guide documents the breaking syntax and behavior changes needed when upgrading existing scripts.

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

Each section below covers the upgrade rules for the 0.7.0 baseline.

For a full list of what changed in each release, see [CHANGELOG.md](../CHANGELOG.md).

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

*For questions about migrating a specific script pattern, open a [GitHub Discussion](https://github.com/AmericanSuperstar/ETL-SQL/discussions).*
