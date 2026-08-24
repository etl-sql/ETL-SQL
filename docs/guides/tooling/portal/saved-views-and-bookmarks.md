# Saved Views and Bookmarks

> **Applies to:** Solo · Team · Enterprise · SaaS

Save named parameter presets so you can return to a specific report configuration without re-entering values each time. Saved views are personal by default; administrators can promote them to shared defaults.

---

## What Is a Saved View?

A saved view captures the current parameter state of an open report — which slicer values are selected, what dates are chosen, which filters are active — and stores it under a name you choose. Loading a saved view restores those parameter values instantly.

Saved views differ from published reports:

| | Saved View | Published Report |
| :--- | :--- | :--- |
| Who creates it | Any report viewer | Report author / admin |
| What it stores | Parameter preset | Full `.rptsql` script + snapshot |
| Who can see it | You (personal) or everyone (shared) | Governed by folder permissions |
| How to load | Select from "My Views" | Open from the catalog |

---

## Creating a Saved View from the Portal UI

1. Open a report and set the parameters you want to save (slicers, date pickers, search inputs).
2. Click the **Save View** button (bookmark icon in the toolbar).
3. Enter a name (e.g. `EMEA - Last 30 Days`) and optionally a description.
4. Click **Save**.

The view appears in the **My Views** dropdown for that report.

> [!TIP]
> Name your views descriptively so colleagues can understand them if you promote a view to a shared default.

---

## Creating a Saved View via Script

Portal administrators can create saved views programmatically:

```sql
EXECUTE portal BEGIN
    CREATE SAVED VIEW 'EMEA Last 30 Days'
    FOR REPORT 'Sales/RegionalBreakdown'
    WITH PARAMETERS (
        @region  = 'EMEA',
        @start   = 'D-30',
        @end     = 'D-0'
    );
END;
```

```sql
-- Create a shared view visible to all users with report access
EXECUTE portal BEGIN
    CREATE SAVED VIEW 'Exec Summary - MTD'
    FOR REPORT 'Finance/ExecutiveDashboard'
    SHARED = TRUE
    WITH PARAMETERS (
        @period = 'M-0',
        @tier   = 'Gold'
    );
END;
```

---

## Loading a Saved View

From the Portal UI:

1. Open the report.
2. Click the **Views** dropdown (bookmark icon or "My Views" label) in the toolbar.
3. Select the saved view name. Parameters populate instantly and the report reruns.

> [!NOTE]
> Relative date expressions stored in a view (`D-30`, `M-1`) resolve to actual dates at the time you load the view, not when you saved it. This means "Last 30 Days" always shows the last 30 days from today.

---

## Managing Saved Views

Open **My Views** from the user menu to see all views you own:

| Action | Effect |
| :--- | :--- |
| **Load** | Apply the view's parameters to the current report |
| **Edit** | Update the stored parameter values |
| **Rename** | Change the view's display name |
| **Delete** | Remove the view permanently |
| **Share** | Promote a personal view to a shared default (admin-only) |

---

## Dropping a Saved View via Script

```sql
EXECUTE portal BEGIN
    DROP SAVED VIEW 'EMEA Last 30 Days'
    FROM REPORT 'Sales/RegionalBreakdown';
END;
```

---

## Related Guides

- [Browsing and Running Reports](browsing-and-running-reports.md) — using interactive parameters
- [Exporting and Subscriptions](exporting-and-subscriptions.md) — deliver a saved view on a schedule
- [Sharing and Embed Tokens](sharing-and-embed-tokens.md) — share the report URL with others
