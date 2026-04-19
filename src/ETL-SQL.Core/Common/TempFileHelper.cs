using System;
using System.IO;

namespace ETL_SQL.Common
{
    /// <summary>
    /// Utility for safe cleanup of temporary files, logging failures instead of swallowing them.
    /// </summary>
    public static class TempFileHelper
    {
        /// <summary>
        /// Deletes a temporary file if it exists. Logs diagnostic messages on failure.
        /// </summary>
        public static void SafeDelete(string? path, ILogger? logger = null)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    if (logger != null) logger.Debug($"[TempFileHelper] Successfully deleted temp file '{path}'.");
                }
                else if (logger != null)
                {
                    logger.Debug($"[TempFileHelper] SafeDelete skipped: file '{path}' does not exist.");
                }
            }
            catch (Exception ex)
            {
                if (logger != null) logger.Warning($"[TempFileHelper] Could not delete temp file '{path}': {ex.Message}");
            }
        }
    }
}
