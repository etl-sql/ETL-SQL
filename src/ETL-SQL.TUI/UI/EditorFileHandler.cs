using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Services;

namespace ETL_SQL.TUI.UI
{
    public class EditorFileHandler
    {
        private readonly IFileSystem _fs;
        private readonly SecurityService _security;

        public EditorFileHandler(IFileSystem fs, SecurityService security)
        {
            _fs = fs;
            _security = security;
        }

        public async Task<(string[] lines, string path)> LoadAsync(string filePath, Func<string, string, bool, Task<string?>> passwordPrompt)
        {
            _ = passwordPrompt;
            if (!string.IsNullOrEmpty(filePath)) filePath = filePath.Trim('"', '\'', ' ');

            if (string.IsNullOrEmpty(filePath) || filePath == "untitled.etlsql")
            {
                return (new[] { "" }, "untitled.etlsql");
            }

            bool exists = await Task.Run(() => _fs.Exists(filePath));
            if (!exists)
            {
                return (new[] { "" }, filePath);
            }

            var lines = await Task.Run(() => _fs.ReadAllLines(filePath));
            return (lines, filePath);
        }

        public async Task<bool> SaveAsync(string filePath, string text, Func<string, string, bool, Task<string?>> passwordPrompt)
        {
            var (success, _) = await SaveAndReturnSealedAsync(filePath, text, passwordPrompt);
            return success;
        }

        /// <summary>
        /// Applies save-time secret sealing and writes <paramref name="filePath"/>, returning
        /// both the write outcome and the sealed script text so callers can sync their buffer
        /// and clear undo history.
        /// </summary>
        public async Task<(bool success, string sealedText)> SaveAndReturnSealedAsync(
            string filePath,
            string text,
            Func<string, string, bool, Task<string?>> passwordPrompt)
        {
            string password = "";
            if (_security.RequiresSavePassword(text))
            {
                password = _security.ExtractLiteralUsePassword(text) ?? _security.MasterPassword ?? "";
                if (string.IsNullOrEmpty(password))
                {
                    password = await passwordPrompt("Master Password (to encrypt script)", "", true) ?? "";
                    _security.MasterPassword = password;
                }
            }

            string secured = _security.SecureScriptForSave(text, password);

            try
            {
                await Task.Run(() => _fs.WriteAllText(filePath, secured));
                return (true, secured);
            }
            catch
            {
                return (false, text);
            }
        }
    }
}
