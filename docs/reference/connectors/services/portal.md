# PORTAL

Admin service connector for an ETL-SQL Portal service. Does not transfer data — statements inside an
`EXECUTE portal BEGIN ... END` block are dispatched to the Portal's REST API for scripted
administration: user/group management, folder ACLs, report publishing, dataset refresh, snapshots,
shared connections, and more.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Portal base URL (e.g. `http://portal-server:5000`) | Yes |
| `PORT` | Override port when `HOST` has no port | No |
| `USER` | Portal admin username | Yes |
| `PASSWORD` | Portal admin password (use `ENC:` in production) | Yes |

> [!NOTE]
> JWT authentication is acquired automatically on first use and refreshed as needed. The `PASSWORD`
> value is never logged or stored in session state.

## Example

```sql
CREATE CONNECTION portal AS PORTAL(HOST     = 'http://portal.corp.example:5000',
         USER     = 'admin',
         PASSWORD = ENC:U2FsdGVkX1+...);

EXECUTE portal BEGIN
    -- User & group management
    CREATE USER 'jsmith' WITH EMAIL='j@corp.com', ROLE='Viewer';
    ALTER USER 'jsmith' SET ROLE = 'Editor';
    CREATE GROUP 'DataTeam' WITH DESCRIPTION='Data Engineering';
    ADD USER 'alice' TO GROUP 'DataTeam';

    -- Folder management & ACL
    CREATE FOLDER '/Finance/Reports';
    GRANT VIEW ON FOLDER '/Finance/Reports' TO GROUP 'DataTeam';
    DROP FOLDER '/Finance/Reports' CASCADE;

    -- Report lifecycle
    PUBLISH REPORT 'Monthly Sales' FROM 'reports/monthly_sales.rptsql' IN FOLDER '/Finance/Reports';
    REFRESH REPORT 'Monthly Sales';
    REBUILD SNAPSHOT FOR REPORT 'Monthly Sales';

    -- Dataset management
    REFRESH DATASET 'sales_ds' IN FOLDER '/Finance';
    ALTER DATASET 'sales_ds' IN FOLDER '/Finance' SET (ACCESS = PUBLIC, TTL = '02:00:00');

    -- Governed connections (SMTP is an ordinary connector; credentials are SECRET: references)
    CREATE CONNECTION corporate AS SMTP(
        HOST = 'smtp.corp.example', USERNAME = 'mailer',
        PASSWORD = 'SECRET:corporate_smtp_password', DEFAULT_FROM = 'reports@corp.example');
    SELECT * FROM eng.connections;   -- filter for SMTP aliases when needed

    -- Discovery
    SELECT * FROM eng.users;
    SELECT * FROM eng.reports WHERE folder = '/Finance/Reports';
END;

EXECUTE orch BEGIN
    CREATE SCHEDULE MonthlySalesNightly ON '0 2 * * *';
    CREATE JOB MonthlySalesRefresh FOR REPORT '/Finance/Reports/Monthly Sales';
    ALTER JOB MonthlySalesRefresh ADD SCHEDULE MonthlySalesNightly;
END;
```

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [Orchestrator](orchestrator.md)
