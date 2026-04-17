using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using ETL_SQL.Common;

namespace ETL_SQL.Services
{
    public class SecurityService
    {
        private readonly ILogger _logger;
        private static readonly Regex ConnRegex = new Regex(@"(CREATE\s+CONNECTION\s+\w+\s+ON\s+\w+\s*\(\s*(['""]))([^'""\(\)]+)(\2\s*\))(?:\s+WITH\s*\((.*?)\))?", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex EncRegex = new Regex(@"(['""])ENC:[A-Za-z0-9+/=]*\1", RegexOptions.Compiled);

        public SecurityService(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Centralized method to update security settings from the 'Security' section of the application configuration.
        /// </summary>
        public void UpdateFromConfiguration(IConfiguration configuration)
        {
            var section = configuration.GetSection("Security");
            if (!section.Exists()) return;

            // 1. Allowed Hosts (Egress Control)
            var hosts = section.GetSection("AllowedHosts").Get<string[]>();
            if (hosts != null && hosts.Length > 0)
            {
                AllowedHosts.Clear();
                AllowedHosts.UnionWith(hosts);
                _logger.Info("Security: Loaded {Count} allowed hosts.", hosts.Length);
            }

            // 2. Approved Safe Zones (Guardrail Bypass Zones)
            var zones = section.GetSection("ApprovedSafeZones").Get<string[]>();
            if (zones != null && zones.Length > 0)
            {
                ApprovedSafeZones.Clear();
                ApprovedSafeZones.AddRange(zones);
                _logger.Info("Security: Loaded {Count} approved safe zones.", zones.Length);
            }

            // 3. Allowed Environment Variables (ENV() Whitelist)
            var envVars = section.GetSection("AllowedEnvVars").Get<string[]>();
            if (envVars != null && envVars.Length > 0)
            {
                AllowedEnvVars.Clear();
                AllowedEnvVars.UnionWith(envVars);
                _logger.Info("Security: Loaded {Count} authorized environment variables.", envVars.Length);
            }

            // 4. Runaway Protection Limits
            MaxFileOperations = int.TryParse(section["MaxFileOperationsPerScript"], out var mfo) ? mfo : DefaultMaxFileOperations;
            MaxRecursiveDepth = int.TryParse(section["MaxRecursiveNestingDepth"], out var mrd) ? mrd : DefaultMaxRecursiveDepth;
        }

        private static readonly string[] AllowedExtensions = { ".csv", ".json", ".parquet", ".avro", ".db", ".enc", ".gz", ".7z", ".txt", ".sql", ".log", ".xlsx", ".xml", ".yaml", ".yml", ".ini", ".md", ".zip" };
        private static readonly string[] BlockedExtensions = { ".dll", ".exe", ".bat", ".cmd", ".sh", ".msi", ".sys", ".com", ".pfx", ".cer" };
        
        // Final guardrail: Scripts cannot edit other scripts (Human-Authoring Only)
        private static readonly string[] BlockedWriteExtensions = { ".etlsql", ".rptsql", ".sql", ".etls", ".py", ".js", ".sh", ".bat", ".cmd" };

        // Comprehensive System Lockdown (Windows & Linux)
        private static readonly string[] BlockedDirectories = { 
            // VCS & IDE
            ".git", ".vscode", ".idea", "node_modules", "bin", "obj", 
            // Windows System
            "System32", "Windows", "SysWOW64", "Program Files", "Program Files (x86)", 
            "ProgramData", "AppData", "Documents and Settings", "Config.msi", "System Volume Information",
            // Linux System
            "/bin", "/boot", "/dev", "/etc", "/lib", "/lib32", "/lib64", "/libx32", "/lost+found", 
            "/media", "/mnt", "/opt", "/proc", "/root", "/run", "/sbin", "/srv", "/sys", "/tmp", "/usr", "/var",
            // Sensitive Config/Env
            ".ssh", ".aws", ".azure", ".kube", ".gnupg", ".config", "Users/Public"
        };

        public const int DefaultMaxFileOperations = 100;
        public const int DefaultMaxRecursiveDepth = 5;

        public int MaxFileOperations { get; set; } = DefaultMaxFileOperations;
        public int MaxRecursiveDepth { get; set; } = DefaultMaxRecursiveDepth;

        public string? MasterPassword { get; set; }

        /// <summary>
        /// Explicit list of directories (Safe Zones) where script safety overrides 
        /// (like ### ALLOW_GREATER_THAN_100_FILE) are permitted.
        /// </summary>
        public List<string> ApprovedSafeZones { get; } = new();

        /// <summary>
        /// Flag to indicate the current operation is performed by an internal engine component 
        /// (e.g. SessionManager) and should bypass metadata protection.
        /// </summary>
        public bool IsInternalOperation { get; set; }
        public bool IsTestMode { get; set; }

        /// <summary>
        /// Explicit list of environment variable names that scripts are authorized to read via ENV().
        /// Empty by default. Use '*' to allow all (not recommended for multi-tenant envs).
        /// </summary>
        public HashSet<string> AllowedEnvVars { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Explicit list of network hosts that scripts are authorized to connect to.
        /// Supports '*' wildcards at the start (e.g. *.google.com).
        /// Default is '*' (unrestricted). Remove '*' to enable strict egress control.
        /// </summary>
        public HashSet<string> AllowedHosts { get; } = new(StringComparer.OrdinalIgnoreCase) { "*" };

        /// <summary>
        /// Validates that an environment variable is safe to read.
        /// </summary>
        public void ValidateEnvVar(string name)
        {
            if (AllowedEnvVars.Contains("*")) return;
            if (!AllowedEnvVars.Contains(name))
            {
                throw new SecurityException($"Access to environment variable '{name}' is denied. It must be added to the authorized list in SecurityService.AllowedEnvVars.");
            }
        }

        /// <summary>
        /// Validates that a network host is safe to connect to.
        /// </summary>
        public void ValidateHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return;
            if (IsInternalOperation || IsTestMode) return;

            // Always allow local loopback
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || 
                host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("::1", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (AllowedHosts.Contains("*")) return;

            foreach (var allowed in AllowedHosts)
            {
                if (allowed.StartsWith("*."))
                {
                    var domain = allowed.Substring(2);
                    if (host.EndsWith(domain, StringComparison.OrdinalIgnoreCase)) return;
                }
                
                if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase)) return;
            }

            throw new SecurityException($"Connection to host '{host}' is denied. This host must be added to the authorized list in SecurityService.AllowedHosts.");
        }

        /// <summary>
        /// Validates that a path is safe to access. Checks for root access, protected directories, and system paths.
        /// </summary>
        public void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var fullPath = Path.GetFullPath(path);
            
            // 0. Internal Bypass: If the engine is managing its own files (SessionManager), bypass safety checks.
            if (IsInternalOperation) return;

            // 0.1 Test Mode Authorizations:
            // In test mode, we automatically authorize access to the test execution directory and system temp.
            // This is necessary because many tests create ephemeral files in these locations.
            bool isAuthorizedTestPath = false;
            if (IsTestMode)
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)) isAuthorizedTestPath = true;
                
