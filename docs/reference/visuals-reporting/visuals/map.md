# MAP

A geographic chart that plots data onto a map. Two modes are available: CHOROPLETH (default) colours regions by a numeric value, and POINTS plots sized dots at latitude/longitude coordinates.

## Syntax

```sql
CREATE VISUAL VisualName AS MAP (
  SOURCE = #tableName,
  MAPPINGS (
    ...
  )
);
```

## Mappings

Mappings (CHOROPLETH mode, default):
- **REGION** - column containing region names or FIPS codes (required)
- **VALUE** - numeric column controlling fill colour (optional)

Mappings (POINTS mode; set MODE = POINTS):
- **LON** - longitude column (required)
- **LAT** - latitude column (required)
- **VALUE** - numeric column controlling dot size (optional)
- **LABEL** - column shown in the tooltip

## Options

Options (all modes):
- **MAP_NAME = 'key'** - built-in map to use (see table below)
- **MAP_FILE = 'path'** - path to a custom GeoJSON file (alternative to MAP_NAME)
  MODE     = CHOROPLETH | POINTS   (default: CHOROPLETH)
  TITLE    = 'text'

Options (CHOROPLETH only):
- **COLOR_LOW = '#hex'** - fill colour for the lowest value (default: #e0f3f8)
- **COLOR_HIGH = '#hex'** - fill colour for the highest value (default: #08306b)
- **SHOW_LABELS = ON | OFF** - render region name labels on the map (default: OFF)
- **MATCH_BY = NAME | FIPS** - how REGION values are matched to map features (default: NAME)

Built-in map keys (MAP_NAME):
- **WORLD** - 177 countries (Natural Earth 110m)
- **US_STATES** - 50 states + DC
- **US_COUNTIES** - 3,221 US counties (Census 20m simplified)
- **MN_COUNTIES** - 87 Minnesota counties
- **CANADA_PROVINCES** - 13 provinces and territories
- **EUROPE** - 39 European countries

Matching notes:
  - By default regions match against the feature's 'name' property (e.g. "Minnesota", "Autauga").
  - Set MATCH_BY = FIPS and supply 5-digit FIPS codes (e.g. "27001") to match US counties
    by FIPS instead of name.
  - City names and zip codes are not supported by the built-in maps. For zip-code choropleth,
    download the Census ZCTA GeoJSON (see Report_Cookbook.md Recipe 11) and supply it via MAP_FILE.

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
