# SHOW CONNECTION CONFIG
Displays configuration options for a specific connection with sensitive values redacted.

## Syntax
```sql
SHOW CONNECTION <conn> CONFIG [INTO #table];
```

## Parameters
- **conn** — The name of a registered connection.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with option name and value pairs for the specified connection. Passwords, keys, and other secrets are masked.

## Example
```sql
CREATE CONNECTION SalesDB AS MSSQL(SERVER='sales01', DATABASE='Sales', TRUSTED_CONNECTION=TRUE);

-- View the connection's resolved configuration
SHOW CONNECTION SalesDB CONFIG;

-- Capture for programmatic inspection
SHOW CONNECTION SalesDB CONFIG INTO #cfg;
SELECT OptionName, OptionValue FROM #cfg;
```

## Notes
- Sensitive values (passwords, API keys, `ENC:` strings) are redacted in the output.
- The connection must already be registered in the session.

## References
- [SHOW Commands](README.md)
