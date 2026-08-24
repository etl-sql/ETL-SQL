# TOOLTIP

Attaches a detail surface to a visual, page, container, or button. A detail surface is either a transient text tooltip or a persistent, focusable popover that can carry formatted content and whole visuals — including a chart driven by the row the reader activated.

## Syntax

```sql
-- 1. Transient text tooltip
TOOLTIP = '<text>'

-- 2. Inline detail: markdown, visuals, or both
TOOLTIP ('<markdown>')
TOOLTIP (VISUALS (<visual_name>, ...))
TOOLTIP ('<markdown>', VISUALS (<visual_name>, ...))

-- 3. Referenced container
TOOLTIP = <container_name>
```

## The Two Detail Surfaces

Which surface a clause produces is decided by what it carries, not by an extra keyword.

- **Transient tooltip** — `TOOLTIP = '<text>'`, and the inline form when it carries only markdown. Shows on hover and on keyboard focus, disappears when the pointer leaves or focus moves, and is never focusable. Rendered as `role="tooltip"` and referenced by the mark's `aria-describedby`. It must never contain anything interactive.
- **Detail popover** — the referenced-container form, and the inline form when it lists `VISUALS`. A fine pointer gets a hover preview; click, tap, `Enter`, or `Space` pins it. A pinned popover is a labelled dialog that survives pointer leave and closes only on `Escape` (returning focus to the mark), an outside click, re-activating the mark, opening another detail surface, or the visual being refreshed or unmounted.

## Row Context

Opening a popover pushes one value from the activated row into `@hover_value`, which the surface's visuals may read in their `SOURCE`.

- **Explicit mapping required** — the value comes from the owning visual's `X`, `LABEL`, `NAME`, `REGION`, or `Y` mapping, in that order. A visual with a popover but none of those mappings is rejected: there is no fallback to "the first column", because an implicit choice is not a decision about what is safe to disclose.
- **No secret columns** — a column whose name indicates a credential (`PASSWORD`, `SECRET`, `TOKEN`, `API_KEY`, `CLIENT_SECRET`, and similar) is rejected, both as the row context and anywhere inside the surface. Secret values must not reach refresh parameters, the manifest, URLs, accessibility text, snapshots, or exports.

## Limits

Every limit is checked before the report is published, and a breach fails the build rather than rendering a partial surface.

- **`MaxNestingDepth = 3`** — container nesting inside one surface.
- **`MaxVisuals = 8`** — visuals rendered by one surface.
- **`MaxNodes = 32`** — containers plus visuals expanded for one surface.
- **`MaxRefreshQueries = 8`** — sources re-evaluated when the surface opens.
- **`MaxManifestBytes = 262144`** — serialized size of one surface, including the rows its visuals returned.
- **`MaxSurfacesPerReport = 32`** — detail surfaces declared by one report.
- **`MaxTransientTextLength = 1024`** — characters in a transient tooltip.

A detail surface may not open another detail surface, and may not reach itself through a container cycle.

## Behavior Outside the Browser

PDF, print, Markdown, email, terminal, and plain text cannot be hovered, so they describe a surface instead of expanding it, and never imply the interaction is available.

- Transient text is reproduced in place: `Detail: <text>`.
- A popover becomes `Interactive detail available in browser: <visuals>.`
- Offline snapshots replay through the same runtime, so detail behaves exactly as it does online.

## Examples

```sql
-- Transient text on a KPI card
CREATE VISUAL RevenueCard AS CARD (
    SOURCE = (SELECT SUM(Revenue) AS Revenue FROM #sales),
    MAPPINGS (VALUE = Revenue),
    TOOLTIP = 'Total booked revenue for the selected period'
);
```

```sql
-- A chart inside a popover, driven by the activated row
CREATE VISUAL MonthDetail AS BAR (
    SOURCE = (SELECT Region, Revenue FROM #sales WHERE Month = @hover_value),
    TITLE = 'Regional Detail for ' + ISNULL(@hover_value, 'Selected Month'),
    MAPPINGS (X = Region, Y = Revenue)
);

CREATE CONTAINER TooltipBox AS BOX (
    LAYOUT (
        STRUCTURE = 'A',
        MAP ('A' = MonthDetail)
    )
);

CREATE VISUAL BarWithTooltip AS BAR (
    SOURCE = (SELECT Month, SUM(Revenue) AS Revenue FROM #sales GROUP BY Month),
    MAPPINGS (X = Month, Y = Revenue),
    TOOLTIP = TooltipBox
);
```

```sql
-- Inline detail without declaring a container
CREATE VISUAL InlineDetailBar AS BAR (
    SOURCE = (SELECT Month, Revenue FROM #sales),
    MAPPINGS (X = Month, Y = Revenue),
    TOOLTIP ('**Regional breakdown**', VISUALS (MonthDetail))
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
- [CONTAINER](container.md)
- [VISUAL](visual.md)
- [Report Manifest](../report-manifest.md)
