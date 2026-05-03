Type: IMAGE
Embeds a static or dynamic image — logo, product photo, map snapshot, or QR code. The source can be a file path, URL, or base-64 data URI from a query.

Mappings:
  SRC     — column containing the image path/URL/data-URI (use with SOURCE query)
            or omit SOURCE and use DEFAULT = 'path/url' for a static image

Options:
  FIT     = 'contain'|'cover'|'fill'|'none'  — CSS object-fit behaviour (default 'contain')
  WIDTH   = 'css-value'    — e.g. '100%', '300px'
  HEIGHT  = 'css-value'

Static image (no query needed):
```sql
CREATE VISUAL Logo AS IMAGE (
  DEFAULT = '/assets/company-logo.png',
  OPTIONS (FIT = 'contain', WIDTH = '200px', HEIGHT = '80px')
);
```

Dynamic image from data:
```sql
SELECT product_id, image_url FROM #products WHERE featured = 1
INTO #hero;

CREATE VISUAL ProductHero AS IMAGE (
  SOURCE   = #hero,
  MAPPINGS (SRC = image_url),
  OPTIONS  (FIT = 'cover', WIDTH = '100%', HEIGHT = '300px')
);
```
