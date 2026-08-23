# Report Ownership and Trust Badges

Published reports display standardized metadata badges in the report runtime header and Portal catalog cards. Badges provide immediate visual trust indicators regarding report ownership, data governance, certification tier, and snapshot data freshness.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS). Badges render in the standalone Report Player as well as Portal catalog cards.

## Badge Types

| Badge Icon | Type | Description | Example |
| :---: | :--- | :--- | :--- |
| 👤 | **Owner** | The operational team responsible for maintaining the report script. | `👤 Finance Analytics Team` |
| 🛡️ | **Steward** | Designated data steward responsible for data quality and governance. | `🛡️ Jane Doe (Data Governance)` |
| ⭐ | **Certification** | Compliance / trust tier awarded by data stewards (`Certified Gold`, `Silver`, `Verified`). | `⭐ Certified Gold` |
| 🕒 | **Freshness** | Timestamp and status derived from the last snapshot build time (`BuiltAt`). | `🕒 2026-07-22 18:00 UTC` |
| 🏷️ | **Tags** | Metadata keywords applied via report headers or Portal catalog tagging. | `🏷️ #sales`, `🏷️ #q3` |

---

## Configuring Badges in Report Scripts (`.rptsql`)

Title and description badges are populated automatically from metadata directives placed at the top of your `.rptsql` file:

```sql
SET REPORT TITLE = 'Q3 Regional Sales Summary';
SET REPORT DESCRIPTION = 'Interactive Data Insight and Revenue Breakdown';

CREATE CONNECTION db AS MOCKDB();
SELECT 'Q3' AS Quarter, 120000 AS Revenue INTO #q3_summary;

CREATE VISUAL Q3Card AS CARD (
  SOURCE = #q3_summary,
  MAPPINGS (VALUE = Revenue, LABEL = Quarter)
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = Q3Card)
  )
);
```

When publishing to the Portal, ownership, stewardship, certification tier, and catalog tags are managed via the Portal metadata catalog:

```sql
EXECUTE portal BEGIN
  PUBLISH REPORT '/Finance/Q3_Sales'
    FROM 'reports/q3_sales.rptsql'
    WITH (
      OWNER = 'Finance Analytics Team',
      STEWARD = 'Jane Doe',
      CERTIFICATION = 'Certified Gold',
      TAGS = 'sales,q3,finance'
    );
END;
```

---

## Visual Styling and Themes

Badges render cleanly across all built-in themes:
- **Light Theme**: Soft background pills (`#f0f4f8`), gold highlight for certifications (`#fef3c7`), green highlight for fresh snapshots (`#dcfce7`).
- **Dark Theme**: High-contrast dark pills (`#334155`), amber highlight (`#78350f`), deep green (`#14532d`).

---

## Related Topics

- [Authoring Dashboards](authoring-dashboards.md) — 3-tier architecture and dashboard design.
- [Custom Theming and Branding](custom-theming-and-branding.md) — Global shell styling and CSS overrides.
- [Catalog Search & Discovery Guide](../tooling/catalog-search.md) — Finding certified reports.
