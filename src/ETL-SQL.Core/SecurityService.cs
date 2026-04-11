using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ETL_SQL.Common;

namespace ETL_SQL.Services
{
    public class SecurityService
    {
        private static readonly Regex ConnRegex = new Regex(@"(CREATE\s+CONNECTION\s+\w+\s+ON\s+\w+\s*\(\s*(['""]))([^'""\(\)]+)(\2\s*\))(?:\s+WITH\s*\((.*?)\))?", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex EncRegex = new Regex(@"(['""])ENC:[A-Za-z0-9+/=]*\1", RegexOptions.Compiled);

        private static readonly string[] AllowedExtensions = { ".csv", ".json", ".parquet", ".txt", ".sql", ".log", ".xlsx", ".xml", ".yaml", ".yml", ".ini", ".md", ".zip" };
        private static readonly string[] BlockedExtensions = { ".dll", ".exe", ".bat", ".cmd", ".sh", ".msi", ".sys", ".com", ".pfx", ".cer" };
        private static readonly string[] BlockedDirectories = { ".git", ".vscode", ".idea", "node_modules", "bin", "obj", "System32", "Windows" };

        public const int DefaultMaxFileOperations = 100;
        public const int DefaultMaxRecursiveDepth = 5;

        public string? MasterPassword { get; set; }

        /// <summary>
        /// Validates that a path is safe to access. Checks for root access, protected directories, and system paths.
        /// </summary>
        public void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);

            // 1. Block root directory access (e.g., C:\ or /)
            if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException($"Unauthorized access to root directory: {fullPath}");
            }

            // 2. Block access to system/protected directories
            var segments = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var blocked in BlockedDirectories)
            {
                if (segments.Any(s => string.Equals(s, blocked, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new SecurityException($"Unauthorized access to protected system directory: {blocked} in path {fullPath}");
                }
            }

            // 3. Block access to Windows/System32 specifically on Windows
            if (fullPath.Contains("\\Windows\\System32", StringComparison.OrdinalIgnoreCase) || 
                fullPath.Contains("\\Windows\\SysWOW64", StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException("Unauthorized access to Windows System directories.");
            }
        }

        /// <summary>
        /// Validates that a file's extension is safe to process.
        /// </summary>
        public void ValidateFileType(string path, bool allowUnknown = false)
        {
            if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path)) return;

            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) return;

            // 1. Check blacklist (highest priority)
            if (BlockedExtensions.Contains(ext))
            {
                throw new SecurityException($"Access denied to dangerous file type: {ext}");
            }

            // 2. Check whitelist
            if (!AllowedExtensions.Contains(ext) && !allowUnknown)
            {
                throw new SecurityException($"File type '{ext}' is not in the allowed data-connector whitelist. Use ### ALLOW_FILE_TYPE_ACCESS override if necessary.");
            }
        }

        /// <summary>
        /// Checks if an operation count or recursion depth exceeds the allowed limits.
        /// </summary>
        public void CheckRunawayProtection(int count, int depth, bool allowLargeCount = false, bool allowDeepRecursion = false)
        {
            if (count > DefaultMaxFileOperations && !allowLargeCount)
            {
                throw new SecurityException($"Runaway protection: File operation count ({count}) exceeds the safety limit of {DefaultMaxFileOperations}. Use ### ALLOW_GREATER_THAN_100_FILE override.");
            }

            if (depth > DefaultMaxRecursiveDepth && !allowDeepRecursion)
            {
                throw new SecurityException($"Runaway protection: Recursive operation depth ({depth}) exceeds the safety limit of {DefaultMaxRecursiveDepth}. Use ### ALLOW_RECURSIVE_GREATER_THAN_5_LAYERS override.");
            }
        }

        /// <summary>
        /// Decrypts any encrypted connection strings within a script using a master password.
        /// </summary>
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

    public class SecurityException : Exception
    {
        public SecurityException(string message) : base(message) { }
    }
}
