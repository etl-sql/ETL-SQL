# Breaking Changes

This file is the canonical record of every behavioral change that could cause an existing script to produce different results or fail to execute. Maintained by the protocol in [AGENTS.md §14](AGENTS.md).

## Format

```
### vX.Y.Z — Category: Short description
- **What changed**: One sentence describing the old vs. new behavior.
- **Who is affected**: Scripts using [syntax / feature / connector].
- **Migration**: What a script author must change.
```

Categories: `Syntax` | `Semantic` | `TypeSystem` | `Runtime` | `Connector` | `Parser`

---

## v1.0.0 (baseline)

All syntax and behavior documented in:
- [`Docs/Reference/Grammar.md`](Docs/Reference/Grammar.md)
- [`Docs/Reference/Data_Connectors.md`](Docs/Reference/Data_Connectors.md)
- [`Docs/Reference/Standard_Library.md`](Docs/Reference/Standard_Library.md)

as of this version constitutes the **v1.0 baseline**. No migration required from prior versions.

**Connector option baseline:** All connection options use `PASSWORD` (not `PWD`). `CREATE CONNECTION` uses direct parentheses after the type name — `WITH()` is not valid on `CREATE CONNECTION`.

---

<!-- Add new entries above this line, most recent version first. -->
