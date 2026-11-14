using System;
using System.Threading.Tasks;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Data;

/// <summary>
/// Rename on a remote server without losing the destination. FTP and SFTP used to delete an existing
/// destination and then rename, so a missing source or a refused rename destroyed it (ROADMAP RF10).
/// This checks the source first, moves an existing destination aside, renames, and puts the old
/// destination back if the rename fails. The backup is removed only after the rename succeeded.
/// </summary>
public static class RemoteRename
{
    public static async Task RunAsync(
        string connector,
        string source,
        string destination,
        bool overwrite,
        Func<string, Task<bool>> exists,
        Func<string, string, Task> rename,
        Func<string, Task> delete)
    {
        if (!await exists(source).ConfigureAwait(false))
            throw new ExecutionException(
                $"{connector} rename failed: source '{source}' does not exist. The destination was not changed.");

        if (!await exists(destination).ConfigureAwait(false))
        {
            await rename(source, destination).ConfigureAwait(false);
            return;
        }

        if (!overwrite)
            throw new ExecutionException(
                $"{connector} rename failed: destination '{destination}' already exists and OVERWRITE is OFF.");

        var backup = $"{destination}.etlsql-replace-{Guid.NewGuid().ToString("N")[..12]}";
        await rename(destination, backup).ConfigureAwait(false);

        try
        {
            await rename(source, destination).ConfigureAwait(false);
        }
        catch (Exception renameError)
        {
            try
            {
                await rename(backup, destination).ConfigureAwait(false);
            }
            catch (Exception restoreError)
            {
                throw new ExecutionException(
                    $"{connector} rename of '{source}' to '{destination}' failed ({renameError.Message}), and the " +
                    $"previous destination could not be put back ({restoreError.Message}). It is saved as '{backup}'.",
                    new AggregateException(renameError, restoreError));
            }
            throw;
        }

        try
        {
            await delete(backup).ConfigureAwait(false);
        }
        catch (Exception deleteError)
        {
            throw new ExecutionException(
                $"{connector} renamed '{source}' to '{destination}', but the previous destination could not be " +
                $"removed from '{backup}' ({deleteError.Message}). Delete it manually.",
                deleteError);
        }
    }
}
