using System.Security.Cryptography;
using System.Text;

namespace ETL_SQL.Orchestrator.Execution;

public sealed record ImmutableSandboxArtifact(string ArtifactId, string Hash, string Path);

public interface IImmutableSandboxArtifactStore
{
    Task<ImmutableSandboxArtifact> PutScriptAsync(string script, CancellationToken cancellationToken = default);
    Task StageAsync(
        string artifactId,
        string expectedHash,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

public sealed class ImmutableSandboxArtifactStoreOptions
{
    public required string RootPath { get; init; }
}

/// <summary>
/// Content-addressed, append-only input store for sandbox workloads. Logical job names and caller
/// paths never participate in physical addressing; bytes are rehashed both before publication and
/// after staging into a single-use assignment.
/// </summary>
public sealed class FileSystemImmutableSandboxArtifactStore : IImmutableSandboxArtifactStore
{
    private readonly string _rootPath;

    public FileSystemImmutableSandboxArtifactStore(ImmutableSandboxArtifactStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RootPath);
        if (!Path.IsPathFullyQualified(options.RootPath))
            throw new ArgumentException("The immutable sandbox artifact root must be absolute.", nameof(options));

        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootPath));
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<ImmutableSandboxArtifact> PutScriptAsync(
        string script,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        var bytes = Encoding.UTF8.GetBytes(script);
        var hex = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var artifactId = $"sha256-{hex}";
        var hash = $"sha256:{hex}";
        var directory = Path.Combine(_rootPath, hex[..2]);
        var path = Path.Combine(directory, $"{hex}.etlsql");
        Directory.CreateDirectory(directory);

        if (!File.Exists(path))
        {
            var temporary = Path.Combine(directory, $".{hex}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                VerifyHash(temporary, hash);
                try
                {
                    File.Move(temporary, path, overwrite: false);
                }
                catch (IOException) when (File.Exists(path))
                {
                    // Another writer published the same content. The verification below decides
                    // whether it is the expected immutable object or a collision/tamper event.
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        VerifyHash(path, hash);
        return new ImmutableSandboxArtifact(artifactId, hash, path);
    }

    public async Task StageAsync(
        string artifactId,
        string expectedHash,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var source = ResolveArtifact(artifactId, expectedHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var parent = Path.GetDirectoryName(Path.GetFullPath(destinationPath))
            ?? throw new ArgumentException("The sandbox staging destination has no parent.", nameof(destinationPath));
        Directory.CreateDirectory(parent);
        if (File.Exists(destinationPath))
            throw new IOException("A sandbox artifact staging destination must be unused.");

        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                         81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }

        VerifyHash(destinationPath, expectedHash);
        File.SetAttributes(destinationPath, File.GetAttributes(destinationPath) | FileAttributes.ReadOnly);
    }

    private string ResolveArtifact(string artifactId, string expectedHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        RequireCanonicalHash(expectedHash);
        var hex = expectedHash[7..];
        if (!artifactId.Equals($"sha256-{hex}", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The sandbox artifact id does not match its immutable hash.");

        var path = Path.GetFullPath(Path.Combine(_rootPath, hex[..2], $"{hex}.etlsql"));
        if (!path.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The sandbox artifact path escaped its server-owned root.");
        if (!File.Exists(path))
            throw new FileNotFoundException("The immutable sandbox artifact was not found.");
        VerifyHash(path, expectedHash);
        return path;
    }

    private static void VerifyHash(string path, string expectedHash)
    {
        RequireCanonicalHash(expectedHash);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = $"sha256:{Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()}";
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expectedHash)))
            throw new InvalidDataException("Immutable sandbox artifact bytes do not match the authorized hash.");
    }

    private static void RequireCanonicalHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("sha256:", StringComparison.Ordinal) ||
            value.Length != 71 || value[7..].Any(character =>
                !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("A canonical lowercase sha256 hash is required.", nameof(value));
    }
}
