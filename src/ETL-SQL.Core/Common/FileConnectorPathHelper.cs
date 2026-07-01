using System;
using System.IO;
using System.Linq;

namespace ETL_SQL.Core.Common
{
    public static class FileConnectorPathHelper
    {
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
    }
}
