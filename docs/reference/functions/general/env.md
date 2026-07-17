# ENV

Returns the value of a host environment variable visible to the ETL-SQL process.

## Syntax

```sql
ENV(name)
```

## Parameters

- **name** - Environment variable name as a string expression.

## Returns

Returns the environment variable value as `STRING`, or `NULL` when the variable is not defined.

## Null Behavior

`ENV(NULL)` returns `NULL`.

## Security Notes

- Do not print, concatenate, or export secret environment variables.
- Prefer configured secret providers and `SECRET:name` references for credentials.
- Environment variable availability depends on the process host, service manager, container, or scheduler.

## Examples

```sql
DECLARE @artifactRoot = ENV('ETLSQL_ARTIFACT_ROOT');

IF @artifactRoot IS NULL
BEGIN
  THROW 'ETLSQL_ARTIFACT_ROOT is not configured.';
END;
```

```sql
DECLARE @host = ENV('PGHOST');
DECLARE @user = ENV('PGUSER');

CREATE CONNECTION src AS POSTGRES(
  HOST = @host,
  DATABASE = 'Sales',
  USER = @user,
  PASSWORD = 'SECRET:pg_password'
);
```

## References

- [Functions](../README.md)
- [Configuration Settings](../../../administration/platform/settings.md)
