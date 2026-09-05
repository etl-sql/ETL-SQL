# GANTT

A project timeline chart where each row is a task bar spanning a START and END date. Supports progress fill, milestone diamonds, predecessor dependency arrows, today indicator lines, label placement, and swimlane grouping.

## Syntax

```sql
CREATE VISUAL VisualName AS GANTT (
  SOURCE = #tableName,
  MAPPINGS (
    Y = col_task,
    START = col_start,
    END = col_end,
    PROGRESS = col_progress,
    MILESTONE = col_milestone,
    DEPENDS_ON = col_predecessor,
    GROUP = col_phase,
    COLOR = col_color
  ),
  OPTIONS (
    TITLE = 'Project Timeline',
    TODAY_LINE = ON,
    TODAY_COLOR = '#ef4444',
    TODAY_DATE = '2026-03-15',
    LABEL_POSITION = LEFT
  )
);
```

## Mappings

- **Y** — Task label (required). Alias: **LABEL**.
- **START** — Start date or datetime string (required). Alias: **X**.
- **END** — End date or datetime string (required). Alias: **X2**. Rows where `START = END` automatically render as a diamond milestone marker.
- **PROGRESS** — Optional task completion percentage (0–100), rendered as an inner progress bar overlay.
- **MILESTONE** — Optional boolean or flag column (`1`, `true`, `yes`, `on`) explicitly marking the row as a milestone diamond marker.
- **DEPENDS_ON** — Optional predecessor task label to draw elbow dependency arrows with arrowheads between tasks.
- **GROUP** — Optional column for row grouping / swim lanes. Renders a labeled section header row above tasks belonging to the group.
- **COLOR** — Optional per-task bar fill color as a hex string (`#rrggbb`).

## Options

- **TITLE = 'text'** — Visual title text displayed in the header.
- **TODAY_LINE = ON|OFF** — Toggle vertical reference line marking today or a target date (default OFF).
- **TODAY_COLOR = '#rrggbb'** — Color of the vertical today line and indicator label (default `#ef4444`).
- **TODAY_DATE = 'YYYY-MM-DD'** — Explicit date override for the today line (defaults to current date if omitted).
- **LABEL_POSITION = LEFT|INSIDE|RIGHT|NONE** — Placement of the task label text (default LEFT). Set to `INSIDE` to draw the label inside the bar, `RIGHT` to place it after the bar end, or `NONE` to omit labels.
- **COLOR:PRIMARY = '#rrggbb'** — Default bar color when no COLOR mapping is supplied (default `#5470c6`).

## Examples

### Example 1: Project Plan with Progress, Milestones, and Dependencies

```sql
SELECT 'Design'      AS Task, '2026-01-01' AS StartDate, '2026-01-10' AS EndDate, 100 AS Pct, 0 AS IsMile, '' AS Pred INTO #project UNION ALL
SELECT 'Dev Core',   '2026-01-11',         '2026-01-25',                   60,        0,          'Design'            UNION ALL
SELECT 'Dev UI',     '2026-01-18',         '2026-02-05',                   30,        0,          'Dev Core'          UNION ALL
SELECT 'Beta Gate',  '2026-02-05',         '2026-02-05',                    0,        1,          'Dev UI'            UNION ALL
SELECT 'Deployment', '2026-02-06',         '2026-02-12',                    0,        0,          'Beta Gate';

CREATE VISUAL ProjectRoadmap AS GANTT (
  SOURCE   = #project,
  MAPPINGS (
    Y          = Task,
    START      = StartDate,
    END        = EndDate,
    PROGRESS   = Pct,
    MILESTONE  = IsMile,
    DEPENDS_ON = Pred
  ),
  OPTIONS (
    TITLE          = 'Product Delivery Schedule',
    TODAY_LINE     = ON,
    TODAY_COLOR    = '#e11d48',
    LABEL_POSITION = LEFT
  )
);
```

### Example 2: Grouped Swim Lanes with Section Headers

```sql
SELECT 'Planning'    AS Phase, 'Scope'    AS Task, '2026-03-01' AS StartDate, '2026-03-07' AS EndDate INTO #plan UNION ALL
SELECT 'Planning',             'Approval',         '2026-03-08',               '2026-03-08'             UNION ALL
SELECT 'Execution',            'Sprint 1',         '2026-03-09',               '2026-03-22'             UNION ALL
SELECT 'Execution',            'Sprint 2',         '2026-03-23',               '2026-04-05';

CREATE VISUAL PhaseGantt AS GANTT (
  SOURCE   = #plan,
  MAPPINGS (
    GROUP = Phase,
    Y     = Task,
    START = StartDate,
    END   = EndDate
  ),
  OPTIONS (
    TITLE          = 'Phased Execution Plan',
    LABEL_POSITION = INSIDE
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
