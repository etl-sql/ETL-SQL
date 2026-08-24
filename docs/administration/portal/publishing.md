# Publishing Reports

> **Applies to:** Team · Enterprise · SaaS

Register a `.rptsql` script as a named report in a Portal folder, and manage the versioning, sharing, and promotion controls around it.

> [!TIP]
> **Solo/Workstation users:** Publishing is not a prerequisite for *using* reports. Run the same `.rptsql` with the CLI or the Report Player. This guide is a prerequisite for *sharing* reports.

---

## By Deployment Profile

| Profile | How a report reaches its audience |
| :--- | :--- |
| **Solo / Workstation** | Run `.rptsql` with the CLI or Report Player. No Portal is required. |
| **Team / SME** | Publish into a folder, grant the folder to a group. Optionally require review before publish (`Portal:Studio:RequireApprovalToPublish`). |
| **Enterprise / Corporate** | As Team, plus protected branches and separation of duties — an author can never approve their own draft, including Admin. The reviewer is written into a `Reviewed-by:` commit trailer so the review outlives the Portal database. |
| **SaaS / Departmental** | As Enterprise, per environment. Catalogs never merge: a report id from one environment is meaningless in another, so promotion between environments is an explicit export/import, not a shared folder. |

---

## Quick Publish

1. Upload or copy the `.rptsql` file into the Portal's `ScriptRootPath` directory.
2. Open **Admin → Folders**, select the destination folder.
3. Click **Publish Report**, fill in **Name**, **Description**, and **Script path** (relative to `ScriptRootPath`).

Or via script:

```sql
EXECUTE portal BEGIN
  PUBLISH REPORT 'Monthly Sales'
      FROM 'reports/finance/monthly_sales.rptsql'
      IN FOLDER '/Finance'
      WITH (DESCRIPTION = 'Monthly revenue by region', TAGS = 'finance,monthly');
END;
```

When the active signed organization policy declares `metadata.requiredTags`, publishing checks report metadata and every `CREATE DATASET` column in the script. Missing required tags return `400 organization_metadata_policy`. See [Authoritative Organization Policy](../platform/organization-policy.md).

---

## Publishing Guides

| Guide | What it covers |
| :--- | :--- |
| [Report Publishing Workflows](report-publishing-workflows.md) | Script-first CLI publishing, GUI steps, metadata tags, script hash pinning, update/delete, and environment promotion |
| [Report Versioning and Promotion](report-versioning-and-promotion.md) | Dataset at-rest key lifecycle, key rotation, in-place upgrades, rollback, and orphan reconciliation |
| [Embed Tokens and Sharing](embed-tokens-and-sharing.md) | Share links, embed tokens, saved views, alerts, effective permissions, usage metrics, catalog search |

---

## Related

- [Portal Administration](README.md)
- [Groups and Folder Permissions](permissions.md)
- [Authoritative Organization Policy](../platform/organization-policy.md)
- [Portal Configuration](../platform/config/portal-configuration.md) — `Portal:Dataset:*` keys
