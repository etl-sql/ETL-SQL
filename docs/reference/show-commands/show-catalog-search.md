# SHOW CATALOG SEARCH
Searches the portal catalog for reports matching a text query using fuzzy token matching and relevance scoring.

## Syntax
```sql
SHOW CATALOG SEARCH '<text>' [INTO #table];
```

## Parameters
- **'text'** — Search string matched against report titles, descriptions (`SET REPORT DESCRIPTION`), tags (`#sales`, `#inventory`), categories, domains, owners, stewards, and column/metric names. Supports fuzzy token matching and typo tolerance.
- **INTO #table** — Optional. Captures results into an engine temporary table for downstream filtering or processing.

## Returns
A result set containing:
- **Type** — `Report` or `Folder`
- **Id** — Entity ID
- **Name** — Report or folder name
- **Path** — Full folder path
- **Description** — Report description
- **Tags** — Comma-separated tag badges
- **Category** — Category classification
- **Owner** — Report owner/team
- **Certification** — Certification tier (e.g. `Certified Gold`)
- **IsFavorite** — Whether favorited by current user

## Relevance & Ranking
1. **Exact Title Matches** — Direct match on report title or folder name (Score: 1.0)
2. **Fuzzy Title Matches** — Typo-tolerant Levenshtein distance matches on title words (Score: 0.75-0.85)
3. **Tags & Descriptions** — Matching `#tag` values or description text
4. **Metrics & Folder Paths** — Matching folder hierarchy or underlying dataset metric names

## Examples
```sql
EXECUTE portal BEGIN
    -- Standard search with fuzzy token matching
    SHOW CATALOG SEARCH 'Q3 Sales';

    -- Typo-tolerant search for revenue reports
    SHOW CATALOG SEARCH 'Salse Revenue' INTO #catalog;

    SELECT Name, Path, Owner, Certification
    FROM #catalog
    WHERE IsFavorite = TRUE;
END;
```

## Notes
- Must be executed inside an `EXECUTE portal BEGIN...END` block.
- Respects portal RBAC permissions; users only see reports they have permission to access unless restricted-report discovery is enabled by policy.

## References
- [SHOW Commands Reference](README.md)
- [Catalog Search & Discovery Guide](../../guides/catalog-search.md)
