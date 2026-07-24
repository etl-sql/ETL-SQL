using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Webhook
{
    /// <summary>
    /// Write-only connector that POSTs each inserted row as a JSON payload to an HTTP(S)
    /// webhook endpoint (Slack / Teams incoming webhooks, or any generic JSON receiver).
    /// The endpoint URL embeds its auth token, so it is treated as a credential end to end:
    /// SECRET:-resolvable, masked in every display surface, and never logged.
    /// </summary>
    public class WebhookConnector : IConnector
    {
        public string Name => "WEBHOOK";
        public IReadOnlyList<string> Aliases => new[] { "SLACK", "TEAMS" };

        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult("HTTP webhook (built-in HttpClient)");

        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "URL", Array.Empty<string>() },
            { "FORMAT", new[] { "SLACK", "TEAMS", "GENERIC" } },
            { "BODY_TEMPLATE", Array.Empty<string>() },
            { "TIMEOUT_SECONDS", Array.Empty<string>() },
            { "RETRY_COUNT", Array.Empty<string>() },
            { "RETRY_BACKOFF_MS", Array.Empty<string>() },
            { "RETRY_STATUS", Array.Empty<string>() },
            { "MAX_REDIRECTS", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "FORMAT", new[] { "SLACK", "TEAMS", "GENERIC" } }
        };

        public string GetHelp()
        {
            return @"Webhook Connector (write-only sink)
------------------------------------
POSTs each inserted row as a JSON payload to an HTTP(S) endpoint.
Works for Slack / Teams incoming webhooks and any generic JSON receiver.

Basic Usage:
  CREATE CONNECTION alerts AS WEBHOOK(URL = 'SECRET:slack_url', FORMAT = 'slack');
  INSERT INTO alerts (Text) VALUES ('Nightly load finished: 1.2M rows.');

Supported Options:
  URL              - The webhook endpoint (required). Accepts 'SECRET:name' references and
                     ${ENV_VAR} placeholders. Treated as a credential: masked in SHOW CONNECTION,
                     logs, and error messages (Slack/Teams URLs embed their auth token).
  FORMAT           - Payload shape: SLACK, TEAMS, or GENERIC. Default: GENERIC.
                     SLACK   -> {""text"": ""...""} from the row's Text column.
                     TEAMS   -> a MessageCard built from the row's Title and Text columns.
                     GENERIC -> the whole row serialized as one JSON object.
                     Rows without a Text column fall back to 'col: value' pairs.
  BODY_TEMPLATE    - Raw request body with ${column} placeholders; overrides FORMAT shaping.
                     Substituted values are JSON-string-escaped so JSON templates stay valid.
  TIMEOUT_SECONDS  - Per-request timeout in seconds (default 30).
  RETRY_COUNT      - Retries per row on retryable HTTP statuses (default 2).
  RETRY_BACKOFF_MS - Base backoff in milliseconds, doubled per attempt (default 500).
                     A Retry-After header, when present, takes precedence (capped at 60s).
  RETRY_STATUS     - Comma-separated statuses that trigger a retry. Default: 408,429.
                     5xx retries are opt-in because the endpoint may already have processed
                     the delivery (webhook posts are not idempotent).
  MAX_REDIRECTS    - Redirect-following cap (default 5).

Security & delivery semantics:
  - Every request and every redirect hop is validated against the egress policy; an ambient
    system proxy cannot bypass it, and DNS-resolved addresses are re-validated at connect time.
  - Only 307/308 redirects are followed (they preserve the POST body). A 301/302/303 fails the
    statement instead of silently converting the delivery to a GET - update the URL.
  - Reads are not supported: SELECT from a webhook connection returns no rows.";
        }

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            return new WebhookDataSource(context, connectionString, options);
        }

        public string BuildConnectionString(Dictionary<string, string> properties)
        {
            return ConnectionStringBuilder.Build("WEBHOOK", properties);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) =>
            Task.FromResult(new[] { "Title", "Text" }.AsEnumerable());
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult(Enumerable.Empty<string>());

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            if (options != null && options.TryGetValue("URL", out var url)
                && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }

            if (Uri.TryCreate(connectionString, UriKind.Absolute, out var connUri)) return connUri.Host;

            return null;
        }
    }
}
