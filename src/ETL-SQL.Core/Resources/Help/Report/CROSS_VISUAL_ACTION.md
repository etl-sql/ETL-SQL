# CROSS_VISUAL_ACTION
Defines how a visual responds when a user interacts with *other* visuals on the same page.

Syntax:
```sql
OPTIONS (
    CROSS_VISUAL_ACTION = 'FILTER' | 'HIGHLIGHT' | 'NONE'
)
```

## Modes

### FILTER
The target visual is re-queried, and any data not matching the selection **disappears**. This is the default for `TABLE` and `SLICER` visuals.

### HIGHLIGHT
The target visual maintains its full original shape, but the matching subset is **highlighted** (solid color) while the rest is **dimmed** (ghosted). This is the default for chart types like `BAR`, `LINE`, and `PIE`.

### NONE
The target visual ignores interactions from other visuals.

## Implementation Details
- **Path B Logic**: The engine calculates the "Total" vs "Selection" data on the server to ensure high performance even with large datasets.
- **Power BI Style**: This behavior mimics standard BI platforms, where slicers define the "universe" and chart clicks define the "selection."

## Example
```sql
CREATE VISUAL CategoryBreakdown AS BAR (
    SOURCE = #sales,
    MAPPINGS (X = Category, Y = Revenue),
    OPTIONS (
        CROSS_VISUAL_ACTION = 'HIGHLIGHT'
    )
);
```
