# PRINT_LAYOUT

Defines print layout overrides (page breaks, keep-together, exclusions) on individual visuals within a paginated report.

```sql
CREATE VISUAL DepartmentChart AS BAR (
    ...
    PRINT_LAYOUT = (
        PAGE_BREAK_BEFORE = ON,
        KEEP_TOGETHER = ON,
        EXCLUDE_FROM_PRINT = OFF
    )
);
```

- **PAGE_BREAK_BEFORE = ON|OFF** — Forces a page break before this visual is rendered.
- **PAGE_BREAK_AFTER = ON|OFF** — Forces a page break immediately after this visual.
- **KEEP_TOGETHER = ON|OFF** — Instructs the paginator to avoid breaking the visual across multiple pages if possible.
- **EXCLUDE_FROM_PRINT = ON|OFF** — Hides this visual during PDF rendering or print generation.

## References
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
