# ETL-SQL Development TODO List
## v0.10.0 work

### API connector production outbound support

#### Current problem

The `API` / `REST` connector is useful as an authenticated request/response source, but it is not production-ready as an ETL sink.

Current behavior:
- `CREATE CONNECTION ... AS API(...)` works.
- Auth currently supports `NONE`, `BASIC`, `BEARER`, and `APIKEY`.
- `ReadBatches()` can call an endpoint with connection-level `METHOD` and static `BODY`.
- Static `POST` / `PUT` request bodies are possible through connection options.
- JSON responses can be parsed into row batches.

Current gap:
- `RestDataSource.WriteBatches(...)` throws `NotSupportedException`.
- Therefore `INSERT INTO api_conn (...) SELECT ...` cannot submit rows to an API.
- Docs/help currently imply INSERT/EXECUTE support that the implementation does not actually provide.
- A `SELECT` that triggers a `POST` is surprising and not a good primary outbound ETL pattern.

Primary goal:
- Make `INSERT INTO api_conn (...) SELECT ...` the natural way to submit data to REST APIs.

Example target script:

```sql
DECLARE @api_token STRING = 'ENC:...';

CREATE CONNECTION bed_api AS API(
    URL = 'https://example.org/api/bed-usage',
    METHOD = 'POST',
    AUTH_TYPE = 'BEARER',
    TOKEN = @api_token,
    BODY_MODE = 'ROW_OBJECT',
    RESPONSE_TABLE = '#bed_api_results',
    RESPONSE_CORRELATION_COLUMNS = 'submission_id,location',
    SUCCESS_STATUS = '200,201,202,204',
    RETRY_COUNT = 3,
    RETRY_BACKOFF_MS = 500
);

INSERT INTO bed_api (submission_id, location, totalBeds, occupiedBeds)
SELECT submission_id, location, total_beds, occupied_beds
FROM #bed_usage;

SELECT *
FROM #bed_api_results
WHERE success = FALSE;
```

#### Design decision: response table first

Use `RESPONSE_TABLE = '#temp_name'` as the primary response-capture mechanism.

Rationale:
- API submission results are operational rowsets: one submitted row or batch produces status, response body, duration, retry count, and error state.
- Temp tables are easy to query, join back to source rows, retry, persist, export, and audit.
- This fits ETL-SQL's engine-side staged data model better than one large JSON variable.
- A JSON variable is useful for a single response document, but it is awkward for hundreds or thousands of row-level submissions.

Recommended compromise:
- Store one response row per API call in a temp table.
- Include a `response_body` column containing raw JSON/text.
- Users can still use `JSON_VALUE`, `JSON_QUERY`, `OPENJSON`, or `JSON_TABLE` against `response_body`.

Do not start with `DECLARE @results JSON` as the main capture model. It can be added later as a convenience for single-call workflows, but the production path should be tabular.

Long-term syntax to consider later:

```sql
INSERT INTO bed_api (submission_id, location, totalBeds, occupiedBeds)
OUTPUT RESPONSE INTO #bed_api_results
SELECT submission_id, location, total_beds, occupied_beds
FROM #bed_usage;
```

That is cleaner than a connection option, but it requires parser/evaluator work. For the first implementation, use `RESPONSE_TABLE`.

#### New connection options

Add these supported options to `RestConnector.GetSupportedOptions()` and docs/help:

| Option | Values / type | Purpose |
| :--- | :--- | :--- |
| `BODY_MODE` | `ROW_OBJECT`, `ROW_ARRAY`, `WRAPPED_ARRAY` | Controls how inserted rows become request JSON. Default should be `ROW_OBJECT` for writes. |
| `BATCH_SIZE` | Integer | Number of rows per HTTP request for batch modes. Default can use incoming batch size or 1 for `ROW_OBJECT`. |
| `BATCH_ROOT` | String | Wrapper property name for `WRAPPED_ARRAY`, e.g. `submissions`. |
| `RESPONSE_TABLE` | Temp table name | Optional `#temp` table to receive API response rows. |
| `RESPONSE_CORRELATION_COLUMNS` | Comma-separated column list | Source columns copied into `RESPONSE_TABLE` for joins/retry. |
| `SUCCESS_STATUS` | Comma-separated HTTP status codes | Status codes considered success. Default: `200,201,202,204`. |
| `ERROR_MODE` | `FAIL_FAST`, `CONTINUE` | `FAIL_FAST` throws on first failed request. `CONTINUE` records failures and completes. Default: `FAIL_FAST`. |
| `RETRY_COUNT` | Integer | Number of retries for transient failures. Default: `0` or conservative `3`; pick one and document it. |
| `RETRY_BACKOFF_MS` | Integer | Base delay before retry. Default: `500`. |
| `RETRY_STATUS` | Comma-separated HTTP status codes | Default: `408,429,500,502,503,504`. |
| `IDEMPOTENCY_KEY_COLUMN` | Column name | Row column used as idempotency key for retries. |
| `IDEMPOTENCY_HEADER` | Header name | Header to receive idempotency key. Default: `Idempotency-Key`. |
| `URL_TEMPLATE` | String | Later phase. Per-row URL with `${column}` placeholders. |
| `BODY_TEMPLATE` | String | Later phase. Custom JSON body with `${column}` placeholders. |
| `ERROR_BODY_MAX_CHARS` | Integer | Max response/error body text retained in errors/response table. Default: `4096`. |

