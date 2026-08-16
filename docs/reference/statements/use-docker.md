# Containerized Test Databases (USE DOCKER)
<!-- DockerActionStatement -->

Spawns, manages, and connects to ephemeral database containers during test runs or migration workflows.

---

## Spawning a Container

```sql
USE DOCKER('<image>') [AS <alias>];

USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest') AS mssql_db;
USE DOCKER('postgres:15-alpine')                         AS pg_db;
USE DOCKER('gvenzl/oracle-free:latest')                  AS ora_db;
```

After startup, the connection string is available dynamically via the `<alias>.CONNECTION_STRING` property:

```sql
DECLARE @conn VARCHAR(500) = mssql_db.CONNECTION_STRING;
CREATE CONNECTION stage_db AS MSSQL(@conn);
```

---

## Supported Images

| Database | Image pattern | Default credentials | Port |
| :--- | :--- | :--- | :--- |
| SQL Server | contains `mssql` | `sa` / `Password123!` | 1433 |
| PostgreSQL | contains `postgres` | `postgres` / `postgres` | 5432 |
| Oracle | contains `oracle` | `system` / `oracle` | 1521 |

---

## Lifecycle Commands

| Command | Effect |
| :--- | :--- |
| `START DOCKER <alias>` | Resume a stopped container |
| `STOP DOCKER <alias>` | Stop the container (state preserved) |
| `PAUSE DOCKER <alias>` | Suspend CPU (faster resume than stop/start) |
| `CLOSE DOCKER <alias>` | Destroy container and all its state |
| `CLOSE DOCKER` | Destroy **all** containers in the session |

> Containers are **not** automatically closed when a script ends. Always include an explicit `CLOSE DOCKER` or wrap in `TRY...CATCH`.

---

## Multiple Containers

```sql
USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest') AS src;
USE DOCKER('postgres:15-alpine')                         AS dst;

CREATE CONNECTION source_db AS MSSQL(src.CONNECTION_STRING);
CREATE CONNECTION target_db AS POSTGRES(dst.CONNECTION_STRING);

SELECT * INTO #tmp FROM source_db.dbo.Customers;
INSERT INTO target_db.public.customers SELECT * FROM #tmp;

CLOSE DOCKER;
```

---

## References

- [Statement Reference](README.md)
- [CREATE CONNECTION](ddl/create.md)
- [Syntax Index](../../syntax-index.md)
