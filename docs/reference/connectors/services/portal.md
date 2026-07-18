# PORTAL

Admin service connector for an ETL-SQL Portal service. Does not transfer data — statements inside an
`EXECUTE portal BEGIN ... END` block are dispatched to the Portal's REST API for scripted
administration: user/group management, folder ACLs, report publishing, dataset refresh, snapshots,
SMTP connections, and more.

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
    PUBLISH REPORT 'Monthly Sales' FROM SCRIPT 'reports/monthly_sales.rsql' IN FOLDER '/Finance/Reports';
    REFRESH REPORT 'Monthly Sales';
    REBUILD SNAPSHOT FOR REPORT 'Monthly Sales';

    -- Dataset management
    REFRESH DATASET 'sales_ds' IN FOLDER '/Finance';
    ALTER DATASET 'sales_ds' IN FOLDER '/Finance' WITH SCHEDULE='0 2 * * *';

    -- Refresh jobs (routed to Orchestrator)
    CREATE REFRESH JOB FOR REPORT 'Monthly Sales' SCHEDULE '0 2 * * *' AT orch;

    -- SMTP connections (portal-managed mail credentials)
    CREATE SMTP CONNECTION 'corporate' WITH (
        HOST = 'smtp.corp.example', USERNAME = 'mailer',
        PASSWORD = ENC:U2FsdGVkX1+..., FROM_ADDRESS = 'reports@corp.example');
    SHOW SMTP CONNECTIONS;   -- never returns passwords

    -- Discovery
    SHOW USERS;
    SHOW REPORTS IN FOLDER '/Finance/Reports';
END;
```

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [Orchestrator](orchestrator.md)