Keep existing options:
- `URL`
- `METHOD`
- `AUTH_TYPE`
- `USER`
- `PASSWORD`
- `TOKEN`
- `HEADER_NAME`
- `ROOT_PATH`
- `BODY`
- `BODY_CONTENT_TYPE`
- `TIMEOUT_SECONDS`
- `HEADER_*`

Also align docs/help around `PATCH` support. The help currently mentions `PATCH`, but `RestConnector.GetSupportedOptions()` only lists `GET`, `POST`, `PUT`, `DELETE`. Either implement `PATCH` or remove it from docs. Recommendation: implement `PATCH`.

#### Body modes

##### `ROW_OBJECT`

Default for `INSERT INTO api_conn`.

Each inserted row becomes one HTTP request body.

Input:

```sql
INSERT INTO bed_api (location, totalBeds, occupiedBeds)
VALUES ('ICU', 24, 19);
```

Request body:

```json
{
  "location": "ICU",
  "totalBeds": 24,
  "occupiedBeds": 19
}
```

Behavior:
- One request per row.
- Good for APIs that do not support bulk submission.
- Easy to correlate each response to one input row.
- If `IDEMPOTENCY_KEY_COLUMN` is set, send that row value as a header.

##### `ROW_ARRAY`

Each connector write batch becomes one HTTP request containing a JSON array.

Connection:

```sql
CREATE CONNECTION bed_api AS API(
    URL = 'https://example.org/api/bed-usage/bulk',
    METHOD = 'POST',
    BODY_MODE = 'ROW_ARRAY',
    BATCH_SIZE = 500
);
```

Request body:

```json
[
  { "location": "ICU", "totalBeds": 24, "occupiedBeds": 19 },
  { "location": "ER", "totalBeds": 16, "occupiedBeds": 12 }
]
```

Behavior:
- One response row per HTTP batch.
- Correlation columns are harder because the response covers many source rows.
- `RESPONSE_TABLE` should include `batch_index`, `request_index`, and maybe `row_count`.

##### `WRAPPED_ARRAY`

Same as `ROW_ARRAY`, but wraps the array in an envelope.

Connection:

```sql
CREATE CONNECTION bed_api AS API(
    URL = 'https://example.org/api/bed-usage/bulk',
    METHOD = 'POST',
    BODY_MODE = 'WRAPPED_ARRAY',
    BATCH_ROOT = 'submissions',
    BATCH_SIZE = 500
);
```

Request body:

```json
{
  "submissions": [
    { "location": "ICU", "totalBeds": 24, "occupiedBeds": 19 }
  ]
}
```

Require `BATCH_ROOT` for `WRAPPED_ARRAY`; fail clearly if missing.

##### `BODY_TEMPLATE`

Defer to a later phase unless needed immediately.

Connection:

```sql
CREATE CONNECTION bed_api AS API(
    URL = 'https://example.org/api/submit',
    METHOD = 'POST',
    BODY_MODE = 'TEMPLATE',
    BODY_TEMPLATE = '{"facility":"${location}","beds":{"total":${totalBeds},"occupied":${occupiedBeds}}}'
);
```

Important:
- This is powerful but easy to misuse.
- Prefer structured row serialization first.
- If implemented, missing columns must throw clear sanitized `ExecutionException`s.

#### Response table schema

When `RESPONSE_TABLE` is set, create or append to that temp table from inside the engine context.

Preferred columns:

