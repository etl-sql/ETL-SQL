using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Governance;

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
/// What a health probe found. The two unhealthy answers are separate because they call for
/// opposite actions: one is a fact about a session that no longer exists, the other is the absence
/// of an answer from one that may.
/// </summary>
public enum StudioSessionHealth
{
    /// <summary>The host answered its lifecycle probe.</summary>
    Healthy,

    /// <summary>
    /// The process is running but did not answer — a timeout, a refused connection, or a non-success
    /// status. Not offered to callers, and never deleted: the session may simply be starting, or
    /// busy.
    /// </summary>
    Unreachable,

    /// <summary>The process is gone. Nothing is recoverable, so the record can be removed.</summary>
    Gone,
}

/// <summary>
/// Persists and discovers local Studio hosts. Records are per instance, so separate projects and
/// explicit same-project instances never share process, port, execution, or filesystem state.
/// </summary>
public sealed class StudioSessionRegistry(string? storageRoot = null, HttpClient? httpClient = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _storageRoot = Path.GetFullPath(storageRoot ?? DefaultStorageRoot());
    private readonly HttpClient _httpClient = httpClient ?? PolicyBoundHttp.CreateClient(timeout: TimeSpan.FromSeconds(2));

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

            if (record is null) continue;

            switch (await CheckHealthAsync(record, cancellationToken))
            {
                case StudioSessionHealth.Healthy:
                    healthy.Add(record);
                    break;
                case StudioSessionHealth.Gone:
                    SafeDelete(path);
                    break;
                default:
                    // Running but silent: not offered, and deliberately not deleted. This used to be
                    // a delete, which turned one slow probe on a busy machine into a live host that
                    // no longer existed as far as the product was concerned.
                    break;
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
            return !IsProcessAlive(record);
        }

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!IsProcessAlive(record))
            {
                Remove(record.InstanceId);
                return true;
            }
            await Task.Delay(100, cancellationToken);
        }
        return false;
    }

    public void Remove(string instanceId) => SafeDelete(RecordPath(instanceId));

    /// <summary>
    /// Whether the session answered its lifecycle probe. Equivalent to
    /// <see cref="CheckHealthAsync"/> returning <see cref="StudioSessionHealth.Healthy"/>.
    ///
    /// <para>Callers deciding whether to <b>remove</b> a record must use
    /// <see cref="CheckHealthAsync"/> instead: this collapses "the process is gone" and "the process
    /// is running but did not answer in two seconds" into the same <c>false</c>, and those call for
    /// opposite actions.</para>
    /// </summary>
    public async Task<bool> IsHealthyAsync(StudioSessionRecord record, CancellationToken cancellationToken = default) =>
        await CheckHealthAsync(record, cancellationToken) == StudioSessionHealth.Healthy;

    /// <summary>
    /// Probes a session and reports which of the three states it is in.
    ///
    /// <para>The distinction that matters is between <see cref="StudioSessionHealth.Gone"/> and
    /// <see cref="StudioSessionHealth.Unreachable"/>. The probe has a two-second timeout, and a
    /// cold host's first request pays JIT and routing warm-up that a loaded machine can push past
    /// it. Treating that timeout as death meant one slow response deleted a live session's record:
    /// the host kept running, kept holding its port, and became undiscoverable, with nothing on
    /// screen to say why. A timeout is "no answer yet", not "gone", and only the second is a fact
    /// solid enough to destroy state on.</para>
    ///
    /// <para>A record left <see cref="StudioSessionHealth.Unreachable"/> is not offered to callers
    /// either; it simply survives, and the next sweep after its process exits removes it.</para>
    /// </summary>
    public async Task<StudioSessionHealth> CheckHealthAsync(
        StudioSessionRecord record,
        CancellationToken cancellationToken = default)
    {
        if (!IsProcessAlive(record)) return StudioSessionHealth.Gone;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, record.BaseUrl + "/api/studio/lifecycle");
            request.Headers.TryAddWithoutValidation(record.Authentication.HeaderName, record.Authentication.Token);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode ? StudioSessionHealth.Healthy : StudioSessionHealth.Unreachable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up, which says nothing about the session. Without this the probe's
            // own timeout and the caller's cancellation are indistinguishable.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StudioSessionHealth.Unreachable;
        }
    }

    private string RecordPath(string instanceId)
    {
        if (!Guid.TryParse(instanceId, out var parsed))
            throw new ArgumentException("Studio instance IDs must be GUIDs.", nameof(instanceId));
        return Path.Combine(_storageRoot, parsed.ToString("N") + ".json");
    }

    /// <summary>
    /// Whether the recorded process is still running <b>and is still the process the record
    /// describes</b>.
    ///
    /// <para>Liveness alone is not identity. Operating systems reuse process IDs freely, so a
    /// record whose session exited can find an unrelated process wearing its number. That did not
    /// matter much while any unanswered probe deleted the record within seconds; now that a running
    /// but silent session is deliberately kept, a PID-only check would let a record — and the
    /// bearer token in it — survive indefinitely pointing at a stranger, and its
    /// <c>StudioUrl</c> would offer that token to whatever now answers on the recorded port.</para>
    ///
    /// <para>The start time settles it. The session had to be running before its record could be
    /// written, so a process that started <em>after</em> <see cref="StudioSessionRecord.StartedAtUtc"/>
    /// cannot be that session. A process whose start time cannot be read is not one we can confirm
    /// is ours either, and is treated the same way — the closed answer, since the consequence of
    /// guessing wrong is handing out a token.</para>
    /// </summary>
    private static bool IsProcessAlive(StudioSessionRecord record)
    {
        try
        {
            using var process = Process.GetProcessById(record.ProcessId);
            if (process.HasExited) return false;
            return process.StartTime.ToUniversalTime() <= record.StartedAtUtc.UtcDateTime;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
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
