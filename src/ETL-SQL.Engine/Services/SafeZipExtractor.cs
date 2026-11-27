using ETL_SQL.Core;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Engine.Services;

/// <summary>Extracts ZIP entries only after each final target passes the shared path authorizer.</summary>
public static class SafeZipExtractor
{
    public static void Extract(
        string archivePath,
        string destinationDirectory,
        bool overwrite,
        IExecutionContext context,
        FileSystemPolicyAuthorizer authorizer)
    {
        SafeArchiveService.ExtractZip(archivePath, destinationDirectory, overwrite, context, authorizer);
    }
}
