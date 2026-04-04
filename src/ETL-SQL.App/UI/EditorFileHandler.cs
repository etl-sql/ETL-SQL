using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Services;

namespace ETL_SQL.UI
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

            if (!_fs.Exists(filePath))
            {
                return (new[] { "" }, filePath);
            }

            var lines = _fs.ReadAllLines(filePath);
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
            if (_security.NeedsEncryption(text))
            {
                string password = _security.MasterPassword ?? "";
                if (string.IsNullOrEmpty(password))
                {
                    password = await passwordPrompt("Master Password (to encrypt script)", "", true) ?? "";
                    _security.MasterPassword = password;
                }

                if (!string.IsNullOrEmpty(password))
                {
                    text = _security.EncryptScript(text, password);
                }
            }

            try
            {
                _fs.WriteAllText(filePath, text);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
