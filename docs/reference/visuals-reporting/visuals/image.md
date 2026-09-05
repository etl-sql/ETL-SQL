# IMAGE

Embeds static images, query-driven dynamic photos, or multi-item image galleries with accessibility and fallback support.

## Syntax

```sql
CREATE VISUAL VisualName AS IMAGE (
  [SOURCE = #tableName,]
  [MAPPINGS (
    [SRC|URL = <column_name>]
  ),]
  [CONTENT = '<image_url_or_path>',]
  OPTIONS (
    ALT = '<accessibility_description>',
    [MODE = SINGLE|GALLERY,]
    [COLUMNS = <int_columns>,]
    [ASPECT_RATIO = '<ratio>',]
    [FALLBACK = '<fallback_image_url>',]
    [FIT = 'contain'|'cover'|'fill'|'none',]
    [WIDTH = '<css_size>',]
    [HEIGHT = '<css_size>']
  )
  [, ACTIONS (ON_CLICK = <action>)]
);
```

## Mappings

- **SRC** — Column containing the image URL, file path, or base64 data URI (URL is also accepted).

## Options

- **ALT = 'description'** — Accessible alternative text description for assistive technologies and screen readers (linter rule RPT4001).
- **MODE = SINGLE|GALLERY** — Display single image or a multi-image gallery grid from source rows (default `SINGLE`).
- **COLUMNS = n** — Number of grid columns when `MODE = GALLERY` (default `3`).
- **ASPECT_RATIO = 'ratio'** — CSS aspect ratio constraint (e.g. `'16:9'`, `'4:3'`, `'1:1'`).
- **FALLBACK = 'url'** — Fallback image URL loaded automatically if primary image resource fails.
- **FIT = 'contain'|'cover'|'fill'|'none'** — CSS object-fit behavior for container boundaries (default `'contain'`).
- **WIDTH = 'size'** — CSS width rule (e.g. `'100%'`, `'300px'`).
- **HEIGHT = 'size'** — CSS height rule (e.g. `'200px'`, `'auto'`).

## Examples

```sql
CREATE VISUAL CompanyLogo AS IMAGE (
  CONTENT = '/assets/brand-logo.svg',
  OPTIONS (
    ALT = 'Company Brand Logo',
    WIDTH = '180px',
    HEIGHT = '60px',
    FIT = 'contain'
  ),
  ACTIONS (
    ON_CLICK = OPEN_URL('https://portal.example.com', TARGET = '_blank')
  )
);
```

```sql
SELECT photo_url, product_name INTO #catalog_images FROM #products;

CREATE VISUAL ProductGallery AS IMAGE (
  SOURCE = #catalog_images,
  MAPPINGS (URL = photo_url),
  OPTIONS (
    ALT = 'Product Catalog Gallery',
    MODE = GALLERY,
    COLUMNS = 4,
    ASPECT_RATIO = '16:9',
    FALLBACK = '/assets/placeholder.png',
    FIT = 'cover'
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
