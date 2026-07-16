# ACTIVE_DIRECTORY
Connects to an Active Directory or LDAP server to perform user, group, and computer lookups. Automatically translates SQL WHERE clauses into raw LDAP filter queries.

Syntax:
  CREATE CONNECTION <name> AS ACTIVE_DIRECTORY(
    HOST           = 'ldap.corp.com',
    PORT           = 389,
    USE_SSL        = FALSE,
    AUTH_MODE      = 'NEGOTIATE', -- NEGOTIATE, SIMPLE, INTEGRATED
    USER           = 'username',
    PASSWORD       = '<password>',
    DOMAIN         = 'CORP',
    BASE_DN        = 'OU=Employees,DC=corp,DC=com',
    FILTER_CONTEXT = 'users', -- users, groups, computers
    ATTRIBUTES     = 'sAMAccountName,displayName,mail'
  );

Options:
- **HOST** — LDAP/AD server hostname or IP (required)
- **PORT** — Connection port (default 389, or 636 for SSL)
- **USE_SSL** — Connect via secure LDAPS (default FALSE)
- **AUTH_MODE** — Bind method: NEGOTIATE, SIMPLE, INTEGRATED (default INTEGRATED)
- **USER** — User account name or bind DN (for SIMPLE / NEGOTIATE)
- **PASSWORD** — Binding password (for SIMPLE / NEGOTIATE)
- **DOMAIN** — Active Directory Domain name
- **BASE_DN** — Search base Distinguished Name
- **FILTER_CONTEXT** — Built-in search scope: 'users', 'groups', or 'computers' (default 'users')
- **FILTER** — Raw LDAP filter override (bypasses automatic SQL parsing)
- **ATTRIBUTES** — Comma-separated AD attributes list to retrieve

```sql
CREATE CONNECTION ActiveDir AS ACTIVE_DIRECTORY(
  HOST      = 'ldap.corp.company.com',
  BASE_DN   = 'DC=corp,DC=company,DC=com',
  AUTH_MODE = 'NEGOTIATE',
  USER      = 'svc_etl',
  PASSWORD  = ENC:U2FsdGVkX1+...
);

-- Search active users with smith in their name
SELECT sAMAccountName, displayName, mail, memberOf
FROM ActiveDir
WHERE sAMAccountName = '*smith*';
```

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
