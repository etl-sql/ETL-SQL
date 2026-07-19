# ACTIVE_DIRECTORY

Connects to an Active Directory or LDAP server to perform user, group, and computer lookups. Standard
SQL `WHERE` clauses (e.g. `sAMAccountName = 'smith'`) are parsed and translated dynamically into native
LDAP filter queries.

Aliases: `AD`, `LDAP`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Server host name or IP address (e.g. `ldap.corp.com`) | Yes (structured) |
| `PORT` | Directory port (default: `389` for LDAP, `636` for LDAPS) | No |
| `USE_SSL` | Enable SSL encryption / LDAPS connection (`TRUE`/`FALSE`) | No |
| `AUTH_MODE` | Authentication mode: `INTEGRATED`, `SIMPLE` (basic auth over SSL), `NEGOTIATE` (default: `INTEGRATED`) | No |
| `USER` | Login username / bind Distinguished Name (DN) | No |
| `PASSWORD` | Login password (use `ENC:` prefix) | No |
| `DOMAIN` | Domain name | No |
| `BASE_DN` | LDAP search base Distinguished Name (e.g. `OU=Users,DC=corp,DC=com`) | No |
| `FILTER_CONTEXT` | Scope context: `users`, `groups`, or `computers` (default: `users`) | No |
| `FILTER` | Raw LDAP query filter (overrides `FILTER_CONTEXT` and standard AD parsing) | No |
| `ATTRIBUTES` | Comma-separated list of attributes to query | No |

> [!CAUTION]
> `AUTH_MODE = 'SIMPLE'` transmits credentials in plaintext unless `USE_SSL=TRUE` (LDAPS) is active. Use
> `USE_SSL=TRUE` with simple binding.

## Examples

```sql
-- Search users with Negotiate auth over standard LDAP
CREATE CONNECTION ad_corp AS ACTIVE_DIRECTORY(
         HOST       = 'ldap.corp.example.com',
         BASE_DN    = 'DC=corp,DC=example,DC=com',
         AUTH_MODE  = 'NEGOTIATE',
         USER       = 'domain_service',
         PASSWORD   = ENC:U2FsdGVkX1+...,
         DOMAIN     = 'CORP');

-- Query using the AD connection
SELECT sAMAccountName, displayName, mail, memberOf
FROM ad_corp
WHERE sAMAccountName = 'jdoe';
```

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [SharePoint](sharepoint.md)
