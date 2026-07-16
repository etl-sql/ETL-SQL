# MYSQL
Connects natively to MySQL and MariaDB using the MySqlConnector driver. Supports high-performance bulk copies, connection pooling, and SSL options.

Syntax:
  CREATE CONNECTION <name> AS MYSQL(
    HOST                       = 'localhost',
    PORT                       = 3306,
    DATABASE                   = 'dbname',
    USER                       = 'username',
    PASSWORD                   = '<password>',
    SSL_MODE                   = 'None' | 'Preferred' | 'Required' | 'VerifyCA' | 'VerifyFull',
    ALLOW_PUBLIC_KEY_RETRIEVAL = 'TRUE' | 'FALSE',
    ALLOW_USER_VARIABLES       = 'TRUE' | 'FALSE'
  );

Aliases:
  MARIADB

Options:
- **HOST / SERVER** — server hostname or IP (required)
- **PORT** — port number (default 3306)
- **DATABASE** — database name (required)
- **USER / UID** — username (required)
- **PASSWORD / PWD** — password
- **SSL_MODE** — TLS mode (default Preferred)
- **ALLOW_PUBLIC_KEY_RETRIEVAL** — set to TRUE to allow RSA public key retrieval from server (default FALSE)
- **ALLOW_USER_VARIABLES** — set to TRUE to allow user variables like @var inside queries (default FALSE)
- **TIMEOUT_SECONDS** — command/query execution timeout in seconds (default 30)
- **TABLE** — default table for unqualified SELECT/INSERT

```sql
CREATE CONNECTION AppDB AS MYSQL(
  HOST     = 'mysql.corp.local',
  PORT     = 3306,
  DATABASE = 'app',
  USER     = @db_user,
  PASSWORD = @db_pass,
  SSL_MODE = 'Required'
);

SELECT id, email, created_at
  INTO #users
  FROM AppDB.users
  WHERE active = 1;

PRINT 'Users loaded: ' + @@ROWCOUNT;
```

References:
- [Data Connectors](../../../guides/administration.md)
