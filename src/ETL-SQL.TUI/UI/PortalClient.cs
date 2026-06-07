using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Minimal Report Portal HTTP client mirroring the VS Code publish flow:
    /// login → upload script → list folders → create report.
    /// </summary>
    public sealed class PortalClient
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        public async Task<string?> LoginAsync(string url, string username, string password)
        {
            var (status, json) = await PostAsync($"{url}/api/auth/login", new { username, password }, null);
            if (status != 200) return null;
            return json.ValueKind == JsonValueKind.Object && json.TryGetProperty("token", out var t) ? t.GetString() : null;
        }

        public async Task<(bool ok, string? path, string? error)> UploadScriptAsync(string url, string token, string filename, string contentBase64)
        {
            var (status, json) = await PostAsync($"{url}/api/scripts/upload", new { filename, contentBase64 }, token);
            if (status != 200)
                return (false, null, GetString(json, "error") ?? $"HTTP {status}");
            var path = GetString(json, "path");
            return path != null ? (true, path, null) : (false, null, "missing response path");
        }

        public async Task<List<(int id, string path)>> GetFoldersAsync(string url, string token)
        {
            var result = new List<(int, string)>();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{url}/api/folders");
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                using var resp = await Http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return result;
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                FlattenFolders(doc.RootElement, "", result);
            }
            catch { }
            result.Sort((a, b) => string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public async Task<(int status, string? message)> CreateReportAsync(string url, string token, int folderId, string name, string scriptPath, string description)
        {
            var (status, json) = await PostAsync($"{url}/api/reports", new { folderId, name, scriptPath, description }, token);
            string? msg = GetString(json, "message") ?? GetString(json, "title");
            return (status, msg);
        }

        /// <summary>Flattens the folder tree into (id, "/A/B") rows. Public for tests.</summary>
        public static void FlattenFolders(JsonElement nodes, string prefix, List<(int id, string path)> result)
        {
            var items = nodes.ValueKind == JsonValueKind.Array ? nodes.EnumerateArray() : SingleOrEmpty(nodes);
            foreach (var node in items)
            {
                if (node.ValueKind != JsonValueKind.Object) continue;
                int id = node.TryGetProperty("id", out var i) && i.TryGetInt32(out var iv) ? iv : 0;
                string nm = GetString(node, "name") ?? "";
                string p = prefix.Length > 0 ? $"{prefix}/{nm}" : $"/{nm}";
                result.Add((id, p));
                if (node.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array)
                    FlattenFolders(ch, p, result);
            }
        }

        private static IEnumerable<JsonElement> SingleOrEmpty(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object) yield return e;
        }

        private static string? GetString(JsonElement json, string prop) =>
            json.ValueKind == JsonValueKind.Object && json.TryGetProperty(prop, out var v) ? v.GetString() : null;

        private static async Task<(int status, JsonElement json)> PostAsync(string url, object body, string? token)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            if (token != null) req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            using var resp = await Http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            JsonElement el = default;
            try { if (!string.IsNullOrWhiteSpace(text)) el = JsonDocument.Parse(text).RootElement.Clone(); }
            catch { }
            return ((int)resp.StatusCode, el);
        }
    }
}
