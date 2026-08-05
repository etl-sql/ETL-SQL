# Catalog Search & Discovery Guide
<!-- SearchPortalCatalogStatement -->

The ETL-SQL Portal includes a fuzzy, tokenized catalog search engine that allows non-technical business consumers and developers to discover reports, datasets, and folders across the enterprise.

> **Applies to:** Team, Enterprise and SaaS — catalog search is a Portal feature. On Solo / Workstation, discover scripts with the file system and `eng.*` catalog views.

## Features & Capabilities

- **Fuzzy Token Matching** — Tokenizes multi-word queries (`"Q3 Sales"`) and matches title components regardless of word order or casing.
- **Typo Tolerance** — Computes Levenshtein edit distance for tokens with length ≥ 3, matching misspelled queries like `"Salse Revenue"` to `"Sales Revenue"`.
- **Tag & Synonym Discovery** — Matches report tags (`#sales`, `#inventory`), categories, domains, owners, stewards, and certifications.
- **Metric & Column Search** — Inspects underlying dataset metadata and column names.
- **Relevance Ranking** — Ranks search results deterministically:
  1. Exact title / folder matches (Score: 1.0)
  2. Substring & fuzzy title matches (Score: 0.75-0.85)
  3. Description & tag matches
  4. Column & folder path matches
- **Permission-Aware** — Returns only assets the caller is authorized to view.

## Usage Options

### Web Portal API
`GET /api/catalog/search?q={query}&limit=50`

```json
[
  {
    "type": "Report",
    "id": 12,
    "name": "RPT_2026_SALES_Q3_FINAL",
    "path": "/Finance/Sales/RPT_2026_SALES_Q3_FINAL",
    "description": "Q3 Sales and Regional Breakdown",
    "tags": "#sales,#quarterly",
    "owner": "Finance Analytics Team",
    "certification": "Certified Gold",
    "isFavorite": true
  }
]
```

### ETL-SQL Engine Command
`SELECT * [INTO #table] FROM portal.eng.catalog_search('<query>');`

```sql
EXECUTE portal BEGIN
    SELECT * INTO #results FROM eng.catalog_search('quarterly sales');
    SELECT Name, Path, Owner FROM #results WHERE Certification IS NOT NULL;
END;
```

### Business Consumer Home Endpoint
`GET /api/catalog/consumer-home?limit=10`

Returns a composite view with four curated categories:
- `favorites` — Reports favorited by the current user
- `recent` — Recently viewed reports
- `featured` — Certified or steward-approved reports
- `popular` — Most frequently viewed reports

## References
- [Portal Admin Commands](../reference/portal-admin/README.md)
- [Engine Catalog Reference](../reference/eng/README.md)
- [Portal User Guide](portal-user.md)
