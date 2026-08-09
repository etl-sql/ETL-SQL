# Choropleth Map Charts

**Pattern**: Color-scaled geographic regions driven by a data column. Six bundled maps require no external files; zip-code and custom-boundary maps require a user-supplied GeoJSON file.

### Bundled maps (no file needed)

Specify `MAP_NAME` with one of the six built-in keys. The engine serves the GeoJSON from `/maps/{name}.geojson` automatically.

| `MAP_NAME` | Regions | Match column contains |
|---|---|---|
| `WORLD` | 177 countries | Country name — `"France"`, `"United States of America"` |
| `US_STATES` | 50 states + DC | State name — `"Minnesota"`, `"New York"` |
| `US_COUNTIES` | 3 221 counties | County name — `"Hennepin"`, `"Cook"` |
| `MN_COUNTIES` | 87 MN counties | County name — `"Hennepin"`, `"Ramsey"` |
| `CANADA_PROVINCES` | 13 provinces/territories | Province name — `"Alberta"`, `"Ontario"` |
| `EUROPE` | 39 countries | Country name — `"France"`, `"Germany"` |

```sql
-- Revenue by US state
SELECT State, SUM(Revenue) AS Revenue
INTO #state_rev
FROM dbo.Sales
GROUP BY State;

CREATE VISUAL RevenueMap AS MAP (
  SOURCE   = #state_rev,
  MAPPINGS (REGION = State, VALUE = Revenue),
  OPTIONS  (
    MAP_NAME   = US_STATES,
    COLOR_LOW  = '#e0f2fe',
    COLOR_HIGH = '#0369a1',
    TITLE      = 'Revenue by State'
  )
);
```

### Matching by FIPS code instead of name

County data often carries FIPS codes rather than names. Set `MATCH_BY = FIPS` and put the 5-digit FIPS code in the region column.

```sql
SELECT fips_code, incident_count
INTO #incidents
FROM dbo.CountyIncidents;

CREATE VISUAL IncidentMap AS MAP (
  SOURCE   = #incidents,
  MAPPINGS (REGION = fips_code, VALUE = incident_count),
  OPTIONS  (
    MAP_NAME = US_COUNTIES,
    MATCH_BY = FIPS,           -- matches feature id (e.g. "27053") not name
    COLOR_LOW  = '#fef9c3',
    COLOR_HIGH = '#b45309',
    TITLE = 'Incidents by County'
  )
);
```

### Zip code choropleth

> **There is no bundled ZIP code map.** US ZIP Code Tabulation Area (ZCTA) GeoJSON from the Census Bureau is ~300 MB uncompressed — too large to bundle. To map zip codes:
>
> 1. Download the simplified ZCTA file yourself from the Census Cartographic Boundary Files:  
>    `https://www.census.gov/geographies/mapping-files/time-series/geo/cartographic-boundary.html`  
>    (choose **ZCTAs**, **20m** simplification for the smallest usable file, ~25 MB).
> 2. Place the file anywhere accessible to the Report Player — e.g., alongside your `.rptsql` file.
> 3. Reference it with `MAP_FILE`:

```sql
SELECT zip_code, SUM(orders) AS Orders
INTO #zip_orders
FROM dbo.OrdersByZip
GROUP BY zip_code;

CREATE VISUAL ZipMap AS MAP (
  SOURCE   = #zip_orders,
  MAPPINGS (REGION = zip_code, VALUE = Orders),
  OPTIONS  (
    MAP_FILE   = 'C:\Reports\Maps\cb_2023_us_zcta520_20m.geojson',
    MATCH_BY   = NAME,          -- ZCTA features use the zip code as their name property
    COLOR_LOW  = '#f0fdf4',
    COLOR_HIGH = '#166534',
    TITLE      = 'Orders by ZIP Code'
  )
);
```

> **Tip**: If your ZCTA file is still too large to load comfortably, filter it to only the states or metro area you need using a tool like [mapshaper.org](https://mapshaper.org) (free, browser-based) before placing it in your maps folder.

### Point map (city names, lat/lon coordinates)

Cities are points, not polygons — they cannot be choropleth-filled. Use `MODE = POINTS` with `LON_COL` and `LAT_COL` mappings to scatter-plot locations on a base map instead. The `VALUE` mapping controls dot size.

```sql
SELECT city_name, longitude, latitude, SUM(revenue) AS Revenue
INTO #city_rev
FROM dbo.SalesByCity
GROUP BY city_name, longitude, latitude;

CREATE VISUAL CityMap AS MAP (
  SOURCE   = #city_rev,
  MAPPINGS (
    LON   = longitude,
    LAT   = latitude,
    VALUE = Revenue,
    LABEL = city_name
  ),
  OPTIONS  (
    MAP_NAME = US_STATES,      -- base map for context
    MODE     = POINTS,
    TITLE    = 'Revenue by City'
  )
);
```

> **If you only have city names and no coordinates**: geocode them first in your ETL script using a `LOOKUP` against a reference table, or pre-join to a reference dataset that maps city names to lat/lon before the visual's `SOURCE` query.

### Key Points

- `MAP_NAME` selects a bundled map; `MAP_FILE` points to a custom GeoJSON file on disk. Exactly one is required.
- The `REGION` mapping column must match the region's `name` property in the GeoJSON (case-insensitive). Use `MATCH_BY = FIPS` to match on the numeric FIPS `id` instead.
- `COLOR_LOW` and `COLOR_HIGH` define the two-color gradient. Regions with no data row are rendered in a neutral grey.
- `MODE = CHOROPLETH` (default) fills regions. `MODE = POINTS` plots scatter dots using `LON`/`LAT` mappings on the same base map.
- Zip code maps require a user-supplied ZCTA GeoJSON (~25 MB at 20m simplification). See the Census Cartographic Boundary Files link above.
