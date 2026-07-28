# ORCHESTRATOR

Admin service connector for an ETL-SQL Orchestrator service. Does not transfer data — statements inside
an `EXECUTE orch BEGIN ... END` block are dispatched to the Orchestrator's REST API for remote job
management, using API-key authentication.

Aliases: `ORCH`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Orchestrator base URL (e.g. `http://orch-server:5001`) | Yes |
| `PORT` | Override port when `HOST` has no port | No |
| `API_KEY` | Orchestrator API key (use `ENC:` in production) | No |

## Example

```sql
CREATE CONNECTION orch AS ORCHESTRATOR(HOST    = 'http://orchestrator.corp.example:5001',
         API_KEY = ENC:U2FsdGVkX1+...);

EXECUTE orch BEGIN
    CREATE SCHEDULE MonthlySalesNightly ON '0 2 * * *';
    CREATE JOB MonthlySalesRefresh FOR REPORT '/Finance/Monthly Sales';
    ALTER JOB MonthlySalesRefresh ADD SCHEDULE MonthlySalesNightly;
    DROP JOB IF EXISTS MonthlySalesRefresh;
END;
```

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [Portal](portal.md)
