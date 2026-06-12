using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles PUBLISH DATASET FROM '&lt;file&gt;' AS &amp;name [INTO '&lt;folder&gt;'] [ACCESS PUBLIC|PRIVATE]
    /// ENCRYPT = PASSWORD|KEYFILE — imports a portable EXPORTed file into the portal: decrypts once with
    /// the supplied transport credential, re-encrypts with the portal at-rest key, and registers it.
    /// The published copy is at-rest-encrypted (not movable); the author keeps the original export file.
    /// Portal mode only.
    /// </summary>
    public class PublishDatasetStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(PublishDatasetStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (PublishDatasetStatement)statement;

            var registry = context is Evaluator e ? e.DatasetRegistry : null;
            if (registry == null)
                throw new ExecutionException(
                    $"PUBLISH DATASET '{stmt.DatasetName}' requires portal mode.",
                    null, stmt.Line, stmt.Column);

            // Validate the transport credential needed to decrypt the source file.
            if (stmt.EncryptionMode == DatasetEncryptionMode.Password && string.IsNullOrWhiteSpace(stmt.EncryptionPassword))
                throw new ExecutionException(
                    $"PUBLISH DATASET '{stmt.DatasetName}': ENCRYPT = PASSWORD requires PASSWORD = '...'.",
                    null, stmt.Line, stmt.Column);
            if (stmt.EncryptionMode == DatasetEncryptionMode.KeyFile && string.IsNullOrWhiteSpace(stmt.KeyFile))
                throw new ExecutionException(
                    $"PUBLISH DATASET '{stmt.DatasetName}': ENCRYPT = KEYFILE requires KEYFILE = '...'.",
                    null, stmt.Line, stmt.Column);
            if (stmt.EncryptionMode is not (DatasetEncryptionMode.Password or DatasetEncryptionMode.KeyFile))
                throw new ExecutionException(
                    $"PUBLISH DATASET '{stmt.DatasetName}' requires the transport credential the file was exported with: ENCRYPT = PASSWORD or KEYFILE.",
                    null, stmt.Line, stmt.Column);

            if (await registry.Exists(stmt.DatasetName))
                throw new ExecutionException(
                    $"PUBLISH DATASET '{stmt.DatasetName}': a dataset with that global name already exists.",
                    null, stmt.Line, stmt.Column);

            var sourcePath = context.ResolvePath(stmt.SourcePath);
            if (!File.Exists(sourcePath))
                throw new ExecutionException(
                    $"PUBLISH DATASET '{stmt.DatasetName}': source file not found: '{sourcePath}'.",
                    null, stmt.Line, stmt.Column);

            var callerCtx = (context as Evaluator)?.DatasetCallerContext ?? "";
            var atRestKey = (context as Evaluator)?.DatasetAtRestKey;
            var callerUserId = ParseUserId(callerCtx);
            var targetFolder = stmt.TargetFolder ?? "";

            var publishTarget = await registry.AuthorizePublishAsync(targetFolder, callerCtx);
            if (publishTarget == null)
            {
                await registry.AuditPublishAsync(
                    callerUserId,
                    stmt.DatasetName,
                    targetFolder,
                    succeeded: false,
                    "target folder was not found or caller lacks Manage permission");
                throw new ExecutionException(
                    $"PUBLISH DATASET '{stmt.DatasetName}': target folder was not found or the caller lacks Manage permission.",
                    null, stmt.Line, stmt.Column);
            }

            var allocatedRow = false;
            try
            {
                // Register first to allocate the stable Id the at-rest filename is keyed on.
                var metadata = new DatasetMetadata
                {
                    Name = stmt.DatasetName,
                    FolderPath = publishTarget.FolderPath,
                    FolderId = publishTarget.FolderId,
                    ParquetFilePath = "",
                    SourceQuery = "",                        // published snapshots have no source to re-run
                    AccessLevel = stmt.AccessLevel,
                    CreatedBy = publishTarget.OwnerUserId,
                    LastRefresh = DateTime.UtcNow,
                    RowCount = 0                          // unknown for an imported snapshot
                };
                var id = await registry.RegisterOrUpdate(metadata);
                allocatedRow = true;
                var atRestPath = registry.BuildDatasetFilePath(id, stmt.DatasetName);
                using var fileTransaction = DatasetFileTransaction.Create(atRestPath);

                var transport = new EncryptionOptions(BuildTransportOptions(stmt));
                var atRest = new EncryptionOptions(BuildAtRestOptions(atRestKey));

                var tempPlain = Path.Combine(Path.GetTempPath(), $"__ds_publish_{Guid.NewGuid():N}.parquet");
                try
                {
                    // Decrypt the portable file once with the transport credential, then re-encrypt at rest.
                    transport.DecryptFile(sourcePath, tempPlain);
                    atRest.EncryptFile(tempPlain, fileTransaction.StagingPath);
                    fileTransaction.Commit();
                }
                finally
                {
                    DatasetFileTransaction.Cleanup(tempPlain);
                }

                metadata.Id = id;
                metadata.ParquetFilePath = atRestPath;
                await registry.RegisterOrUpdate(metadata);
                fileTransaction.Complete();
            }
            catch
            {
                if (allocatedRow)
                {
                    try
                    {
                        await registry.Delete(stmt.DatasetName);
                    }
                    catch
                    {
                        // Preserve the original publish failure. Startup reconciliation removes
                        // any empty-path row or managed file left by an interrupted rollback.
                    }
                }
                await registry.AuditPublishAsync(
                    callerUserId,
                    stmt.DatasetName,
                    publishTarget.FolderPath,
                    succeeded: false,
                    "publish processing failed");
                throw;
            }

            await registry.AuditPublishAsync(
                callerUserId,
                stmt.DatasetName,
                publishTarget.FolderPath,
                succeeded: true);

            _logger.Debug("PUBLISH DATASET '{Name}': imported from {Path} and re-encrypted at rest.", stmt.DatasetName, sourcePath);
            context.Log(
                $"Dataset '{stmt.DatasetName}' published. The portal copy is encrypted with the portal at-rest key " +
                "and is not movable — keep your original export file if you need to move it again.",
                ConsoleColor.Yellow);
        }

        // Parses "UserId=<n>" from the caller context; admin/system or unparseable → no personal owner.
        private static int? ParseUserId(string callerPermissions)
        {
            foreach (var part in callerPermissions.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length == 2 && kv[0].Equals("UserId", StringComparison.OrdinalIgnoreCase) && int.TryParse(kv[1], out var id))
                    return id;
            }
            return null;
        }

        private static Dictionary<string, string> BuildAtRestOptions(string? atRestKey) =>
            string.IsNullOrWhiteSpace(atRestKey)
                ? new Dictionary<string, string> { ["ENCRYPT"] = "MACHINE" }
                : new Dictionary<string, string> { ["ENCRYPT"] = "PASSWORD", ["PASSWORD"] = atRestKey };

        private static Dictionary<string, string> BuildTransportOptions(PublishDatasetStatement stmt) =>
            stmt.EncryptionMode == DatasetEncryptionMode.KeyFile
                ? new Dictionary<string, string> { ["ENCRYPT"] = "KEYFILE", ["KEYFILE"] = stmt.KeyFile ?? "" }
                : new Dictionary<string, string> { ["ENCRYPT"] = "PASSWORD", ["PASSWORD"] = stmt.EncryptionPassword ?? "" };
    }
}
