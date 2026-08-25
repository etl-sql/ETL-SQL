# CONFIG
<!-- ShowConnectionConfigStatement -->

`eng.connection_config` exposes the redacted configuration options and parameters of active data source connections.

## Syntax

```sql
SELECT * [INTO #temp_table] FROM eng.connection_config WHERE connection_name = '<connection_name>';
```

## Description

Retrieves a list of all configured options and values for the specified connection name. For security and compliance, sensitive credentials such as PASSWORD, PWD, and KEYFILE are redacted in the output.

## Columns Returned

- **Option** - The name of the configuration option, such as HOST, DATABASE, USER, or PORT.
- **Value** - The configured value, redacted with mask characters if sensitive.

## Examples

```sql
-- Inspect the configuration of the SalesDB connection
SELECT * FROM eng.connection_config WHERE connection_name = 'SalesDB';
```

```sql
-- Save configuration to a temp table to query it programmatically
SELECT * INTO #api_config FROM eng.connection_config WHERE connection_name = 'WebAPI';
SELECT Value FROM #api_config WHERE Option = 'URL';
```

References:
- [Statements](../README.md)


## References

- [SET](config.md)
- [Statements](../README.md)
