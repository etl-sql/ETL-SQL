# Containerized Test Databases (USE DOCKER)
<!-- DockerActionStatement -->

Spawns, manages, and connects to ephemeral database containers during test runs or migration workflows.

---

## Spawning a Container

## Syntax

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

The database is identified by the image's repository name; registry, tag and digest are ignored.
An image whose name only contains a database name (`someone/postgres-tools`) is refused.

- **SQL Server** — repository `mssql/server`; user `sa`; port 1433.
- **PostgreSQL** — repository `postgres`; user `etluser`, database `etldb`; port 5432.
- **MySQL / MariaDB** — repository `mysql` or `mariadb`; user `etluser`, database `etldb`; port 3306.
- **Oracle** — repository `oracle-xe` or `oracle-free` (or `database/free`, `database/express`); user `system` on Free images, `oracle` otherwise; port 1521.

Every container gets a generated password. Read it from `<alias>.CONNECTION_STRING`; never hard-code
it. Ports are published on the loopback interface only, so the container is not reachable from other
machines.

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

## Examples

```sql
USE DOCKER('postgres:16') AS my_postgres;
```
