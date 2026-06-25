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
            var text = string.Join("\n", lines);

            if (text.Contains("ENC:"))
            {
                string password = _security.MasterPassword ?? "";
                if (string.IsNullOrEmpty(password))
                {
                    password = await passwordPrompt("Master Password (to decrypt connection strings)", "", true) ?? "";
                    _security.MasterPassword = password;
                }

                if (!string.IsNullOrEmpty(password))
                {
                    text = _security.DecryptScript(text, password);
                    return (text.Split('\n'), filePath);
                }
            }

            return (lines, filePath);
        }

        public async Task<bool> SaveAsync(string filePath, string text, Func<string, string, bool, Task<string?>> passwordPrompt)
        {
            string password = "";
            if (_security.RequiresSavePassword(text))
            {
                password = _security.MasterPassword ?? _security.ExtractLiteralUsePassword(text) ?? "";
                if (string.IsNullOrEmpty(password))
                {
                    password = await passwordPrompt("Master Password (to encrypt script)", "", true) ?? "";
                    _security.MasterPassword = password;
                }
            }

            text = _security.SecureScriptForSave(text, password);

            try
            {
                await Task.Run(() => _fs.WriteAllText(filePath, text));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
