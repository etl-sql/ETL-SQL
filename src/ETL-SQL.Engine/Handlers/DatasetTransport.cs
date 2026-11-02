using System;
using System.IO;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Shared file handling for EXPORT DATASET and PUBLISH DATASET: the transport KEYFILE and the
/// short-lived plaintext copy made while re-encrypting between the at-rest and transport keys.
/// </summary>
internal static class DatasetTransport
{
    /// <summary>
    /// Resolves the script-supplied KEYFILE through the same path policy as any other file read,
    /// so a transport key cannot be taken from a system directory or outside the approved roots.
    /// </summary>
    internal static string AuthorizeKeyFile(IExecutionContext context, string keyFile) =>
        new FileSystemPolicyAuthorizer(context.SecurityService)
            .Authorize(context, context.ResolvePath(keyFile), FileSystemAccessKind.Read, validateFileType: false)
            .CanonicalPath;

    /// <summary>
    /// Returns a path for the decrypted parquet inside a new private directory. On Unix the
    /// directory is created with mode 0700, so other local users cannot read the plaintext
    /// through the shared temp directory while the copy exists.
    /// </summary>
    internal static string CreatePlaintextStagingPath(string prefix)
    {
        var directory = Directory.CreateTempSubdirectory("etlsql-ds-");
        return Path.Combine(directory.FullName, $"{prefix}_{Guid.NewGuid():N}.parquet");
    }

    /// <summary>Deletes the plaintext copy and its private directory. Best effort.</summary>
    internal static void CleanupPlaintextStaging(string path)
    {
        DatasetFileTransaction.Cleanup(path);
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (directory != null && Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort, like DatasetFileTransaction.Cleanup.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
