using System.Text.Json;

namespace ETL_SQL.Portal.Services;

internal static class PortalSettingsFile
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<T>(json)
                ?? throw new JsonException("Settings must contain an object.");
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public static async Task WriteAsync<T>(string path, T settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
