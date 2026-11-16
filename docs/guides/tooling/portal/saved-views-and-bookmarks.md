# Saved Views and Bookmarks

> **Applies to:** Solo · Team · Enterprise · SaaS

Save a named view to return to a report configuration without re-entering its parameters. Saved views belong to you. Report authors can provide shared bookmarks and an author default in the report script.

## What a saved view captures

A saved view captures typed parameter values, the active page, and named objects' visibility and collapsed state. Portal stores that state with the report revision. If the author republishes the report, an older saved view can show a warning that parts of its state may no longer apply.

| | Saved view | Author bookmark |
| :--- | :--- | :--- |
| Who creates it | A reader with report access | Report author |
| Where it lives | Portal, owned by the reader | Report script |
| Who can use it | Its owner | Readers of the report |
| Default | Your personal default | Author's report default |

## Save and restore a view

1. Open the report and set its parameters and presentation state.
2. Open **Views** in the report toolbar.
3. Choose **Save current view as…** and enter a name.
4. Reopen **Views** and select the name under **My saved views** to apply it.

Use the action beside a saved view to update it to the current state, make it your default, or delete it. Deletion requires confirmation. The star marks your default view. The menu also lets you save the current state as your default or return to the report default.

A saved view's identifier is checked against its owner whenever it is loaded. Sharing a saved-view URL does not give another user access to that view.

## Create a view via script

Use a PORTAL connection authenticated as the user who will own the view:

```sql
EXECUTE portal BEGIN
  CREATE SAVED VIEW 'EMEA quarter' FOR REPORT 'RegionalBreakdown'
    PARAMETERS (@region = 'EMEA', @quarter = '2026-Q1');
END;
```

Add `DEFAULT` to replace that user's default for the report:

```sql
EXECUTE portal BEGIN
  CREATE SAVED VIEW 'Executive summary' FOR REPORT 'ExecutiveDashboard'
    DEFAULT PARAMETERS (@tier = 'Gold');
END;
```

Script assignments are string literals. Values such as `'001'` and `'true'` keep their string types. Parameter interpretation still follows the report's declared types.

## Defaults when opening a report

An explicit bookmark or saved-view identifier takes precedence. Without one, Portal tries your default saved view, then the author's default bookmark, then the report's declared defaults. Having no saved default does not prevent the report from opening.

## Remove a view via script

```sql
EXECUTE portal BEGIN
  DROP SAVED VIEW 'EMEA quarter' FOR REPORT 'RegionalBreakdown';
END;
```

## API clients and v0.22.0 upgrades

Create/update requests send the resolved-state envelope in `stateJson`; the default-view request sends it in `state`. The retired top-level `parameters` and `filters` fields are rejected, including explicit nulls.

Stop older Portal hosts before applying the schema migration. Existing envelopes are preserved. Old flat string maps are converted once, with filters overriding matching parameters. Invalid old maps stop the upgrade and require repair or replacement with a valid envelope before retrying. Mixed-version operation across this migration is unsupported.

## Related guides

- [Saved View Reference](../../../reference/portal-commands/portal-savedview.md)
- [Browsing and Running Reports](browsing-and-running-reports.md)
- [Sharing and Embed Tokens](sharing-and-embed-tokens.md)
