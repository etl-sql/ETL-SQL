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
        /// Deletes a temporary file if it exists. Logs at Verbose on failure instead of silently swallowing.
        /// </summary>
        public static void SafeDelete(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Logger.Verbose($"[TempFileHelper] Could not delete temp file '{path}': {ex.Message}");
            }
        }
    }
}
