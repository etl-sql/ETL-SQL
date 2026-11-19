# Choropleth Maps with Point Overlays

**Pattern**: Two coordinated named `MAP` visuals over the same staged data — one shading regions by density, one plotting site coordinates as sized bubbles — placed side by side on a page so regional context and facility detail read together.

**Demonstrates**: `MAP` with `MAPPINGS (REGION, VALUE)` for choropleth shading, `MAP` with `MODE = POINTS` and `MAPPINGS (LAT, LON, VALUE, LABEL)` for sized coordinate points, and page layout that reads the two together.

> This recipe shows the concise named-`MAP` alternative: two independent visuals sharing a page and
> staged source. When regions, routes, points, and labels must share one projected canvas, use
> [`CUSTOM` geographic composition](../../../samples/08_Reporting/custom_geographic_layers.rptsql).

```sql
SET REPORT TITLE = 'US Regional Revenue & Fulfillment Hub Logistics';
SET REPORT DESCRIPTION = 'Choropleth state-level sales density combined with fulfillment center hub coordinate overlays.';

-- ── 1. Regional State-Level Sales Density (Choropleth Base) ───────────────────
-- US_STATES matches full state names (case-insensitive), not postal codes.
SELECT 'California' AS StateName, 420000 AS Revenue, 'Western' AS Region INTO #state_sales
UNION ALL SELECT 'Texas',      380000, 'South'
UNION ALL SELECT 'New York',   310000, 'East'
UNION ALL SELECT 'Florida',    270000, 'South'
UNION ALL SELECT 'Illinois',   210000, 'Midwest'
UNION ALL SELECT 'Washington', 190000, 'Western'
UNION ALL SELECT 'Colorado',   140000, 'Western'
UNION ALL SELECT 'Ohio',       160000, 'Midwest';

-- ── 2. Fulfillment Hub Coordinate Point Overlays ──────────────────────────────
SELECT 'Los Angeles Hub' AS HubName, 34.0522 AS Lat, -118.2437 AS Lon, 85000 AS Volume, 'Active' AS Status INTO #hub_points
UNION ALL SELECT 'Dallas Mega-Center', 32.7767, -96.7970, 112000, 'Active'
UNION ALL SELECT 'New York Metro', 40.7128, -74.0060, 94000, 'Active'
UNION ALL SELECT 'Chicago Central', 41.8781, -87.6298, 76000, 'Active'
UNION ALL SELECT 'Seattle Air Cargo', 47.6062, -122.3321, 52000, 'Maintenance';

-- ── 3. Regional Choropleth Map Visual ─────────────────────────────────────────
CREATE VISUAL StateRevenueMap AS MAP (
  SOURCE   = #state_sales,
  TITLE    = 'State Revenue Performance (Choropleth Density)',
  MAPPINGS (
    REGION = StateName,
    VALUE  = Revenue
  ),
  OPTIONS (
    MAP_NAME   = US_STATES,
    COLOR_LOW  = '#e3f2fd',
    COLOR_HIGH = '#0d47a1'
  )
);

-- ── 4. Coordinate Geo-Point Overlay Visual ────────────────────────────────────
CREATE VISUAL HubLocationsMap AS MAP (
  SOURCE   = #hub_points,
  TITLE    = 'Fulfillment Logistics Hubs (Geo-Coordinates & Throughput)',
  MAPPINGS (
    LAT   = Lat,
    LON   = Lon,
    LABEL = HubName,
    VALUE = Volume        -- dot size
  ),
  OPTIONS (
    MAP_NAME = US_STATES, -- base map for context
    MODE     = POINTS
  )
);

-- ── 5. Page Layout ────────────────────────────────────────────────────────────
CREATE PAGE GeographicLogisticsPage AS DASHBOARD (
  STRUCTURE = 'A B',
  MAP (
    'A' = StateRevenueMap,
    'B' = HubLocationsMap
  )
);

CREATE NAVIGATION MainNav AS TAB (DEFAULT = GeographicLogisticsPage, PAGES (GeographicLogisticsPage));
```
