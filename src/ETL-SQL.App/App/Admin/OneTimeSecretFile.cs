using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.App.Admin;

/// <summary>
/// Reserves a new file before a one-time credential is minted. Existing files are never
/// overwritten, and an unsuccessful operation removes only the empty file this instance created.
/// </summary>
internal sealed class OneTimeSecretFile : IAsyncDisposable
{
    private readonly FileStream stream;
    private bool committed;

    private OneTimeSecretFile(string path, FileStream stream)
    {
        Path = path;
        this.stream = stream;
    }

    public string Path { get; }

    public static OneTimeSecretFile Reserve(string? requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
            throw new AdminCliException(AdminExitCode.ValidationError,
                "--secret-out is required. One-time secrets are never printed to the terminal or JSON output.");

        string path;
        try { path = System.IO.Path.GetFullPath(requestedPath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new AdminCliException(AdminExitCode.ValidationError,
                $"--secret-out is not a valid file path. {ex.Message}");
        }
        var directory = System.IO.Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new AdminCliException(AdminExitCode.ValidationError,
                $"The --secret-out directory does not exist: {directory}");

        FileStream? stream = null;
        var created = false;
        try
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, useAsync: true);
            created = true;
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return new OneTimeSecretFile(path, stream);
        }
        catch (IOException ex)
        {
            stream?.Dispose();
            if (created) TryDelete(path);
            throw new AdminCliException(AdminExitCode.ValidationError,
                created
                    ? $"Cannot secure --secret-out '{path}'. {ex.Message}"
                    : $"Cannot reserve --secret-out '{path}'. Choose a new file path. {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            stream?.Dispose();
            if (created) TryDelete(path);
            throw new AdminCliException(AdminExitCode.ValidationError,
                $"Cannot write --secret-out '{path}'. {ex.Message}");
        }
    }

    public async Task CommitAsync(string secret, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(secret))
            throw new AdminCliException(AdminExitCode.ValidationError,
                "The Portal did not return a one-time secret. The reserved output file was removed.");

        try
        {
            var bytes = Encoding.UTF8.GetBytes(secret);
            await stream.WriteAsync(bytes, ct);
            await stream.FlushAsync(ct);
            stream.Flush(flushToDisk: true);
            committed = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AdminCliException(AdminExitCode.ValidationError,
                $"The Portal minted a secret, but it could not be saved to '{Path}'. " +
                $"Rotate the account secret before use. {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stream.DisposeAsync();
        if (!committed)
        {
            TryDelete(Path);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
