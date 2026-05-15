Type: GANTT
A project timeline chart where each row is a task bar spanning a START and END date. Tasks are displayed top-to-bottom in source order. Ideal for project plans, sprint schedules, and phase timelines.

Mappings:
  Y     — task label; alias LABEL accepted (required)
  START — start date or datetime; alias X accepted (required)
  END   — end date or datetime; alias X2 accepted (required)
  COLOR — per-task bar color as a hex string (optional; all bars share one color if omitted)

Options:
  TITLE         = 'text'
  COLOR:PRIMARY = '#5470c6'   -- default bar color when no COLOR mapping is supplied

Note: START and END values are parsed as time values; 'YYYY-MM-DD' string format is recommended for date-only tasks. Each source row produces one bar. Tasks with the same Y label are grouped on one Y-axis slot — use distinct labels per row for a traditional Gantt.

```sql
-- Project milestone Gantt chart
SELECT 'Planning'      AS TaskName, '2026-01-01' AS StartDate, '2026-02-15' AS EndDate, '#5470c6' AS BarColor INTO #milestones
UNION ALL SELECT 'Development',     '2026-02-01', '2026-04-30', '#91cc75'
UNION ALL SELECT 'Testing',         '2026-04-01', '2026-05-31', '#fac858'
UNION ALL SELECT 'Deploy',          '2026-06-01', '2026-06-30', '#ee6666';

CREATE VISUAL ProjectTimeline AS GANTT (
  SOURCE   = #milestones,
  TITLE    = 'Project Timeline',
  MAPPINGS (
    Y     = TaskName,
    START = StartDate,
    END   = EndDate,
    COLOR = BarColor
  )
);
```
