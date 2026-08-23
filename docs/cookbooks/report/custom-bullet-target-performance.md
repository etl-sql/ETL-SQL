# Custom Bullet & Target Performance Cards

**Pattern**: Custom Bullet & Target Performance visual constructed via the native Grammar of Graphics layer engine (`CUSTOM`). Replaces monolithic black-box bullet widgets by composing three explicit geometric marks: background qualitative range bands (`RECT`), a foreground actual metric bar (`RECT`), and a target reference marker (`RULE`).

**Demonstrates**: `CUSTOM`, `COORDINATE (TYPE = TRANSPOSED_CARTESIAN)`, `RECT` layers, `RULE` layers, explicit `SCALES (LINEAR, BAND)`, and presentation `CONDITIONS`.

```sql
SET REPORT TITLE = 'Executive Bullet Performance Dashboard';
SET REPORT DESCRIPTION = 'Demonstration of Stephen Few style bullet charts constructed via Grammar of Graphics.';

-- ── 1. Mock Data Generation (KPI actuals, targets, and qualitative thresholds 0-100%) ──
SELECT 'Revenue' AS Metric, 92 AS Actual, 83 AS Target, 50 AS BadRange, 75 AS SatRange, 100 AS MaxRange INTO #bullet_data
UNION ALL SELECT 'Profit Margin', 80, 92, 50, 75, 100
UNION ALL SELECT 'New Logos', 90, 80, 50, 75, 100
UNION ALL SELECT 'NPS Score', 78, 80, 50, 70, 100;

-- ── 2. Grammar of Graphics Bullet Composition ────────────────────────────────
CREATE VISUAL BulletPerformance AS CUSTOM (
  TITLE = 'Department KPI Performance vs Targets (% Attainment)',
  SOURCE = #bullet_data,
  CHART (
    COORDINATE (TYPE = TRANSPOSED_CARTESIAN),
    SCALES (
      metric_scale = BAND (CHANNEL = X, INCLUDE_ZERO = OFF, ORDER = SOURCE),
      val_scale    = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON, MIN = 0, MAX = 100)
    ),
    LAYERS (
      -- Layer 1: Background full-range band (Light Gray)
      range_max = RECT (
        Z_INDEX = 1,
        ENCODINGS (
          X = Metric (TYPE = NOMINAL, SCALE = metric_scale),
          Y = MaxRange (TYPE = QUANTITATIVE, SCALE = val_scale)
        ),
        STYLE (FILL = '#e0e0e0', OPACITY = 0.6)
      ),
      -- Layer 2: Satisfactory range band (Medium Gray)
      range_sat = RECT (
        Z_INDEX = 2,
        ENCODINGS (
          X = Metric (TYPE = NOMINAL, SCALE = metric_scale),
          Y = SatRange (TYPE = QUANTITATIVE, SCALE = val_scale)
        ),
        STYLE (FILL = '#bdbdbd', OPACITY = 0.8)
      ),
      -- Layer 3: Poor range band (Darker Gray)
      range_bad = RECT (
        Z_INDEX = 3,
        ENCODINGS (
          X = Metric (TYPE = NOMINAL, SCALE = metric_scale),
          Y = BadRange (TYPE = QUANTITATIVE, SCALE = val_scale)
        ),
        STYLE (FILL = '#9e9e9e', OPACITY = 0.9)
      ),
      -- Layer 4: Foreground actual performance bar (Narrower, centered)
      actual_bar = RECT (
        Z_INDEX = 4,
        ENCODINGS (
          X = Metric (TYPE = NOMINAL, SCALE = metric_scale),
          Y = Actual (TYPE = QUANTITATIVE, SCALE = val_scale)
        ),
        CONDITIONS (
          COLOR WHEN Actual >= Target THEN '#2e7d32' ELSE '#c62828'
        )
      ),
      -- Layer 5: Target benchmark line (Stephen Few vertical target tick mark)
      target_rule = RULE (
        Z_INDEX = 5,
        ENCODINGS (
          X = Metric (TYPE = NOMINAL, SCALE = metric_scale),
          Y = Target (TYPE = QUANTITATIVE, SCALE = val_scale)
        ),
        STYLE (COLOR = '#000000', STROKE_WIDTH = 3)
      )
    )
  )
);

-- ── 3. Page Layout ────────────────────────────────────────────────────────────
CREATE PAGE BulletPage AS DASHBOARD (
  STRUCTURE = 'A',
  MAP (
    'A' = BulletPerformance
  )
);

CREATE NAVIGATION MainNav AS TAB (DEFAULT = BulletPage, PAGES (BulletPage));
```
