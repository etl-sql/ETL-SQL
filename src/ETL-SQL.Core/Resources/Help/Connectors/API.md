# API
Connects to REST or HTTP endpoints. Use SELECT to call the endpoint and parse the JSON response into a result set. Use INSERT to send rows to the API.

Syntax:
  CREATE CONNECTION <name> AS API(
    URL       = 'https://...',
    METHOD    = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE',
    AUTH_TYPE = 'NONE' | 'BASIC' | 'BEARER' | 'APIKEY' | 'OAUTH2_CLIENT_CREDENTIALS',
    TOKEN     = '<value>',
    ROOT_PATH = '$.data',
    BODY      = '{ ... }'
  );

Supported Options:
  URL          - Endpoint URL (required)
  METHOD       - HTTP method (default GET)
  AUTH_TYPE    - Authentication scheme: NONE, BASIC, BEARER, APIKEY, OAUTH2_CLIENT_CREDENTIALS (default NONE)
  TOKEN        - Bearer token, API key value, or password for BASIC auth
  USER         - Username for BASIC auth
  PASSWORD     - Password for BASIC auth
  HEADER_NAME  - Name of the header for APIKEY auth (e.g. X-API-KEY)
  ROOT_PATH    - JSONPath expression to locate the response array for reading
  BODY         - JSON body sent with connection-level requests
  BODY_CONTENT_TYPE - Content-Type header value (default application/json)
  TIMEOUT_SECONDS - Request timeout in seconds (default 30)
  VALIDATE_JSON_BODY - Validate template JSON body before sending (default TRUE)
  MAX_RETRY_AFTER_SECONDS - Cap on Retry-After header delay in seconds (default 60)

OAuth2 Options (for AUTH_TYPE='OAUTH2_CLIENT_CREDENTIALS'):
  TOKEN_URL    - OAuth2 token endpoint URL
  CLIENT_ID    - OAuth2 client id
  CLIENT_SECRET - OAuth2 client secret
  SCOPE        - Optional OAuth2 scope
  TOKEN_CACHE_SECONDS - Token cache duration override in seconds

Pagination Options:
  PAGINATION_MODE - Pagination strategy: NONE, PAGE, OFFSET, CURSOR, LINK_HEADER (default NONE)
  PAGE_PARAM      - Query parameter for page number (default page)
  PAGE_START      - First page number (default 1)
  OFFSET_PARAM    - Query parameter for offset (default offset)
  LIMIT_PARAM     - Query parameter for page size/limit (default limit)
  PAGE_SIZE       - Requested page size
  CURSOR_PARAM    - Query parameter for cursor token
  CURSOR_PATH     - JSONPath to next cursor in response body
  NEXT_URL_PATH   - JSONPath to next-page URL in response body
  MAX_PAGES       - Safety cap on total retrieved pages (default 1000)

Outbound Write (INSERT) Options:
  INSERT writes support POST, PUT, and PATCH. DELETE is only available for direct request execution.
  BODY_MODE                    - Format for writing data:
                                 - ROW_OBJECT: Each row is sent as a single request (default).
                                 - ROW_ARRAY: Writes are batched into a JSON array.
                                 - WRAPPED_ARRAY: Batches are wrapped in a JSON envelope.
                                 - TEMPLATE: Custom template substitution per-row.
  BATCH_SIZE                   - Number of rows per batch request in array modes (default 500).
  BATCH_ROOT                   - Envelope key name required for WRAPPED_ARRAY (e.g. 'submissions').
  RESPONSE_TABLE               - Name of a temp table (e.g. #results) to capture API call outcomes.
  RESPONSE_CORRELATION_COLUMNS - Source columns to copy into the RESPONSE_TABLE.
  RESPONSE_ITEM_PATH           - JSONPath to response array for per-item correlation mapping.
  SUCCESS_STATUS               - Comma-separated list of successful HTTP status codes (default '200,201,202,204').
  ERROR_MODE                   - Error policy: FAIL_FAST (default) or CONTINUE.
  RETRY_COUNT                  - Retry attempts for transient failures (default 0).
  RETRY_BACKOFF_MS             - Base exponential backoff delay in milliseconds (default 500).
  RETRY_STATUS                 - Comma-separated HTTP statuses triggering retries (default '408,429,500,502,503,504').
  IDEMPOTENCY_KEY_COLUMN       - Row column to use as idempotency key header.
  IDEMPOTENCY_HEADER           - Header name for idempotency key. Default: Idempotency-Key.
  URL_TEMPLATE                 - Dynamic url string with ${column_name} placeholders.
  BODY_TEMPLATE                - Dynamic body string with ${column_name} placeholders.
  ERROR_BODY_MAX_CHARS         - Max characters of response body retained in error messages (default 4096).

Example (Read):
```sql
CREATE CONNECTION GithubAPI AS API(
  URL       = 'https://api.github.com/repos/owner/repo/issues',
  AUTH_TYPE = 'BEARER',
  TOKEN     = @github_token,
  ROOT_PATH = '$'
);

SELECT number, title, state, created_at
INTO #issues
FROM GithubAPI;
```

Example (Write):
```sql
CREATE CONNECTION BedUsageAPI AS API(
  URL       = 'https://example.org/api/bed-usage',
  METHOD    = 'POST',
  AUTH_TYPE = 'BEARER',
  TOKEN     = @api_token,
  BODY_MODE = 'ROW_OBJECT',
  RESPONSE_TABLE = '#api_results',
  RESPONSE_CORRELATION_COLUMNS = 'submission_id'
);

INSERT INTO BedUsageAPI (submission_id, location, totalBeds, occupiedBeds)
SELECT id, location, total, occupied
FROM #bed_usage;
```

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
