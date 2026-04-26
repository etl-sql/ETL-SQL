# ETL-SQL Report Portal: User Guide

The Report Portal is a web application that lets you browse, run, and subscribe to reports built with Report-SQL scripts. You don't need to know ETL-SQL syntax to use it — this guide covers everything from first login to managing your own subscriptions.

---

## Contents

1. [Logging In](#1-logging-in)
2. [Navigating the Portal](#2-navigating-the-portal)
3. [Browsing Folders & Reports](#3-browsing-folders--reports)
4. [Running a Report](#4-running-a-report)
5. [Viewing & Exporting Results](#5-viewing--exporting-results)
6. [Subscribing to a Report](#6-subscribing-to-a-report)
7. [Managing Your Subscriptions](#7-managing-your-subscriptions)
8. [Changing Your Password](#8-changing-your-password)

---

## 1. Logging In

Navigate to the portal URL provided by your administrator (e.g. `http://yourserver:5050`). You will be redirected to the login page.

Enter your **username** and **password**, then click **Sign In**.

> [!NOTE]
> If your administrator created your account, you will be required to set a new password before you can access any reports. The portal will redirect you to the change-password form automatically.

### 1.1 Session Tokens

Once logged in, the portal issues a short-lived access token (default: 60 minutes) and a longer-lived refresh token (default: 7 days). The refresh token silently re-authenticates you in the background, so you are unlikely to be asked to log in again within a week of normal use.

---

## 2. Navigating the Portal

The portal has two main areas accessible from the top navigation:

| Area | Purpose |
| :--- | :--- |
| **Reports** (`/`) | Browse folders and run reports |
| **Admin** (`/admin.html`) | User, group, subscription, and audit management — visible to Admins only |

---

## 3. Browsing Folders & Reports

The Reports page shows a **folder tree** in the left sidebar. Click a folder to expand it and see its sub-folders and reports.

- Folders you do not have access to are not shown.
- A **stale** indicator appears next to a report name when the source script has been modified since the last snapshot was built.

Click a report name to open it.

---

## 4. Running a Report

When you open a report, the portal loads the most recent **snapshot** — a pre-built result set stored on the server. If no snapshot exists yet, the report panel shows a **Run** button.

### 4.1 Using the Run Button

Click **Run** (or **Refresh**) to execute the report's underlying ETL-SQL script now. Execution happens asynchronously; a progress indicator appears while the job runs. When complete, the snapshot is updated and the result is displayed.

> [!IMPORTANT]
> Running a report re-executes the script against live data sources. On large datasets this can take several minutes. Your administrator can set per-report execution timeouts.

### 4.2 Report Parameters

If a report declares parameters, an input form appears above the result panel. Fill in the values and click **Run** — the parameter values are passed to the script at execution time.

---

## 5. Viewing & Exporting Results

A report can contain multiple **visuals** — charts, tables, KPI cards, and Markdown prose. These are rendered in the order defined by the Report-SQL script.

### 5.1 Charts

Interactive ECharts visualisations. Hover over data points for tooltips. Use the chart toolbar (top-right of each chart) to:

- Download the chart as a PNG image
- Switch between chart view and the underlying data table

### 5.2 Tables

Paginated data tables. Click a column header to sort. Use the search box above the table to filter rows client-side.

### 5.3 Exporting

Use the **Export** menu on any report to download the full result:

| Format | Notes |
| :--- | :--- |
| **CSV** | Raw data for the selected visual, Excel-compatible |
| **PDF** | Rendered snapshot of all visuals — suitable for sharing |

---

## 6. Subscribing to a Report

A subscription delivers a report to your email inbox on a schedule.

1. Open a report.
2. Click the **Subscribe** button (envelope icon).
3. Fill in the subscription form:

| Field | Description |
| :--- | :--- |
| **Schedule** | `Daily`, `Weekly`, or `Monthly` |
| **At Time** | Time of day for delivery (24-hour, e.g. `08:00`) |
| **Format** | `PDF`, `CSV`, `Markdown`, or `Link` (portal URL only) |
| **Recipient email** | Defaults to your account email; can be overridden |

4. Click **Save**.

> [!NOTE]
> The **Link** format sends only a URL pointing to the live report — no attachment is generated. This is the fastest option and requires no file export.

> [!TIP]
> Your administrator must configure an SMTP connection before subscriptions with attachments can be delivered. If the SMTP connection is missing, choose the **Link** format as a fallback.

---

## 7. Managing Your Subscriptions

Open **My Subscriptions** from the user menu (top-right). The list shows all subscriptions you own, with their schedule, next run time, and last delivery status.

- **Pause / Resume** — toggle the `IsActive` flag without deleting the subscription.
- **Delete** — removes the subscription and cancels any pending scheduled job.
- **History** — shows the last delivery attempts with timestamps and any error messages.

---

## 8. Changing Your Password

1. Click your username in the top-right corner and choose **Change Password**.
2. Enter your current password, then your new password twice.
3. Click **Save**.

Passwords must be at least 8 characters and contain at least one digit.

> [!IMPORTANT]
> After a password change your current session remains valid, but all other active sessions (e.g. on other devices) are invalidated. You will need to log in again on those devices.
