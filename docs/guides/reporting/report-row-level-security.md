# Row-Level Security in Reports (RLS)

The Web Portal executes report queries securely within the context of the logged-in viewer's identity. Using built-in session identity variables and group predicate functions, you can dynamically restrict visible rows so that users only see data they are authorized to access.

---

> **Applies to:** Team, Enterprise, and SaaS deployments. In workstation environments (Solo), identity variables default to the local OS user and `@@IS_ADMIN = TRUE`.

## Built-In Identity Variables & Predicates

### System Identity Variables

| Variable | Type | Description |
| :--- | :--- | :--- |
| `@@CURRENT_USER` | `VARCHAR` | The username or email of the active viewer. |
| `@@CURRENT_USER_ID` | `INT` | The unique numeric identifier of the viewer. |
| `@@REAL_USER` | `VARCHAR` | The authenticated identity (distinct from `@@CURRENT_USER` during impersonation or service account runs). |
| `@@IS_ADMIN` | `BIT` | Returns `TRUE` if the viewer belongs to the Administrator role. |

### Identity Predicate Functions

| Function | Return Type | Description |
| :--- | :--- | :--- |
| `HAS_GROUP('group_name')` | `BIT` | Returns `TRUE` if the current viewer is a member of the specified security group. |
| `USER_GROUPS()` | `TABLE` | Table-valued function returning all groups assigned to the current viewer (`GroupName VARCHAR`). |

---

## Example 1: Direct Username & Owner Filtering

Filter rows where the manager or account owner matches the logged-in user. Administrators see all rows.

```sql
SET REPORT TITLE = 'My Account Portfolio';

CREATE CONNECTION db AS MOCKDB();

SELECT AccountId, AccountName, OwnerUsername, Revenue
INTO #accounts
FROM db.Accounts;

-- Filter query using @@CURRENT_USER
CREATE VISUAL UserAccountsTable AS TABLE (
  SOURCE = (SELECT AccountId, AccountName, Revenue
            FROM #accounts
            WHERE OwnerUsername = @@CURRENT_USER 
               OR @@IS_ADMIN = TRUE)
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = UserAccountsTable)
  )
);
```

---

## Example 2: Role-Based & Group Access (`HAS_GROUP`)

Restrict regional performance metrics based on membership in regional security groups.

```sql
SET REPORT TITLE = 'Regional Sales Performance';

CREATE CONNECTION db AS MOCKDB();

SELECT Region, RepName, SalesAmount
INTO #regional_data
FROM db.RegionalSales;

-- Apply group predicates per region
CREATE VISUAL RegionChart AS BAR (
  SOURCE = (SELECT Region, SUM(SalesAmount) AS TotalSales
            FROM #regional_data
            WHERE (Region = 'NorthAmerica' AND HAS_GROUP('NA_Sales') = TRUE)
               OR (Region = 'EMEA'         AND HAS_GROUP('EMEA_Sales') = TRUE)
               OR (Region = 'APAC'         AND HAS_GROUP('APAC_Sales') = TRUE)
               OR @@IS_ADMIN = TRUE
            GROUP BY Region),
  MAPPINGS (X = Region, Y = TotalSales)
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = RegionChart)
  )
);
```

---

## Example 3: Dynamic Data-Driven RLS with `USER_GROUPS()`

For complex organizations with hundreds of departments or cost centers, maintain a mapping table and join against `USER_GROUPS()`. This avoids hardcoding group names into your queries.

```sql
SET REPORT TITLE = 'Department Cost Center Review';

CREATE CONNECTION db AS MOCKDB();

-- Base financial transactions
SELECT CostCenterId, ExpenseAmount, Description
INTO #expenses
FROM db.Expenses;

-- Security mapping table
SELECT CostCenterId, GroupName
INTO #cost_center_permissions
FROM db.CostCenterGroups;

-- Dynamic membership join
CREATE VISUAL SecureExpenseTable AS TABLE (
  SOURCE = (SELECT e.CostCenterId, e.Description, e.ExpenseAmount
            FROM #expenses e
            JOIN #cost_center_permissions p ON e.CostCenterId = p.CostCenterId
            WHERE p.GroupName IN (SELECT GroupName FROM USER_GROUPS())
               OR @@IS_ADMIN = TRUE)
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = SecureExpenseTable)
  )
);
```

---

## Common Pitfalls

- **Admin Bypass Configuration**: By default, adding `OR @@IS_ADMIN = TRUE` allows platform administrators to see all rows. If your organization's compliance policy mandates that admins are subject to identical RLS filters, omit `OR @@IS_ADMIN = TRUE` and set `Portal:Security:AdminBypassRowLevelSecurity = false` in `appsettings.json`.
- **Filtering in Tier 2 `#temp` tables**: If you filter by `@@CURRENT_USER` during initial `#temp` table creation, that `#temp` table will be scoped only to the user who triggered the report snapshot. When sharing pre-built snapshots across multiple users, perform RLS filtering in the visual's `SOURCE = (SELECT ...)` query.

---

## Related Topics

- [Authoring Dashboards](authoring-dashboards.md) — Three-tier logic model and visual sources.
- [Report Parameters and Filters](report-parameters-and-filters.md) — Slicers and interactive filters.
- [Portal Administration](../../administration/portal/README.md) — User and group management.
