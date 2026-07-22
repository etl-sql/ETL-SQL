# Report Ownership & Data Freshness Badges

Published reports display standardized metadata badges in the report runtime header and Portal catalog cards. Badges provide immediate visual trust indicators regarding report ownership, data governance, certification level, and data freshness.

## Badge Types

| Badge Icon | Type | Description | Example |
| :---: | :--- | :--- | :--- |
| 👤 | **Owner** | The primary operational team or contact responsible for maintaining the report script. | `👤 Finance Analytics Team` |
| 🛡️ | **Steward** | Designated data steward responsible for data quality and governance. | `🛡️ Jane Doe (Data Governance)` |
| ⭐ | **Certification** | Compliance / trust tier awarded by data stewards (`Certified Gold`, `Silver`, `Verified`). | `⭐ Certified Gold` |
| 🕒 | **Freshness** | Timestamp and freshness status derived from the last snapshot build time (`BuiltAt`). | `🕒 2026-07-22 18:00 UTC` |
| 🏷️ | **Tags** | Metadata keywords applied via report header script directives or Portal catalog tagging. | `🏷️ #sales`, `🏷️ #q3` |

## Visual Styling & Theme Support

Header badges render at the top of every report in both light and dark themes:
- **Light Theme**: Soft background pills (`#f0f4f8`), gold highlight for certifications (`#fef3c7`), green highlight for fresh snapshots (`#dcfce7`).
- **Dark Theme**: High-contrast dark pills (`#334155`), amber highlight (`#78350f`), deep green (`#14532d`).

```html
<header class="report-header">
  <div class="header-left">
    <div class="header-title">Q3 Regional Sales Summary</div>
    <div class="header-subtitle">Interactive Data Insight</div>
    <div class="header-badges">
      <span class="header-badge owner">👤 Finance Analytics Team</span>
      <span class="header-badge cert">⭐ Certified Gold</span>
      <span class="header-badge fresh">🕒 2026-07-22 18:00</span>
      <span class="header-badge tag">🏷️ #sales</span>
    </div>
  </div>
</header>
```

## Configuring Badges in Report Scripts (`.rptsql`)

Badges are populated automatically from metadata directives placed at the top of `.rptsql` files:

```sql
SET REPORT TITLE = 'Q3 Regional Sales Summary';
SET REPORT DESCRIPTION = 'Interactive Data Insight';
SET REPORT OWNER = 'Finance Analytics Team';
SET REPORT STEWARD = 'Jane Doe';
SET REPORT CERTIFICATION = 'Certified Gold';
SET REPORT TAGS = 'sales, quarterly, executive';
```

## References
- [Report-SQL Guide](report-sql.md)
- [Catalog Search & Discovery Guide](catalog-search.md)
- [Portal User Guide](portal-user.md)
