# WEBHOOK

Write-only sink that POSTs each inserted row as a JSON payload to an HTTP(S) webhook endpoint — Slack and Teams incoming webhooks, or any generic JSON receiver. Reads return no rows.

Aliases: `SLACK`, `TEAMS`

## Syntax

```sql
CREATE CONNECTION alerts AS WEBHOOK(
  URL = 'SECRET:slack_url',
  FORMAT = 'slack'
);
```

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `URL` | Webhook endpoint. Accepts `'SECRET:name'` references and `${ENV_VAR}` placeholders. | Yes |
| `FORMAT` | Payload shape: `SLACK`, `TEAMS`, or `GENERIC` (default: `GENERIC`). | No |
| `BODY_TEMPLATE` | Raw request body with `${column}` placeholders; overrides `FORMAT` shaping. Substituted values are JSON-string-escaped so JSON templates stay valid. | No |
| `TIMEOUT_SECONDS` | Per-request timeout in seconds (default: `30`). | No |
| `RETRY_COUNT` | Retries per row on retryable HTTP statuses (default: `2`). | No |
| `RETRY_BACKOFF_MS` | Base backoff in milliseconds, doubled per attempt (default: `500`). A `Retry-After` header takes precedence, capped at 60s. | No |
| `RETRY_STATUS` | Comma-separated HTTP statuses that trigger a retry (default: `408,429`). 5xx retries are opt-in because the endpoint may have already processed the delivery — webhook posts are not idempotent. | No |
| `MAX_REDIRECTS` | Redirect-following cap (default: `5`). | No |

## Payload Shapes

| `FORMAT` | Payload |
| :--- | :--- |
| `SLACK` | `{"text": "..."}` from the row's `Text` column. |
| `TEAMS` | A MessageCard built from the row's `Title` and `Text` columns. |
| `GENERIC` | The whole row serialized as one JSON object. |

Rows without a `Text` column fall back to `col: value` pairs joined with `;`.

## Security Notes

- **The URL is a credential.** Slack/Teams webhook URLs embed their auth token, so `URL` is `SECRET:`-resolvable for `WEBHOOK`/`SLACK`/`TEAMS` connections and is masked down to scheme + host in `SHOW CONNECTION`, logs, and error messages.
- **Egress policy is enforced on every request** — including every redirect hop — and the connector never uses an ambient system proxy. DNS-resolved addresses are re-validated at connect time.
- **Only 307/308 redirects are followed** (they preserve the POST body). A 301/302/303 fails the statement instead of silently converting the delivery to a body-less GET — update the connection `URL` to the endpoint's new address.

## Examples

```sql
-- Slack alert per row
CREATE CONNECTION alerts AS WEBHOOK(URL = 'SECRET:slack_url', FORMAT = 'slack');
INSERT INTO alerts (Text) VALUES ('Nightly load finished: 1.2M rows.');

-- Teams card with title
CREATE CONNECTION teams_alerts AS TEAMS(URL = 'SECRET:teams_url', FORMAT = 'teams');
INSERT INTO teams_alerts (Title, Text)
SELECT 'Data quality warning', CONCAT(FailedRows, ' rows failed validation')
FROM #dq_summary;

-- Generic JSON receiver with a custom body
CREATE CONNECTION collector AS WEBHOOK(
  URL = 'https://ingest.example.com/events',
  BODY_TEMPLATE = '{"event": "etl_run", "job": "${JobName}", "rows": "${RowCount}"}'
);
INSERT INTO collector (JobName, RowCount) VALUES ('import_csv', 120000);
```

## Troubleshooting

- **`rejected the payload with HTTP 400`** — The payload shape doesn't match what the endpoint expects; check `FORMAT` (Slack requires a `text` property) or supply a `BODY_TEMPLATE`.
- **`redirected with HTTP 301 ... does not preserve the POST body`** — The endpoint moved; update the connection `URL` to the new address.
- **`field 'URL' uses a SECRET: reference...` on another connector** — `SECRET:` on `URL` is resolvable only for `WEBHOOK`/`SLACK`/`TEAMS` connections (and org-designated fields); other connectors treat `URL` as plain metadata.
- **Policy denial on create or on a redirect hop** — The destination host, scheme, or port is outside the organization's egress allowlist; the initial URL and every redirect hop are both validated.

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [API/REST Connector](api.md)