| Column | Type | Notes |
| :--- | :--- | :--- |
| `request_index` | INT | Sequential request number within the write operation. |
| `batch_index` | INT | Source batch index. |
| `source_row_index` | INT | Row index when one request maps to one row; nullable for batch modes. |
| `success` | BOOL | Whether status code is in `SUCCESS_STATUS`. |
| `status_code` | INT | HTTP status code, nullable for transport failures. |
| `method` | STRING | HTTP method. |
| `url` | STRING | Target URL without secret query values if any redaction rules apply. |
| `retry_count` | INT | Number of retries performed before final result. |
| `duration_ms` | INT | Duration of final request or full retry sequence; choose one and document it. |
| `row_count` | INT | Number of source rows represented by this request. |
| `response_body` | JSON or STRING | Raw response body. Prefer JSON type if supported cleanly; string is acceptable because JSON functions already accept strings. |
| `error_message` | STRING | Sanitized error message. |
| correlation columns | Original type or STRING | Columns named by `RESPONSE_CORRELATION_COLUMNS`, copied from source rows when one request maps to one source row. |

For batch modes, correlation columns may not map cleanly. Options:
- Leave correlation columns null for batch modes.
- Store a `correlation_json` column containing an array of correlation values.
- Defer row-level batch response correlation until a later phase.

Recommendation for first implementation:
- Fully support correlation columns for `ROW_OBJECT`.
- For `ROW_ARRAY` and `WRAPPED_ARRAY`, record `row_count` and raw response. Do not promise row-level correlation unless the API response format is known.

Temp table creation detail:
- Prefer creating the temp table automatically if it does not exist.
- If it exists, validate required columns or append compatible rows.
- Be explicit and deterministic about column types.
- Avoid overwriting user data silently.

Potential engine integration issue:
- `RestDataSource` currently only has `IExecutionContext`; check whether it can access the temp table/data source manager directly.
- If connectors should not mutate engine temp tables directly, implement a small engine-side response sink contract rather than reaching through internals.
- Keep ownership clean: connector produces response records; engine context stores them.

#### Error behavior

Default should be `ERROR_MODE = FAIL_FAST`.

Behavior:
- If a response status is not in `SUCCESS_STATUS`, write the response row if `RESPONSE_TABLE` is configured.
- Then throw `ExecutionException` in `FAIL_FAST`.
- In `CONTINUE`, keep submitting remaining rows/batches, capture all failures, and throw at the end only if product decision says to fail the statement. Recommendation: `CONTINUE` should complete without throwing, with failures visible in `RESPONSE_TABLE` and summary logs.

Never include secrets in exception text:
- No `TOKEN`
- No `PASSWORD`
- No `Authorization` header
- No API key header values
- No full request body by default if it may contain sensitive data

Use `ERROR_BODY_MAX_CHARS` to cap response body retained in `error_message` or `response_body`.

#### Retry behavior

Transient retry defaults:
- `RETRY_STATUS = '408,429,500,502,503,504'`
- `RETRY_COUNT = 0` or `3`; if defaulting to `3`, be explicit about idempotency risk.
- `RETRY_BACKOFF_MS = 500`

Recommendation:
- Start with `RETRY_COUNT = 0` by default to avoid accidental duplicate submissions.
- Encourage users to configure `IDEMPOTENCY_KEY_COLUMN` before enabling retries for non-idempotent endpoints.

Idempotency:

```sql
CREATE CONNECTION bed_api AS API(
    URL = 'https://example.org/api/bed-usage',
    METHOD = 'POST',
    BODY_MODE = 'ROW_OBJECT',
    IDEMPOTENCY_KEY_COLUMN = 'submission_id',
    IDEMPOTENCY_HEADER = 'Idempotency-Key',
    RETRY_COUNT = 3
);
```

For `ROW_OBJECT`, set the idempotency header per row.

For batch modes:
- Either disallow `IDEMPOTENCY_KEY_COLUMN`, or derive a batch key deterministically from the batch correlation values.
- Recommendation for first implementation: support idempotency only in `ROW_OBJECT`.

#### WHAT_IF behavior

The API write path must respect `SET WHAT_IF ON`.

When `WHAT_IF` is on:
- Do not send HTTP requests.
- Validate connection options.
- Validate `BODY_MODE`.
- Validate `BATCH_ROOT` for `WRAPPED_ARRAY`.
- Validate requested correlation columns exist.
- Count rows and expected request count.
- Populate `RESPONSE_TABLE` only if doing so is already an established WHAT_IF pattern; otherwise log/print a sanitized summary.

