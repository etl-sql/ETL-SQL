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

## Planned Future Breaking Changes

The following changes are under consideration for upcoming versions. They are not yet scheduled, but listed here so you can write forward-compatible scripts now.


---

*For questions about migrating a specific script pattern, open a [GitHub Discussion](https://github.com/AmericanSuperstar/ETL-SQL/discussions).*
