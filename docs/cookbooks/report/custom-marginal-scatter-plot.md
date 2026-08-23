# Marginal Histogram & Scatter Plot Compositions

**Pattern**: High-density 2D distribution analysis combining a primary scatter plot with marginal reference thresholds and quadrant lines using Grammar of Graphics (`CUSTOM`). Highlights multivariate distributions, benchmark thresholds, and outlier quadrants without third-party dependencies.

**Demonstrates**: `CUSTOM`, `COORDINATE (TYPE = CARTESIAN)`, `POINT` marks, horizontal & vertical `RULE` benchmarks, `CONDITIONS`, and multiple layers.

```sql
SET REPORT TITLE = 'Customer Lifetime Value vs Acquisition Cost Analysis';
SET REPORT DESCRIPTION = 'Scatter plot distribution with marginal quadrant benchmarks and outlier encoding.';

-- ── 1. Mock Data Generation (Customer CAC vs LTV with Risk Segmentation) ─────
SELECT 'Acme Corp' AS Account, 1200 AS CAC, 8500 AS LTV, 1500 AS CAC_Benchmark, 5000 AS LTV_Benchmark, 'Enterprise' AS Tier, 0 AS IsChurnRisk INTO #customer_scatter
UNION ALL SELECT 'Beta LLC', 800, 3200, 1500, 5000, 'Mid-Market', 0
UNION ALL SELECT 'Gamma Inc', 2400, 4100, 1500, 5000, 'Enterprise', 1
UNION ALL SELECT 'Delta Co', 450, 1900, 1500, 5000, 'SMB', 0
UNION ALL SELECT 'Epsilon Ltd', 1600, 9200, 1500, 5000, 'Enterprise', 0
UNION ALL SELECT 'Zeta Apps', 3100, 2800, 1500, 5000, 'Mid-Market', 1
UNION ALL SELECT 'Eta Tech', 650, 5400, 1500, 5000, 'Mid-Market', 0
UNION ALL SELECT 'Theta Bio', 2100, 11500, 1500, 5000, 'Enterprise', 0
UNION ALL SELECT 'Iota Retail', 350, 950, 1500, 5000, 'SMB', 1;

-- ── 2. Grammar of Graphics Scatter & Marginal Benchmark Composition ──────────
CREATE VISUAL CustomerDistribution AS CUSTOM (
  TITLE = 'Customer Acquisition Cost vs Lifetime Value Quadrants',
  SOURCE = #customer_scatter,
  CHART (
    COORDINATE (TYPE = CARTESIAN),
    SCALES (
      cac_scale = LINEAR (CHANNEL = X, INCLUDE_ZERO = ON, MIN = 0),
      ltv_scale = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON, MIN = 0)
    ),
    LAYERS (
      -- Layer 1: Marginal Benchmark - Vertical CAC Ceiling Threshold ($1,500)
      cac_threshold = RULE (
        Z_INDEX = 1,
        ENCODINGS (
          X = CAC_Benchmark (TYPE = QUANTITATIVE, SCALE = cac_scale)
        ),
        STYLE (COLOR = '#e57373', STROKE_WIDTH = 2, STROKE_DASH = '4,4')
      ),
      -- Layer 2: Marginal Benchmark - Horizontal LTV Target Floor ($5,000)
      ltv_threshold = RULE (
        Z_INDEX = 2,
        ENCODINGS (
          Y = LTV_Benchmark (TYPE = QUANTITATIVE, SCALE = ltv_scale)
        ),
        STYLE (COLOR = '#81c784', STROKE_WIDTH = 2, STROKE_DASH = '4,4')
      ),
      -- Layer 3: Customer Data Points with Conditional Color by Churn Risk
      points = POINT (
        Z_INDEX = 3,
        ENCODINGS (
          X = CAC (TYPE = QUANTITATIVE, SCALE = cac_scale),
          Y = LTV (TYPE = QUANTITATIVE, SCALE = ltv_scale)
        ),
        CONDITIONS (
          COLOR WHEN IsChurnRisk = 1 THEN '#d32f2f' ELSE '#1976d2',
          SIZE WHEN Tier = 'Enterprise' THEN 14 ELSE 8
        )
      )
    )
  )
);

-- ── 3. Page Layout ────────────────────────────────────────────────────────────
CREATE PAGE MarginalScatterPage AS DASHBOARD (
  STRUCTURE = 'A',
  MAP (
    'A' = CustomerDistribution
  )
);

CREATE NAVIGATION MainNav AS TAB (DEFAULT = MarginalScatterPage, PAGES (MarginalScatterPage));
```