                // Also allow access to the system temp directory in test mode for isolation tests
                if (fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) isAuthorizedTestPath = true;
            }

            var root = Path.GetPathRoot(fullPath);
            var normalizedPath = fullPath.Replace('\\', '/');
            var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // 1. Block root directory access (e.g., C:\ or /) - NEVER bypassable
            if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException($"Unauthorized access to root directory: {fullPath}");
            }

            // 2. Block CRITICAL system/protected directories (Multi-Platform) - NEVER bypassable
            // These are so sensitive that even tests shouldn't touch them unless mocking the service.
            string[] criticalBlocks = { ".git", ".ssh", ".aws", ".azure", ".kube", ".gnupg", ".config", "Windows", "System32", "etc", "/root" };
            foreach (var blocked in criticalBlocks)
            {
                if (segments.Any(s => string.Equals(s, blocked, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new SecurityException($"Unauthorized access to protected system directory: {blocked} in path {fullPath}");
                }
            }

            // 3. Authorization Bypass: If we are in test mode and the path is within an authorized location 
            // (BaseDir or Temp), we allow it to proceed, bypassing 'standard' blocks like AppData or bin/obj.
            if (isAuthorizedTestPath) return;

            // 4. Block access to standard system/protected directories
            foreach (var blocked in BlockedDirectories)
            {
                var blockedClean = blocked.Trim('/'); 
                if (segments.Any(s => string.Equals(s, blockedClean, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new SecurityException($"Unauthorized access to protected system/environment directory: {blocked} in path {fullPath}");
                }
            }

            // 5. Session Isolation (Item 227): Block commands from targeting session metadata
            if (!IsInternalOperation)
            {
                var fileName = Path.GetFileName(fullPath).ToLowerInvariant();
                if (fileName.EndsWith(".etlsession") || fileName.EndsWith(".recovery.json"))
                {
                    throw new SecurityException($"Unauthorized access to internal session metadata: {fileName}");
                }
                
                if (fullPath.Contains("_temp") && segments.Any(s => s.EndsWith("_temp")))
                {
                    // Specifically protect the session temp data folder
                    throw new SecurityException("Direct access to session temporary storage is prohibited.");
                }
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

            // 1. Check blacklist (highest priority - NEVER bypassable)
            if (BlockedExtensions.Contains(ext))
            {
                throw new SecurityException($"Access denied to dangerous file type: {ext}. These system-level file types are strictly forbidden.");
            }

            // 2. Check whitelist
            if (!AllowedExtensions.Contains(ext) && !allowUnknown)
            {
                throw new SecurityException($"File type '{ext}' is not in the allowed data-connector whitelist. Use 'SET ALLOW_FILE_TYPE_ACCESS ON;' override if necessary.");
            }
        }

        /// <summary>
        /// Prevents scripts from modifying or creating files that contain application logic (Script Immutability).
        /// </summary>
        public void ValidateWriteAccess(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || IsInternalOperation) return;

            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) return;

            if (BlockedWriteExtensions.Contains(ext))
            {
                throw new SecurityException($"Script Immutability Guardrail: Modification of application logic files ({ext}) is strictly prohibited via script execution. These files must be managed by a human operator.");
            }
        }

        /// <summary>
        /// Checks if an operation count or recursion depth exceeds the allowed limits.
        /// Bypasses are only honored in 'Approved Safe Zones'.
        /// </summary>
        public void CheckRunawayProtection(int count, int depth, bool allowLargeCount = false, bool allowDeepRecursion = false, string? path = null)
        {
            bool isSafeZone = false;
            if (!string.IsNullOrEmpty(path))
            {
                var fullPath = Path.GetFullPath(path);
                isSafeZone = ApprovedSafeZones.Any(z => fullPath.StartsWith(z, StringComparison.OrdinalIgnoreCase));
            }

            if (count > MaxFileOperations && (!allowLargeCount || !isSafeZone))
            {
                string msg = allowLargeCount && !isSafeZone 
                    ? $"Runaway protection: Safety overrides for operation count are only permitted within approved user workspaces. Path '{path}' is outside a safe zone."
                    : $"Runaway protection: File operation count ({count}) exceeds the safety limit of {MaxFileOperations}. Use 'SET ALLOW_GREATER_THAN_{MaxFileOperations}_FILE ON;' override.";
                throw new SecurityException(msg);
            }

            if (count > MaxFileOperations && allowLargeCount && isSafeZone)
            {
                _logger.Warning("Security Override: Large file operation count ({Count}) authorized via safe zone '{Path}'.", count, path);
            }

            if (depth > MaxRecursiveDepth && (!allowDeepRecursion || !isSafeZone))
            {
                 string msg = allowDeepRecursion && !isSafeZone 
                    ? $"Runaway protection: Safety overrides for recursive depth are only permitted within approved user workspaces. Path '{path}' is outside a safe zone."
                    : $"Runaway protection: Recursive operation depth ({depth}) exceeds the safety limit of {MaxRecursiveDepth}. Use 'SET ALLOW_RECURSIVE_GREATER_THAN_{MaxRecursiveDepth}_LAYERS ON;' override.";
                throw new SecurityException(msg);
            }

            if (depth > MaxRecursiveDepth && allowDeepRecursion && isSafeZone)
            {
                _logger.Warning("Security Override: Deep recursion depth ({Depth}) authorized via safe zone '{Path}'.", depth, path);
            }
        }

        /// <summary>
        /// Executes an internal operation with elevated privileges, ensuring the bypass flag is reset.
        /// </summary>
        public void ExecuteInternal(Action action)
        {
            var wasInternal = IsInternalOperation;
            IsInternalOperation = true;
            try
            {
                action();
            }
            finally
            {
                IsInternalOperation = wasInternal;
            }
        }

        /// <summary>
        /// Executes an internal operation with elevated privileges, ensuring the bypass flag is reset.
        /// </summary>
        public async Task ExecuteInternalAsync(Func<Task> action)
        {
            var wasInternal = IsInternalOperation;
            IsInternalOperation = true;
            try
            {
                await action();
            }
            finally
            {
                IsInternalOperation = wasInternal;
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
        /// <summary>
        /// Checks if a path is considered a critical system path that should never be registered as a Safe Zone.
        /// </summary>
        public bool IsSystemPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            try
            {
                var fullPath = Path.GetFullPath(path);
                var root = Path.GetPathRoot(fullPath);
                
                // 1. Root of any drive is a system path
                if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)) return true;
                
                var normalizedPath = fullPath.Replace('\\', '/');
                var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                
                // 2. Check against critical system directories (Windows & Linux)
                string[] criticalBlocks = { "Windows", "System32", "etc", "root", "bin", "sbin", "usr", "var", "etc", "Boot" };
                foreach (var blocked in criticalBlocks)
                {
                    if (segments.Any(s => string.Equals(s, blocked, StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
                
                return false;
            }
            catch { return true; } // Safety first: if invalid path, treat as system path
        }
    }

    public class SecurityException : Exception
    {
        public SecurityException(string message) : base(message) { }
    }
}
