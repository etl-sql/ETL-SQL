# MAP

A geographic chart that plots data onto a map. Two modes are available: CHOROPLETH (default) colours regions by a numeric value, and POINTS plots sized dots at latitude/longitude coordinates.

## Syntax

```sql
CREATE VISUAL VisualName AS MAP (
  SOURCE = #tableName,
  MAPPINGS (
    ...
  ),
  OPTIONS (
    [BASE_MAP = 'https://tile.provider.org/{z}/{x}/{y}.png'],
    ...
  )
);
```

## Mappings

Mappings (CHOROPLETH mode, default):
- **REGION** — Column containing region names or FIPS codes (required)
- **VALUE** — Numeric column controlling fill color (optional)
- **TOOLTIP** — Column shown in hover tooltip for each region (optional)

Mappings (POINTS mode; set MODE = POINTS):
- **LON** — Longitude column (required)
- **LAT** — Latitude column (required)
- **VALUE** — Numeric column controlling dot size (optional)
- **COLOR** — Color column or series hex/category for point fills (optional)
- **LABEL** — Label column shown in tooltip (optional)
- **TOOLTIP** — Custom tooltip text expression or column (optional)

## Options

Options (all modes):
- **BASE_MAP = 'provider-url-template'** — Tile server URL template (e.g. `'https://tile.openstreetmap.org/{z}/{x}/{y}.png'`). Must use HTTP/HTTPS, contain `{z}`, `{x}`, and `{y}` placeholders, and satisfy connector/host security allowlisting.
- **MAP_NAME = 'key'** — Built-in map to use (see table below)
- **MAP_FILE = 'path'** — Path to custom GeoJSON file (alternative to MAP_NAME)
- **MODE = CHOROPLETH|POINTS** — Map rendering mode (default: CHOROPLETH)
- **ZOOM = n** — Initial map zoom magnification (e.g., `ZOOM = 2`)
- **CENTER = (lat, lon)** — Initial center coordinate tuple (e.g., `CENTER = (40.7128, -74.0060)`)
- **TITLE = 'text'** — Visual title

Options (CHOROPLETH only):
- **COLOR_SCALE = LINEAR|QUANTILE|QUANTIZE|THRESHOLD** — Scale binning mode (default: LINEAR)
- **NULL_COLOR = '#hex'** — Fill color for regions absent from data (e.g., `'#f3f4f6'`)
- **COLOR_LOW = '#hex'** — Fill color for lowest value (default: #e0f3f8)
- **COLOR_HIGH = '#hex'** — Fill color for highest value (default: #08306b)
- **SHOW_LABELS = ON|OFF** — Render region name labels on map (default: OFF)
- **MATCH_BY = NAME|FIPS** — How REGION values are matched to map features (default: NAME)

Built-in map keys (MAP_NAME):
- **WORLD** — 177 countries (Natural Earth 110m)
- **US_STATES** — 50 states + DC
- **US_COUNTIES** — 3,221 US counties (Census 20m simplified)
- **MN_COUNTIES** — 87 Minnesota counties
- **CANADA_PROVINCES** — 13 provinces and territories
- **EUROPE** — 39 European countries

Matching notes:
- By default regions match against the feature's 'name' property (e.g. "United States of America", "Minnesota").
- Set MATCH_BY = FIPS and supply 5-digit FIPS codes (e.g. "27001") to match US counties by FIPS instead of name.
- City names and zip codes are not supported by the built-in maps. For zip-code choropleth, supply custom GeoJSON via MAP_FILE.

## Examples

```sql
-- World choropleth: colour countries by sales total
SELECT country, SUM(revenue) AS total_rev
  INTO #world_sales
  FROM dbo.Sales
  GROUP BY country;

CREATE VISUAL GlobalSalesMap AS MAP (
  SOURCE   = #world_sales,
  MAPPINGS (REGION = country, VALUE = total_rev),
  OPTIONS  (MAP_NAME = 'WORLD', TITLE = 'Global Sales by Country')
);

-- US stores point map with sized bubbles
SELECT store_name, lat, lon, sales_volume
  INTO #store_locations
  FROM dbo.Stores;

CREATE VISUAL StoreLocationsMap AS MAP (
  SOURCE   = #store_locations,
  MAPPINGS (LAT = lat, LON = lon, VALUE = sales_volume, LABEL = store_name),
  OPTIONS  (
    MAP_NAME = 'US_STATES',
    MODE     = POINTS,
    TITLE    = 'Store Locations & Volume'
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
- [Visuals Reference](../README.md)
