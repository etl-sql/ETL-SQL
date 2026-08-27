# Report Runtime Contract

The report canvas is shared infrastructure. ReportPlayer, Portal, and the VS Code preview may provide different host chrome, authentication, routing, and surrounding controls, but the report canvas must render from the same manifest contract and the same runtime assets.

## Canonical Assets

The canonical browser runtime files live in `src/ETL-SQL.ReportRuntime/Resources/Shared/`:

- `report-runtime.js`
- `report-runtime.css`
- bundled shared browser dependencies used by the runtime
- map fixtures and other browser runtime data under `maps/`

Host copies are generated artifacts:

- ReportPlayer: `src/ETL-SQL.ReportPlayer/wwwroot/`
- Portal: `src/ETL-SQL.Portal/wwwroot/js/` and `src/ETL-SQL.Portal/wwwroot/css/`
- VS Code: `src/etl-sql-vscode/media/`

Edit the canonical files first, then run:

```powershell
node .\scripts\sync-assets.js
```

CI runs:

```powershell
node .\scripts\sync-assets.js -Check
```

That check fails when any host copy drifts from the canonical shared asset.

## Canvas Contract

All hosts render the report canvas from a fully resolved `ReportManifest`.

- C# evaluates ETL-SQL expressions, report queries, conditional formatting, parameters, styles, themes, and action metadata before serialization.
- JavaScript renders resolved manifest state. It should not evaluate ETL-SQL expressions or infer server semantics from raw script text.
- Runtime behavior for visuals, pages, containers, themes, scalar inputs, slicers, deferred `RUN`, cross highlighting, and table formatting must be identical across hosts.
- Host-specific code may handle shell navigation, authentication, persistence, API routing, and embedding, but not fork report semantics.

## Style Cascade

The effective manifest should reflect this order, with later layers winning:

1. Built-in native runtime and theme-token defaults.
2. Report-level theme and metadata.
3. Page-level style.
4. Container-level style.
5. Visual/button named style from `CREATE STYLE`.
6. Visual/button inline `STYLE (...)`.
7. Runtime interaction state such as selection, highlighting, pending parameters, and hover.

Named styles are resolved by C# from `IExecutionContext.ReportContext.StyleDefinitions`. Host runtimes receive the resolved style dictionary on each manifest object.

## Design Tokens and Color Palettes

All standard report visuals and authored HTML visuals consume scoped `--etl-*` CSS variables projected at report, page, container, and visual levels:

| Token | Description | Fallback / Derivation |
| :--- | :--- | :--- |
| `--etl-surface-card` | Primary card / component background fill | `#ffffff` (light) / `#252526` (dark) |
| `--etl-surface` | Secondary / general surface fill | `#ffffff` (light) / `#252526` (dark) |
| `--etl-bg` | Page and report canvas background | `#f5f5f5` (light) / `#1e1e1e` (dark) |
| `--etl-text-primary` | Primary readable text color | `#0f172a` (light) / `#f8fafc` (dark) |
| `--etl-text-muted` | Subtitles, captions, and muted text | `#64748b` (light) / `#94a3b8` (dark) |
| `--etl-border` | Component border color | `#e2e8f0` (light) / `#334155` (dark) |
| `--etl-shadow` | Card drop shadow / elevation | `0 1px 3px rgba(0,0,0,0.1)` / `0 4px 6px -1px rgba(0,0,0,0.3)` |
| `--etl-accent` | Brand and interactive accent color | `#2563eb` (light) / `#3b82f6` (dark) |
| `--etl-success` | Success / healthy status color | `#16a34a` (light) / `#22c55e` (dark) |
| `--etl-warning` | Warning / attention status color | `#eab308` (light) / `#f59e0b` (dark) |
| `--etl-danger` | Danger / critical error color | `#dc2626` (light) / `#ef4444` (dark) |
| `--etl-info` | Informational status color | `#0284c7` (light) / `#38bdf8` (dark) |
| `--etl-color-1`, `--etl-color-2`, ... | Categorical palette mark color sequence | Derived from `PALETTE` |
| `--etl-palette-1`, `--etl-palette-2`, ... | Explicit palette color aliases | Derived from `PALETTE` |
| `--etl-series-<name>` | Specific series color override | Derived from `COLOR:<series>` |
| `--etl-radius-sm` | Small border radius (tags, badges) | `4px` |
| `--etl-radius-md` | Standard border radius (cards, inputs) | `8px` |
| `--etl-radius-lg` | Large border radius (modals, dialogs) | `12px` |
| `--etl-font-family` | Primary typography font stack | `-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif` |
| `--etl-font-mono` | Monospace typography font stack | `ui-monospace, SFMono-Regular, Menlo, Consolas, monospace` |

- **Token Allowlist**: Only `--etl-*` public tokens are permitted. Host-private custom properties such as `--portal-*` are strictly excluded from the report contract.
- **Safe CSS Serialization**: Token values containing semicolons (`;`), braces (`{}`), backslashes (`\`), control characters, `url(...)`, `expression(...)`, `@import`, or non-`--etl-*` variables are rejected.

## References
- [User Manual](../../guides/onboarding/getting-started.md)


### Palette & Series Contract Extensions
- **Palette Contrast Threshold**: Minimum 3.0:1 contrast ratio against the effective inherited background. Alpha-channel colors (`#RGBA`, `#RRGGBBAA`) are composited before calculation.
- **Stable Series Assignment**: Categorical series identities sorted alphabetically (case-insensitive) to assign deterministic palette indexes.
- **Series Tokens**: All resolved series map to `--etl-series-<sanitized>` with collision suffixes `-2`, `-3`.
- **Lifecycle Cleanliness**: Stale dynamic tokens (`--etl-color-*`, `--etl-palette-*`, `--etl-series-*`) are removed on scope/theme changes, and restored cleanly after visual maximize/restore.
