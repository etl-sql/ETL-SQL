using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ETL_SQL.WorkstationEditor;

public sealed record StudioAuthenticationMetadata(string HeaderName, string Token);

public sealed record StudioSessionRecord(
    string InstanceId,
    string WorkspaceRoot,
    int ProcessId,
    int Port,
    DateTimeOffset StartedAtUtc,
    StudioAuthenticationMetadata Authentication)
{
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public string StudioUrl => $"{BaseUrl}/studio/?token={Uri.EscapeDataString(Authentication.Token)}";
}

/// <summary>
/// Persists and discovers local Studio hosts. Records are per instance, so separate projects and
/// explicit same-project instances never share process, port, execution, or filesystem state.
/// </summary>
public sealed class StudioSessionRegistry(string? storageRoot = null, HttpClient? httpClient = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _storageRoot = Path.GetFullPath(storageRoot ?? DefaultStorageRoot());
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

    public string StorageRoot => _storageRoot;

    public static string NormalizeWorkspace(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public async Task WriteAsync(StudioSessionRecord record, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_storageRoot);
        var normalized = record with { WorkspaceRoot = NormalizeWorkspace(record.WorkspaceRoot) };
        var target = RecordPath(normalized.InstanceId);
        var temporary = target + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)) + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(normalized, JsonOptions), Encoding.UTF8, cancellationToken);
        RestrictToCurrentUser(temporary);
        File.Move(temporary, target, overwrite: true);
        RestrictToCurrentUser(target);
    }

    public async Task<IReadOnlyList<StudioSessionRecord>> ListHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_storageRoot)) return [];
        var healthy = new List<StudioSessionRecord>();
        foreach (var path in Directory.EnumerateFiles(_storageRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            StudioSessionRecord? record = null;
            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken);
                record = JsonSerializer.Deserialize<StudioSessionRecord>(json, JsonOptions);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                SafeDelete(path);
            }

            if (record is not null && await IsHealthyAsync(record, cancellationToken))
            {
                healthy.Add(record);
            }
            else if (record is not null)
            {
                SafeDelete(path);
            }
        }
        return healthy.OrderBy(record => record.StartedAtUtc).ToList();
    }

    public async Task<StudioSessionRecord?> FindWorkspaceAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeWorkspace(workspaceRoot);
        return (await ListHealthyAsync(cancellationToken)).FirstOrDefault(record =>
            string.Equals(record.WorkspaceRoot, normalized, WorkspaceComparison));
    }

    public async Task<StudioSessionRecord?> FindPortAsync(int port, CancellationToken cancellationToken = default) =>
        (await ListHealthyAsync(cancellationToken)).FirstOrDefault(record => record.Port == port);

    public async Task<bool> RequestStopAsync(
        StudioSessionRecord record,
        bool force,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, record.BaseUrl + "/api/studio/shutdown");
            request.Headers.TryAddWithoutValidation(record.Authentication.HeaderName, record.Authentication.Token);
            request.Content = JsonContent.Create(new StudioShutdownRequest(force));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return !IsProcessAlive(record.ProcessId);
        }

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!IsProcessAlive(record.ProcessId))
            {
                Remove(record.InstanceId);
                return true;
            }
            await Task.Delay(100, cancellationToken);
        }
        return false;
    }

    public void Remove(string instanceId) => SafeDelete(RecordPath(instanceId));

    public async Task<bool> IsHealthyAsync(StudioSessionRecord record, CancellationToken cancellationToken = default)
    {
        if (!IsProcessAlive(record.ProcessId)) return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, record.BaseUrl + "/api/studio/lifecycle");
            request.Headers.TryAddWithoutValidation(record.Authentication.HeaderName, record.Authentication.Token);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private string RecordPath(string instanceId)
    {
        if (!Guid.TryParse(instanceId, out var parsed))
            throw new ArgumentException("Studio instance IDs must be GUIDs.", nameof(instanceId));
        return Path.Combine(_storageRoot, parsed.ToString("N") + ".json");
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A concurrent host may be replacing its record. The next discovery pass retries.
        }
    }

    private static void RestrictToCurrentUser(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static StringComparison WorkspaceComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string DefaultStorageRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ETL-SQL",
        "studio",
        "sessions");
}
