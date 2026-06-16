CONFIG is a keyword used exclusively with the SHOW CONNECTION command to inspect the configuration options and parameters of a data source connection.

Syntax:
  SHOW CONNECTION <connection_name> CONFIG [INTO #temp_table];

Description:
  Retrieves a list of all configured options and values for the specified connection name.
  For security and compliance, sensitive credentials (such as PASSWORD, PWD, and KEYFILE) are redacted in the output.

Columns Returned:
- **Option** — The name of the configuration option (e.g. HOST, DATABASE, USER, PORT)
- **Value** — The configured value (redacted with mask characters if sensitive)

Examples:
  -- Inspect the configuration of the SalesDB connection
  SHOW CONNECTION SalesDB CONFIG;

  -- Save configuration to a temp table to query it programmatically
  SHOW CONNECTION WebAPI CONFIG INTO #api_config;
  SELECT Value FROM #api_config WHERE Option = 'URL';

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
