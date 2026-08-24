# Sharing Reports and Embed Tokens

> **Applies to:** Team · Enterprise · SaaS

Share Portal reports securely with colleagues or embed them in external applications without requiring a Portal account.

---

## Share Links

A share link provides time-limited, read-only access to a specific report — useful for sending to stakeholders who do not have a Portal account.

### Create a Share Link from the Portal UI

1. Open the report you want to share.
2. Click the **Share** button (link icon in the toolbar).
3. Set an expiry date (default: 30 days).
4. Click **Copy Link**.

The recipient opens the URL in any browser and views the report without logging in.

> [!CAUTION]
> Share links are publicly accessible to anyone with the URL. Set an appropriate expiry and revoke links when no longer needed.

### Create a Share Link via Script

```sql
EXECUTE portal BEGIN
    CREATE SHARE LINK
    FOR REPORT 'Sales/MonthlyOverview'
    EXPIRES '2027-01-01'
    ROLE VIEWER;
END;
```

```sql
-- Create a short-lived link for an external review meeting
EXECUTE portal BEGIN
    CREATE SHARE LINK
    FOR REPORT 'Finance/QuarterlyReview'
    EXPIRES IN 48 HOURS
    ROLE VIEWER;
END;
```

### Revoke a Share Link

Share links can be revoked from the Portal admin panel (**Admin → Share Links**) or by script:

```sql
EXECUTE portal BEGIN
    REVOKE SHARE LINK 'sl_a3f9b2c1d';
END;
```

---

## Embed Tokens

An embed token allows you to display a Portal report inside an `<iframe>` in your own web application. The token authenticates the iframe session without requiring users to log in to the Portal separately.

### When to use embed tokens

- Internal dashboards inside an intranet portal or SharePoint page
- Customer-facing reports embedded in your SaaS product
- Kiosk displays or meeting room screens

> [!IMPORTANT]
> Embed tokens bypass Portal login for the embedded report. Scope them to the minimum required report and shortest practical lifetime. Use `EXPIRES IN` whenever possible rather than a fixed future date.

### Generate an Embed Token via Script

```sql
EXECUTE portal BEGIN
    CREATE EMBED TOKEN
    FOR REPORT 'Operations/LiveStatus'
    EXPIRES IN 8 HOURS;
END;
```

```sql
-- Token scoped to a specific report with parameter defaults
EXECUTE portal BEGIN
    CREATE EMBED TOKEN
    FOR REPORT 'Sales/RegionalBreakdown'
    EXPIRES IN 24 HOURS
    WITH PARAMETERS (@region = 'EMEA');
END;
```

The command outputs a token string. Pass it as a query parameter to the Portal embed URL:

```html
<iframe
  src="https://portal.company.com/embed?token=<embed_token>"
  width="100%"
  height="600"
  frameborder="0"
  allowfullscreen>
</iframe>
```

### CORS Configuration

Your Portal administrator must add your external application's origin to the Portal's `AllowedOrigins` setting before the iframe will load. Contact your administrator if you see a CORS error in the browser console.

---

## Security Considerations

| Risk | Mitigation |
| :--- | :--- |
| Link/token leaked via email or Slack | Set short expiry; revoke immediately if compromised |
| Embedding on a public website | Use the shortest practical expiry; scope to a specific report |
| Parameter injection via iframe URL | Report parameters passed in embed tokens are server-validated; untrusted URLs cannot override token-scoped values |
| Token enumeration | Tokens are cryptographically random and not guessable |

---

## Related Guides

- [Browsing and Running Reports](browsing-and-running-reports.md) — Portal navigation basics
- [Saved Views and Bookmarks](saved-views-and-bookmarks.md) — save parameter presets for sharing
- [Portal Administration](../../../administration/portal/README.md) — CORS config, share-link revocation, and audit logs
