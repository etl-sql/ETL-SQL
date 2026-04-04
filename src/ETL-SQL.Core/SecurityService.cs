using System;
using System.Text.RegularExpressions;
using ETL_SQL.Common;

namespace ETL_SQL.Services
{
    public class SecurityService
    {
        private static readonly Regex ConnRegex = new Regex(@"(CREATE\s+CONNECTION\s+\w+\s+ON\s+\w+\s*\(\s*(['""]))([^'""\(\)]+)(\2\s*\))(?:\s+WITH\s*\((.*?)\))?", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex EncRegex = new Regex(@"(['""])ENC:[A-Za-z0-9+/=]*\1", RegexOptions.Compiled);

        public string? MasterPassword { get; set; }

        /// <summary>
        /// Decrypts any encrypted connection strings within a script using a master password.
        /// </summary>
        /// <param name="text">The script content possibly containing encrypted segments.</param>
        /// <param name="password">The master password to use for decryption.</param>
        /// <returns>The script with decrypted connection segments.</returns>
        public string DecryptScript(string text, string password)
        {
            if (string.IsNullOrEmpty(password)) return text;

            return EncRegex.Replace(text, m =>
            {
                try
                {
                    var quote = m.Value[0];
                    var encrypted = m.Value.Trim('\'', '\"');
                    var decrypted = CryptoUtils.Decrypt(encrypted, password);
                    return quote + decrypted + quote;
                }
                catch { return m.Value; }
            });
        }

        /// <summary>
        /// Encrypts any plaintext connection strings within a script using a master password.
        /// </summary>
        /// <param name="text">The plaintext script.</param>
        /// <param name="password">The master password to use for encryption.</param>
        /// <returns>The script with encrypted connection segments.</returns>
        public string EncryptScript(string text, string password)
        {
            if (string.IsNullOrEmpty(password)) return text;

            return ConnRegex.Replace(text, m =>
            {
                var target = m.Groups[3].Value;
                var options = m.Groups[5].Value;
                
                if (target.StartsWith("ENC:") || options.Contains("ENCRYPT=OFF", StringComparison.OrdinalIgnoreCase))
                    return m.Value;

                var encrypted = CryptoUtils.Encrypt(target, password);
                var result = m.Groups[1].Value + encrypted + m.Groups[4].Value;
                if (m.Groups[5].Success) result += " WITH (" + m.Groups[5].Value + ")";
                return result;
            });
        }

        /// <summary>
        /// Checks if a script contains any plaintext connections that should be encrypted.
        /// </summary>
        /// <param name="text">The script to analyze.</param>
        /// <returns>True if at least one plaintext connection is found, otherwise false.</returns>
        public bool NeedsEncryption(string text)
        {
            foreach (Match m in ConnRegex.Matches(text))
            {
                var target = m.Groups[3].Value;
                var options = m.Groups[5].Value;
                if (!target.StartsWith("ENC:") && !options.Contains("ENCRYPT=OFF", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
