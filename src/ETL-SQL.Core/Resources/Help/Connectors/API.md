# API
Connects to REST or HTTP endpoints. Use SELECT to call the endpoint and parse the JSON response into a result set. Use INSERT or EXECUTE to POST or PUT data.

Syntax:
  CREATE CONNECTION <name> ON API(
    URL       = 'https://...',
    METHOD    = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE',
    AUTH_TYPE = 'NONE' | 'BASIC' | 'BEARER' | 'APIKEY',
    TOKEN     = '<value>',
    ROOT_PATH = '$.data',
    BODY      = '{ ... }'
  );

Options:
  URL          — endpoint URL (required)
  METHOD       — HTTP verb (default GET)
  AUTH_TYPE    — authentication scheme (default NONE)
  TOKEN        — Bearer token, API key value, or password for BASIC auth
  USER         — username for BASIC auth
  ROOT_PATH    — JSONPath expression to locate the response array
  BODY         — JSON body sent with POST/PUT requests
  TIMEOUT      — request timeout in seconds (default 30)
  HEADERS      — additional HTTP headers as a JSON object string

```sql
CREATE CONNECTION GithubAPI ON API(
  URL       = 'https://api.github.com/repos/owner/repo/issues',
  AUTH_TYPE = 'BEARER',
  TOKEN     = @github_token,
  ROOT_PATH = '$'
);

SELECT number, title, state, created_at
  INTO #issues
  FROM GithubAPI;

PRINT 'Issues loaded: ' + @@ROWCOUNT;
```

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
