# API

Universal connector for web services and REST APIs. Supports `SELECT` to call endpoints and parse JSON
results, and `INSERT` to write batches or rows to endpoints.

Aliases: `REST`, `HTTP`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `URL` | The endpoint URL | Yes |
| `METHOD` | HTTP method: `GET`, `POST`, `PUT`, `PATCH`, `DELETE` (default: `GET`) | No |
| `AUTH_TYPE` | Authentication mode: `NONE`, `BASIC`, `BEARER`, `APIKEY`, `OAUTH2_CLIENT_CREDENTIALS` (default: `NONE`) | No |
| `USER` | Username (for `BASIC` auth) | No |
| `PASSWORD` | Password (for `BASIC` auth) | No |
| `TOKEN` | Secret token (for `BEARER` or `APIKEY` auth) | No |
| `HEADER_NAME` | Header name for `APIKEY` auth (e.g. `X-API-Key`) | No |
| `ROOT_PATH` | JSONPath to the data array within the response (e.g. `$.items`) | No |
| `BODY` | JSON request body for `POST`/`PUT`/`PATCH` connections | No |
| `TIMEOUT_SECONDS` | Request timeout in seconds (default: `30`) | No |
| `VALIDATE_JSON_BODY` | Validate template JSON body before sending (default: `TRUE`) | No |
| `MAX_RETRY_AFTER_SECONDS` | Cap on Retry-After header delay in seconds (default: `60`) | No |
| `TOKEN_URL` | OAuth2 token endpoint URL | No |
| `CLIENT_ID` | OAuth2 client id | No |
| `CLIENT_SECRET` | OAuth2 client secret | No |
| `SCOPE` | Optional OAuth2 scope | No |
| `TOKEN_CACHE_SECONDS` | Token cache duration override in seconds | No |
| `PAGINATION_MODE` | Pagination strategy: `NONE`, `PAGE`, `OFFSET`, `CURSOR`, `LINK_HEADER` (default: `NONE`) | No |
| `PAGE_PARAM` | Query parameter for page number (default: `page`) | No |
| `PAGE_START` | First page number (default: `1`) | No |
| `OFFSET_PARAM` | Query parameter for offset (default: `offset`) | No |
| `LIMIT_PARAM` | Query parameter for page size/limit (default: `limit`) | No |
| `PAGE_SIZE` | Requested page size | No |
| `CURSOR_PARAM` | Query parameter for cursor token | No |
| `CURSOR_PATH` | JSONPath to next cursor in response body | No |
| `NEXT_URL_PATH` | JSONPath to next-page URL in response body | No |
| `MAX_PAGES` | Safety cap on total retrieved pages (default: `1000`) | No |
| `BODY_MODE` | Outbound write format: `ROW_OBJECT`, `ROW_ARRAY`, `WRAPPED_ARRAY`, `TEMPLATE` (default: `ROW_OBJECT`) | No |
| `BATCH_SIZE` | Size of batch writes for array modes (default: `500`) | No |
| `BATCH_ROOT` | Envelope property name required for `WRAPPED_ARRAY` | No |
| `RESPONSE_TABLE` | Temp table name (e.g. `#my_results`) to store API response metadata | No |
| `RESPONSE_CORRELATION_COLUMNS` | Comma-separated columns to copy from source row to response table | No |
| `RESPONSE_ITEM_PATH` | JSONPath to response array for per-item correlation mapping | No |
| `SUCCESS_STATUS` | Successful status codes (default: `200,201,202,204`) | No |
| `ERROR_MODE` | Error policy: `FAIL_FAST` or `CONTINUE` (default: `FAIL_FAST`) | No |
| `RETRY_COUNT` | Number of attempts to retry transient failures (default: `0`) | No |
| `RETRY_BACKOFF_MS` | Delay before retry in milliseconds (default: `500`) | No |
| `RETRY_STATUS` | Status codes triggering retries (default: `408,429,500,502,503,504`) | No |
| `IDEMPOTENCY_KEY_COLUMN` | Row column to use as idempotency key header | No |
| `IDEMPOTENCY_HEADER` | Header name for idempotency key (default: `Idempotency-Key`) | No |
| `URL_TEMPLATE` | Dynamic URL template containing `${column_name}` placeholders | No |
| `BODY_TEMPLATE` | Dynamic body template containing `${column_name}` placeholders | No |
| `ERROR_BODY_MAX_CHARS` | Maximum character length of response body saved in errors (default: `4096`) | No |

## Examples

```sql
-- Public GitHub API — array is the root response
CREATE CONNECTION github_issues AS API(URL='https://api.github.com/repos/microsoft/terminal/issues', ROOT_PATH='$');
SELECT title, created_at FROM github_issues;

-- Bearer token authentication
CREATE CONNECTION my_api AS API(URL='https://api.example.com/v1/customers', AUTH_TYPE='BEARER', TOKEN='sk_live_abc123');

-- APIKEY header auth
CREATE CONNECTION weather AS API(URL='https://api.weather.com/data', AUTH_TYPE='APIKEY', TOKEN='my_api_key_value', HEADER_NAME='X-API-Key');

-- Outbound write (INSERT) with ROW_OBJECT (default)
CREATE CONNECTION bed_api AS API(
    URL = 'https://example.org/api/bed-usage', METHOD = 'POST',
    AUTH_TYPE = 'BEARER', TOKEN = @api_token, BODY_MODE = 'ROW_OBJECT',
    RESPONSE_TABLE = '#api_results', RESPONSE_CORRELATION_COLUMNS = 'submission_id,location', RETRY_COUNT = 3
);
INSERT INTO bed_api (submission_id, location, totalBeds, occupiedBeds)
SELECT id, loc_name, total, occupied FROM #bed_data;

-- Outbound batch write with WRAPPED_ARRAY
CREATE CONNECTION bulk_api AS API(
    URL = 'https://example.org/api/bulk', METHOD = 'POST',
    BODY_MODE = 'WRAPPED_ARRAY', BATCH_ROOT = 'submissions', BATCH_SIZE = 100
);
INSERT INTO bulk_api (location, totalBeds) SELECT loc_name, total FROM #bed_data;
```

> [!IMPORTANT]
> **DELETE support & behavior:**
> - `METHOD='DELETE'` is supported for direct API connection queries (sends a single HTTP `DELETE`).
> - The API connector does **not** support direct DML `DELETE FROM api_conn ...`. Because the engine
>   implements a streaming delete for non-database connections (read the dataset, filter deleted rows,
>   write the remaining rows back via `WriteBatches`), a `DELETE` on an API connection would trigger
>   write requests (`POST`/`PUT`/`PATCH`) for all surviving rows.
> - To delete resources via the API, configure a connection with `METHOD='DELETE'` (e.g. with dynamic
>   template params) and execute it directly or via a query block.

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
