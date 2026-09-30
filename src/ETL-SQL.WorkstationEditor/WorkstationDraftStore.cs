using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETL_SQL.Reporting.Authoring;

namespace ETL_SQL.WorkstationEditor;

/// <summary>
/// Recovery drafts for the self-installed host: an author's unsaved edits to a workspace file, kept
/// under the OS user's local application data so a crash, a closed browser, or a restarted host
/// loses nothing. The OS profile is the user boundary; the workspace root is part of every key, so
/// two workspaces with a file of the same name never share a draft. Nothing is kept in the browser.
/// </summary>
public sealed class WorkstationDraftStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly WorkstationWorkspace _workspace;
    private readonly string _directory;
    private readonly TimeSpan _retention;

    public WorkstationDraftStore(WorkstationWorkspace workspace, string? directory = null, TimeSpan? retention = null)
    {
        _workspace = workspace;
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETL-SQL", "StudioDrafts");
        _retention = retention ?? TimeSpan.FromDays(7);
    }

    public async Task<WorkstationDraft?> ReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        var file = DraftFile(relativePath);
        if (!File.Exists(file)) return null;
        if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow - _retention)
        {
            File.Delete(file);
            return null;
        }
        await using var stream = File.OpenRead(file);
        return await JsonSerializer.DeserializeAsync<WorkstationDraft>(stream, Json, cancellationToken);
    }

    /// <summary>Keeps the draft, or refuses one that holds a plaintext credential.</summary>
    public async Task<bool> WriteAsync(string relativePath, WorkstationDraft draft, CancellationToken cancellationToken)
    {
        if (StudioDraftPolicy.ContainsPlaintextSecret(draft.Content)) return false;
        var file = DraftFile(relativePath);
        Directory.CreateDirectory(_directory);
        // Written aside and moved, so a crash mid-write leaves the previous draft rather than half of one.
        var staging = file + ".tmp";
        await File.WriteAllTextAsync(staging, JsonSerializer.Serialize(draft with { UpdatedAt = DateTime.UtcNow }, Json), cancellationToken);
        File.Move(staging, file, overwrite: true);
        return true;
    }

    public void Delete(string relativePath)
    {
        var file = DraftFile(relativePath);
        if (File.Exists(file)) File.Delete(file);
    }

    /// <summary>
    /// The draft's file, named by a hash of the workspace root and the resolved script path. The path
    /// is resolved by the workspace first, so a draft can only be kept for a file the host would open.
    /// </summary>
    private string DraftFile(string relativePath)
    {
        var fullPath = _workspace.ResolveEditablePath(relativePath);
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant()));
        return Path.Combine(_directory, Convert.ToHexString(key).ToLowerInvariant() + ".json");
    }
}

public sealed record WorkstationDraft(string Content, string? BaseSourceRevision, DateTime UpdatedAt);
