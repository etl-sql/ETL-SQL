using System;
using System.IO;
using System.Linq;

namespace ETL_SQL.Core.Common
{
    public static class FileConnectorPathHelper
    {
        /// <summary>
        /// Authorizes a file-connector write at its I/O boundary: the local write guardrail (blocks
        /// script files / dangerous types) plus the enterprise <c>FileSystemPolicyAuthorizer</c>. The
        /// base connection path is authorized at CREATE CONNECTION, but a per-use resolution (e.g. a
        /// <c>${placeholder}</c> target, or a connection created before policy tightened) is only
        /// enterprise-checked here. No-op when there is no execution context.
        /// </summary>
        public static void AuthorizeWrite(ETL_SQL.Core.IExecutionContext? context, string filePath)
        {
            if (context == null || string.IsNullOrEmpty(filePath)) return;
            context.SecurityService.ValidateWriteAccess(filePath);
            new ETL_SQL.Core.Governance.FileSystemPolicyAuthorizer(context.SecurityService)
                .Authorize(context, filePath, ETL_SQL.Core.Governance.FileSystemAccessKind.Write,
                    validateFileType: false);
        }

        /// <summary>
        /// Authorizes a file-connector read at its I/O boundary against the enterprise
        /// <c>FileSystemPolicyAuthorizer</c> (approved roots / policy freshness). Defense-in-depth for
        /// per-use path resolution: the base connection path is authorized at CREATE CONNECTION.
        /// No-op when there is no execution context.
        /// </summary>
        public static void AuthorizeRead(ETL_SQL.Core.IExecutionContext? context, string filePath)
        {
            if (context == null || string.IsNullOrEmpty(filePath)) return;
            new ETL_SQL.Core.Governance.FileSystemPolicyAuthorizer(context.SecurityService)
                .Authorize(context, filePath, ETL_SQL.Core.Governance.FileSystemAccessKind.Read,
                    validateFileType: false);
        }

        public static string CoerceFilePathExtension(string path, bool encrypt, bool compress)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // Do not coerce temporary, backup, or staging files used internally by the engine
            if (path.Contains(".tmp", StringComparison.OrdinalIgnoreCase) ||
                path.Contains(".bak", StringComparison.OrdinalIgnoreCase) ||
                path.Contains(".staged", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            // Do not coerce exported dataset files (which end in .etlds)
            if (path.EndsWith(".etlds", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            // Do not coerce portal dataset registry/cache files (which end in _<id>.parquet or _<id>.avro)
            int lastDot = path.LastIndexOf('.');
            if (lastDot > 0)
            {
                string stem = path.Substring(0, lastDot);
                int lastUnderscore = stem.LastIndexOf('_');
                if (lastUnderscore > 0 && lastUnderscore < stem.Length - 1)
                {
                    string idStr = stem.Substring(lastUnderscore + 1);
                    if (int.TryParse(idStr, out _) && (path.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".avro", StringComparison.OrdinalIgnoreCase)))
                    {
                        return path;
                    }
                }
            }

            if (encrypt)
            {
                if (compress)
                {
                    // Encrypted and compressed: must end with .zip
                    if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        if (path.EndsWith(".pgp", StringComparison.OrdinalIgnoreCase))
                            path = path.Substring(0, path.Length - 4);
                        path += ".zip";
                    }
                }
                else
                {
                    // Encrypted only: must end with .pgp
                    if (!path.EndsWith(".pgp", StringComparison.OrdinalIgnoreCase))
                    {
                        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            path = path.Substring(0, path.Length - 4);
                        path += ".pgp";
                    }
                }
            }
            else if (compress)
            {
                // Compressed only: must end with .zip
                if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    if (path.EndsWith(".pgp", StringComparison.OrdinalIgnoreCase))
                        path = path.Substring(0, path.Length - 4);
                    path += ".zip";
                }
            }
            return path;
        }