Recommended WHAT_IF summary:
- method
- host
- path
- body mode
- source row count
- expected HTTP request count
- batch size
- redacted headers
- sample payload with redaction, or omit payload by default

#### Security and zero-trust requirements

Follow connector standards:
- Option keys must be uppercase with underscores.
- `PASSWORD` only; do not add `PWD`.
- Do not leak secrets through `ConnectionString`, exceptions, logs, response table, or WHAT_IF output.
- Continue calling `context.SecurityService.ValidateHost(new Uri(url).Host)`.
- Sanitize provider exceptions through `ExecutionException`.
- Use async HTTP methods with cancellation tokens.
- Respect `TIMEOUT_SECONDS`.

Additional API-specific redaction:
- Treat these option/header names as sensitive:
  - `TOKEN`
  - `PASSWORD`
  - `CLIENT_SECRET` if OAuth is later added
  - `AUTHORIZATION`
  - Any header name containing `KEY`, `TOKEN`, `SECRET`, or `PASSWORD`
- Redact sensitive query-string parameters in URLs written to `RESPONSE_TABLE`.

#### Implementation phases

##### Phase 1: Core outbound write path

Files likely involved:
- `src/ETL-SQL.Connectors/Rest/RestDataSource.cs`
- `src/ETL-SQL.Connectors/Rest/RestConnector.cs`
- Engine/context temp table support if needed for `RESPONSE_TABLE`
- `src/ETL-SQL.Core/Resources/Help/Connectors/API.md`
- `Docs/Reference/Data_Connectors.md`
- `Docs/Syntax_Index.md`
- `tests/ETL-SQL.Tests/Integration/Connectors/RestApiTests.cs`

Implement:
- `RestDataSource.WriteBatches(...)`.
- `BODY_MODE = ROW_OBJECT`.
- `BODY_MODE = ROW_ARRAY`.
- `BODY_MODE = WRAPPED_ARRAY`.
- `BATCH_SIZE`.
- `BATCH_ROOT`.
- `SUCCESS_STATUS`.
- `ERROR_MODE = FAIL_FAST | CONTINUE`.
- Basic `RESPONSE_TABLE` capture.
- `RESPONSE_CORRELATION_COLUMNS` for `ROW_OBJECT`.
- `PATCH` support or remove docs mention.

Acceptance tests:
- `INSERT INTO api_conn` posts a single row as JSON object.
- Multiple inserted rows in `ROW_OBJECT` produce multiple POST requests.
- `ROW_ARRAY` posts a JSON array.
- `WRAPPED_ARRAY` posts `{ "<BATCH_ROOT>": [...] }`.
- Missing `BATCH_ROOT` for `WRAPPED_ARRAY` throws `ExecutionException`.
- HTTP 201 is success by default.
- HTTP 400 fails in `FAIL_FAST`.
- HTTP 400 records a response row before throwing when `RESPONSE_TABLE` is configured.
- `ERROR_MODE = CONTINUE` records failures and continues.
- `RESPONSE_TABLE` includes `success`, `status_code`, `response_body`, and configured correlation columns.
- Auth headers still work on write.
- Secrets do not appear in exception text.

##### Phase 2: Retry and idempotency

Implement:
- `RETRY_COUNT`.
- `RETRY_BACKOFF_MS`.
- `RETRY_STATUS`.
- `IDEMPOTENCY_KEY_COLUMN`.
- `IDEMPOTENCY_HEADER`.

Acceptance tests:
- 429 then 201 retries and succeeds.
- 500 retries configured number of times.
- 400 does not retry by default.
- Idempotency header is sent from row column.
- Missing idempotency column throws clear error.
- Retry count is written to response table.

##### Phase 3: URL and body templates

Implement only after Phase 1/2 are stable.

Options:
- `URL_TEMPLATE`
- `BODY_MODE = TEMPLATE`
- `BODY_TEMPLATE`

Acceptance tests:
- `URL_TEMPLATE = 'https://example.org/locations/${location}/bed-usage'` substitutes and URL-encodes row values.
- Missing template column throws `ExecutionException`.
- Null template values are handled deterministically.
- `BODY_TEMPLATE` produces valid JSON and fails clearly for invalid JSON.

##### Phase 4: OAuth2 client credentials

Implement only when there is a real target API requiring it.

Options:
- `AUTH_TYPE = 'OAUTH2_CLIENT_CREDENTIALS'`
- `TOKEN_URL`
- `CLIENT_ID`
- `CLIENT_SECRET`
- `SCOPE`

