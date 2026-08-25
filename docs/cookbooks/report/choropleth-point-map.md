# Choropleth Maps with Point Overlays

**Pattern**: Two coordinated named `MAP` visuals over the same staged data — one shading regions by density, one plotting site coordinates as sized bubbles — placed side by side on a page so regional context and facility detail read together.

**Demonstrates**: `MAP` with `MAPPINGS (REGION, VALUE)` for choropleth shading, `MAP` with `MAPPINGS (LATITUDE, LONGITUDE, SIZE, COLOR)` for coordinate points, and page layout that reads the two together.

> These are two independent visuals, not one layered surface. Report-SQL has no geographic composition in the `CHART` grammar, so a choropleth and its point overlay cannot share a single projected canvas. Give each visual the same page and the same staged source and the reader gets the pairing.

```sql
SET REPORT TITLE = 'US Regional Revenue & Fulfillment Hub Logistics';
SET REPORT DESCRIPTION = 'Choropleth state-level sales density combined with fulfillment center hub coordinate overlays.';

-- ── 1. Regional State-Level Sales Density (Choropleth Base) ───────────────────
SELECT 'CA' AS StateCode, 420000 AS Revenue, 'Western' AS Region INTO #state_sales
UNION ALL SELECT 'TX', 380000, 'South'
UNION ALL SELECT 'NY', 310000, 'East'
UNION ALL SELECT 'FL', 270000, 'South'
UNION ALL SELECT 'IL', 210000, 'Midwest'
UNION ALL SELECT 'WA', 190000, 'Western'
UNION ALL SELECT 'CO', 140000, 'Western'
UNION ALL SELECT 'OH', 160000, 'Midwest';

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
    REGION = StateCode,
    VALUE  = Revenue
  ),
  OPTIONS (
    MAP_TYPE = US_STATES,
    COLORS (
      'min' = '#e3f2fd',
      'max' = '#0d47a1'
    )
  )
);

-- ── 4. Coordinate Geo-Point Overlay Visual ────────────────────────────────────
CREATE VISUAL HubLocationsMap AS MAP (
  SOURCE   = #hub_points,
  TITLE    = 'Fulfillment Logistics Hubs (Geo-Coordinates & Throughput)',
  MAPPINGS (
    LATITUDE  = Lat,
    LONGITUDE = Lon,
    LABEL     = HubName,
    SIZE      = Volume
  ),
  OPTIONS (
    MAP_TYPE = US_STATES,
    COLORS (
      'Active'      = '#2e7d32',
      'Maintenance' = '#f57c00'
    )
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