        public static Stream OpenReadStream(string filePath, EncryptionOptions encryption, bool compress, string defaultExtension)
        {
            var baseStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Stream currentStream = baseStream;

            if (encryption.Enabled)
            {
                currentStream = encryption.DecryptStream(currentStream);
            }

            if (compress && (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                             || encryption.Enabled))
            {
                var archive = new System.IO.Compression.ZipArchive(currentStream, System.IO.Compression.ZipArchiveMode.Read);
                var entry = archive.Entries.FirstOrDefault();
                if (entry != null)
                {
                    var entryStream = entry.Open();
                    return new ChainedStream(entryStream, archive, currentStream, baseStream);
                }
                else
                {
                    archive.Dispose();
                }
            }

            return new ChainedStream(currentStream, baseStream);
        }

        /// <summary>
        /// Returns an engine-owned staging path for a file write. If <paramref name="transactional"/> is true,
        /// the path is forced into the exact same directory as the target <paramref name="targetFilePath"/>
        /// so that the final publication via File.Move is an instantaneous, atomic file-system metadata swap.
        /// If transactional is false, falls back to the OS %TEMP% directory.
        /// </summary>
        public static string GetStagingFilePath(
            ETL_SQL.Core.IExecutionContext? context,
            string targetFilePath,
            bool transactional)
        {
            if (transactional)
            {
                var dir = System.IO.Path.GetDirectoryName(targetFilePath);
                if (string.IsNullOrEmpty(dir)) dir = Environment.CurrentDirectory;
                else System.IO.Directory.CreateDirectory(dir);

                ReconcileStaleStagingFiles(targetFilePath, TimeSpan.FromHours(24));

                var candidate = System.IO.Path.Combine(
                    dir,
                    System.IO.Path.GetFileName(targetFilePath) + ".etl-stage-" + Guid.NewGuid().ToString("N"));
                var resolved = context?.ResolvePath(candidate) ?? System.IO.Path.GetFullPath(candidate);
                EnsureSameDirectory(targetFilePath, resolved);
                return resolved;
            }
            return System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "etlsql-stage-" + Guid.NewGuid().ToString("N"));
        }

        public static string GetStagingFilePath(string targetFilePath, bool transactional) =>
            GetStagingFilePath(null, targetFilePath, transactional);

        /// <summary>
        /// Publishes a completely written staging file. Transactional publication is restricted to
        /// a same-directory rename, and never deletes the prior target before the replacement call.
        /// </summary>
        public static void PublishStagedFile(string stagingFilePath, string targetFilePath, bool transactional)
        {
            if (string.Equals(stagingFilePath, targetFilePath, StringComparison.OrdinalIgnoreCase)) return;
            if (transactional) EnsureSameDirectory(targetFilePath, stagingFilePath);

            var dir = System.IO.Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.Move(stagingFilePath, targetFilePath, overwrite: true);
        }

        /// <summary>
        /// Removes only engine-owned staging files for this exact target after they have exceeded
        /// the supplied age. Fresh stages are left alone so concurrent writers cannot be disrupted.
        /// </summary>
        public static int ReconcileStaleStagingFiles(string targetFilePath, TimeSpan minimumAge)
        {
            var fullTarget = System.IO.Path.GetFullPath(targetFilePath);
            var dir = System.IO.Path.GetDirectoryName(fullTarget);
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return 0;

            var prefix = System.IO.Path.GetFileName(fullTarget) + ".etl-stage-";
            var cutoff = DateTime.UtcNow - minimumAge;
            var removed = 0;
            foreach (var candidate in System.IO.Directory.EnumerateFiles(dir, prefix + "*", SearchOption.TopDirectoryOnly))
            {
                if (System.IO.File.GetLastWriteTimeUtc(candidate) > cutoff) continue;
                try
                {
                    System.IO.File.Delete(candidate);
                    removed++;
                }
                catch (IOException)
                {
                    // A locked stage may still belong to an active writer. Leave it for the next pass.
                }
                catch (UnauthorizedAccessException)
                {
                    // Cleanup is best effort; publication remains available when cleanup is denied.
                }
            }
            return removed;
        }

        private static void EnsureSameDirectory(string targetFilePath, string stagingFilePath)
        {
            var targetDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(targetFilePath));
            var stagingDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(stagingFilePath));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(targetDirectory, stagingDirectory, comparison))
            {
                throw new InvalidOperationException(
                    "Transactional file publication requires the staging file and target to be in the same directory.");
            }
        }
    }
}