Requirements:
- Cache token per connection until expiry.
- Refresh automatically.
- Never expose client secret or access token in logs/errors/config output.

#### Documentation updates

Update all API docs so they match implementation exactly:
- `Docs/Reference/Data_Connectors.md`
- `src/ETL-SQL.Core/Resources/Help/Connectors/API.md`
- `Docs/Syntax_Index.md`
- `Docs/Cookbook.md` with a full outbound API recipe

Docs must include:
- API as source with `SELECT`.
- API as sink with `INSERT`.
- Bed usage example.
- Row-object POST.
- Batch POST.
- Wrapped batch POST.
- Response table handling.
- Retrying failed submissions.
- WHAT_IF validation.
- Security/redaction notes.

Remove or correct any stale claim that `INSERT`/`EXECUTE` works before it is implemented.

#### Cookbook recipe target

Add a self-contained recipe similar to:

```sql
DECLARE @api_token STRING = 'ENC:...';

CREATE TABLE #bed_usage (
    submission_id VARCHAR,
    location VARCHAR,
    total_beds INT,
    occupied_beds INT
);

INSERT INTO #bed_usage VALUES
    ('sub-001', 'ICU', 24, 19),
    ('sub-002', 'ER', 16, 12);

CREATE CONNECTION bed_api AS API(
    URL = 'https://example.org/api/bed-usage',
    METHOD = 'POST',
    AUTH_TYPE = 'BEARER',
    TOKEN = @api_token,
    BODY_MODE = 'ROW_OBJECT',
    RESPONSE_TABLE = '#bed_api_results',
    RESPONSE_CORRELATION_COLUMNS = 'submission_id,location',
    SUCCESS_STATUS = '200,201,202,204',
    ERROR_MODE = 'CONTINUE',
    IDEMPOTENCY_KEY_COLUMN = 'submission_id',
    RETRY_COUNT = 3,
    RETRY_BACKOFF_MS = 500
);

SET WHAT_IF ON;
INSERT INTO bed_api (submission_id, location, totalBeds, occupiedBeds)
SELECT submission_id, location, total_beds, occupied_beds
FROM #bed_usage;
SET WHAT_IF OFF;

INSERT INTO bed_api (submission_id, location, totalBeds, occupiedBeds)
SELECT submission_id, location, total_beds, occupied_beds
FROM #bed_usage;

SELECT
    submission_id,
    location,
    status_code,
    JSON_VALUE(response_body, '$.error.code') AS error_code,
    JSON_VALUE(response_body, '$.error.message') AS error_message
FROM #bed_api_results
WHERE success = FALSE;
```

#### Open design questions before coding

1. Should `RESPONSE_TABLE` be a connection option only in Phase 1, or should we add parser support for `OUTPUT RESPONSE INTO #table` immediately?
   - Recommendation: start with connection option.

2. Should `ERROR_MODE = CONTINUE` throw at the end if any rows failed?
   - Recommendation: no, because the failure rows are the control surface. Users can decide what to do with `#api_results`.

3. Should `RESPONSE_TABLE` auto-create or require `CREATE TABLE #api_results (...)` up front?
   - Recommendation: auto-create. If already exists, validate compatibility.

4. Should response bodies be `JSON` or `STRING`?
   - Recommendation: store as `STRING` initially unless JSON type handling is already clean in temp tables. JSON functions accept strings, and this avoids failures for non-JSON error bodies.

5. Should `ROW_OBJECT` ignore nulls or include them as JSON null?
   - Recommendation: include nulls as JSON null by default. Consider `OMIT_NULLS = TRUE` later.

6. Should GET with `INSERT INTO` be allowed?
   - Recommendation: no. `WriteBatches()` should require `POST`, `PUT`, or `PATCH` initially. `DELETE` with row-driven URL templates can come later.

7. Should connection-level static `BODY` remain?
   - Recommendation: yes for `ReadBatches()` / one-off request compatibility, but `WriteBatches()` should prefer row serialization and reject ambiguous combinations like `BODY` plus `BODY_MODE = ROW_OBJECT` unless `BODY_MODE = TEMPLATE`.

#### Final target acceptance

The API connector should be considered useful when this workflow is supported end-to-end:
- Stage bed usage rows in `#bed_usage`.
- `INSERT INTO bed_api (...) SELECT ...` sends authenticated POST requests.
- Responses are captured in `#bed_api_results`.
- Failed rows can be queried and retried.
- WHAT_IF validates request count without sending.
- Transient retry can be enabled with idempotency keys.
- Docs and help accurately describe what works.
