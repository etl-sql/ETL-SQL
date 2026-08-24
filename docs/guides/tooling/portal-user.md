# ETL-SQL Portal: User Guide

The Portal is a web application that lets you browse, run, and subscribe to reports built with Report-SQL scripts. You don't need to know ETL-SQL syntax to use it.

> **Applies to:** Team, Enterprise and SaaS — this guide describes the Portal. On Solo / Workstation, run and view the same reports with the CLI or the Report Player instead.

> [!TIP]
> This page is a navigation hub. The Portal user guide has been restructured into focused, task-based pages. Follow the links below to go directly to what you need.

---

## Portal User Guides

| Guide | What it covers |
| :--- | :--- |
| [Browsing and Running Reports](portal/browsing-and-running-reports.md) | Catalog navigation, search, opening reports, interactive parameters (slicers, date pickers, multi-select), and freshness badges |
| [Exporting and Subscriptions](portal/exporting-and-subscriptions.md) | Generating PDF/CSV/Excel snapshots on-demand and configuring scheduled email subscription delivery |
| [Sharing and Embed Tokens](portal/sharing-and-embed-tokens.md) | Creating share links, generating embed tokens for external `<iframe>` hosting, and security considerations |
| [Saved Views and Bookmarks](portal/saved-views-and-bookmarks.md) | Saving named parameter presets, restoring views, sharing defaults with colleagues |

---

## Logging In

Navigate to the portal URL provided by your administrator (for example, `http://yourserver:5000`). Enter your **username** and **password**, then click **Sign In**.

> [!NOTE]
> If your administrator created your account, you will be required to set a new password before accessing any reports. The portal will redirect you to the change-password form automatically.

Once logged in, the portal issues a short-lived access token (default: 60 minutes) and a longer-lived refresh token (default: 7 days). The refresh token silently re-authenticates you in the background.

---

## Changing Your Password

1. Click your username in the top-right corner and choose **Change Password**.
2. Enter your current password, then your new password twice.
3. Click **Save**.

> [!IMPORTANT]
> After a password change your current session remains valid, but all other active sessions (e.g. on other devices) are invalidated.

---

## Related

- [Portal Administration](../../administration/portal/README.md) — publishing, permissions, and audit (admin tasks)
- [Reporting Guides](../reporting/README.md) — report authoring reference
