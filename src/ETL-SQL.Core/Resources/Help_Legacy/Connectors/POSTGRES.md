# POSTGRES
Connects to PostgreSQL using the Npgsql driver. Supports schemas, PostgreSQL-specific types, and SSL.

Syntax:
  CREATE CONNECTION <name> AS POSTGRES(
    HOST     = 'localhost',
    PORT     = 5432,
    DATABASE = 'dbname',
    USER     = 'username',
    PASSWORD = '<password>',
    SSL_MODE = 'Disable' | 'Require' | 'Prefer'
  );

Options:
- **HOST** — server hostname or IP (required)
- **PORT** — port number (default 5432)
- **DATABASE** — database name (required)
- **USER** — username (required)
- **PASSWORD** — password
- **SSL_MODE** — TLS mode (default Prefer)
- **TIMEOUT_SECONDS** — command/query execution timeout in seconds (default 30)
- **TABLE** — default table for unqualified SELECT/INSERT

```sql
CREATE CONNECTION AppDB AS POSTGRES(
  HOST     = 'pg.corp.local',
  PORT     = 5432,
  DATABASE = 'app',
  USER     = @pg_user,
  PASSWORD = @pg_pass,
  SSL_MODE = 'Require'
);

SELECT id, email, created_at
  INTO #users
  FROM AppDB.public.users
  WHERE active = TRUE;

PRINT 'Users loaded: ' + @@ROWCOUNT;
```

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
