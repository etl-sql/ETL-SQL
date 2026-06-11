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
    /// Handles EXPORT DATASET &amp;name TO '&lt;file&gt;' ENCRYPT = PASSWORD|KEYFILE — produces a portable
    /// copy of the dataset's Parquet re-encrypted with a transport credential supplied at export
    /// (never persisted). The cache is decrypted with the portal at-rest key, then re-encrypted to the
    /// target with the transport credential, so the file can be moved to another machine/portal and
    /// PUBLISHed. Portal mode only; the caller must be able to read the dataset.
    /// </summary>
    public class ExportDatasetStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(ExportDatasetStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ExportDatasetStatement)statement;

            var registry = context is Evaluator e ? e.DatasetRegistry : null;
            if (registry == null)
                throw new ExecutionException(
                    $"EXPORT DATASET '{stmt.DatasetName}' requires portal mode.",
                    null, stmt.Line, stmt.Column);

            // Validate the transport credential up front (it is never read from disk/sidecar).
            if (stmt.EncryptionMode == DatasetEncryptionMode.Password && string.IsNullOrWhiteSpace(stmt.EncryptionPassword))
                throw new ExecutionException(
                    $"EXPORT DATASET '{stmt.DatasetName}': ENCRYPT = PASSWORD requires PASSWORD = '...'.",
                    null, stmt.Line, stmt.Column);
            if (stmt.EncryptionMode == DatasetEncryptionMode.KeyFile && string.IsNullOrWhiteSpace(stmt.KeyFile))
                throw new ExecutionException(
                    $"EXPORT DATASET '{stmt.DatasetName}': ENCRYPT = KEYFILE requires KEYFILE = '...'.",
                    null, stmt.Line, stmt.Column);
            if (stmt.EncryptionMode is not (DatasetEncryptionMode.Password or DatasetEncryptionMode.KeyFile))
                throw new ExecutionException(
                    $"EXPORT DATASET '{stmt.DatasetName}' requires a transport credential: ENCRYPT = PASSWORD or KEYFILE.",
                    null, stmt.Line, stmt.Column);

            var callerCtx = (context as Evaluator)?.DatasetCallerContext ?? "";
            var atRestKey = (context as Evaluator)?.DatasetAtRestKey;

            var existing = await registry.Lookup(stmt.DatasetName, callerCtx);
            if (existing == null)
                throw new ExecutionException(
                    $"EXPORT DATASET '{stmt.DatasetName}': dataset not found in the portal registry.",
                    null, stmt.Line, stmt.Column);
            if (string.IsNullOrWhiteSpace(existing.ParquetFilePath) || !File.Exists(existing.ParquetFilePath))
                throw new ExecutionException(
                    $"EXPORT DATASET '{stmt.DatasetName}': the dataset has not been materialised yet. Ask the owner to refresh it.",
                    null, stmt.Line, stmt.Column);

            var atRest    = new EncryptionOptions(BuildAtRestOptions(atRestKey));
            var transport = new EncryptionOptions(BuildTransportOptions(stmt));
            var targetPath = context.ResolvePath(stmt.TargetPath);

            var tempPlain = Path.Combine(Path.GetTempPath(), $"__ds_export_{Guid.NewGuid():N}.parquet");
            try
            {
                // Decrypt the at-rest cache to a transient plaintext parquet, then re-encrypt to the target.
                atRest.DecryptFile(existing.ParquetFilePath, tempPlain);
                transport.EncryptFile(tempPlain, targetPath);
            }
            finally
            {
                try { if (File.Exists(tempPlain)) File.Delete(tempPlain); } catch { /* best effort */ }
            }

            _logger.Debug("EXPORT DATASET '{Name}': wrote portable file to {Path}.", stmt.DatasetName, targetPath);
            context.Log(
                $"Dataset '{stmt.DatasetName}' exported to '{targetPath}'. The file is encrypted with the " +
                "transport credential you supplied — distribute it out of band; the credential is not stored.",
                ConsoleColor.Yellow);
        }

        private static Dictionary<string, string> BuildAtRestOptions(string? atRestKey) =>
            string.IsNullOrWhiteSpace(atRestKey)
                ? new Dictionary<string, string> { ["ENCRYPT"] = "MACHINE" }
                : new Dictionary<string, string> { ["ENCRYPT"] = "PASSWORD", ["PASSWORD"] = atRestKey };

        private static Dictionary<string, string> BuildTransportOptions(ExportDatasetStatement stmt) =>
            stmt.EncryptionMode == DatasetEncryptionMode.KeyFile
                ? new Dictionary<string, string> { ["ENCRYPT"] = "KEYFILE",  ["KEYFILE"]  = stmt.KeyFile ?? "" }
                : new Dictionary<string, string> { ["ENCRYPT"] = "PASSWORD", ["PASSWORD"] = stmt.EncryptionPassword ?? "" };
    }
}
