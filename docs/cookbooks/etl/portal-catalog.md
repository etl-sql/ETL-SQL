# Publishing and Operating a Portal Catalog
Portal administration is script-first: connect with `PORTAL`, then send catalog commands inside an `EXECUTE <portal> BEGIN ... END` block. This makes report deployment repeatable across environments and keeps validation, publication, refresh, and catalog inspection in one reviewable script.

**Pattern Scenario:** Validate and publish a finance report, trigger its first refresh, and capture the resulting catalog metadata.

```sql
CREATE CONNECTION portal AS PORTAL(
    HOST = 'http://report-server.company.com:5000',
    USER = 'admin',
    PASSWORD = 'ENC:U2FsdGVkX1+...'
);

EXECUTE portal BEGIN
    CREATE FOLDER '/Finance';

    VALIDATE REPORT SCRIPT 'C:\Reports\Finance\monthly_sales.rptsql'
        INTO #validation;

    PUBLISH REPORT 'Monthly Sales'
        FROM 'C:\Reports\Finance\monthly_sales.rptsql'
        IN FOLDER '/Finance'
        WITH (
            DESCRIPTION = 'Monthly revenue by region',
            TAGS = 'finance,monthly,certified'
        );

    REFRESH REPORT 'Monthly Sales';
    SELECT * INTO #report FROM eng.reports WHERE name = 'Monthly Sales';
    SELECT * INTO #history FROM eng.report_history('Monthly Sales');
    SELECT * INTO #dependencies FROM eng.report_dependencies('Monthly Sales');
END;
```

> Use the same script with environment-specific connection values during promotion. See [Reference/Grammar.md](../../guides/onboarding/getting-started.md) Appendix B for users, groups, permissions, subscriptions, share links, embed tokens, saved views, alerts, and usage metrics.
