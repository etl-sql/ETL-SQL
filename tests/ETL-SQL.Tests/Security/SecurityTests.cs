using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Security
{
    public class SecurityTests
    {

        [Fact]
        public void TestEncryptionDecryption()
        {
            string original = "Server=myServerAddress;Database=myDataBase;User Id=myUsername;Password=myPassword;";
            string pass = "Secret123!";

            string encrypted = CryptoUtils.Encrypt(original, pass);
            Assert.StartsWith("ENC:", encrypted);

            string decrypted = CryptoUtils.Decrypt(encrypted, pass);
            Assert.Equal(original, decrypted);

            Assert.ThrowsAny<Exception>(() => CryptoUtils.Decrypt(encrypted, "WrongPass"));
        }

        [Fact]
        public void UnauthenticatedEncValue_IsRefused()
        {
            const string password = "legacy-master-password";
            // A version-1 payload: AES-CBC with no authentication tag.
            var salt = new byte[16];
            var iv = new byte[16];
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Key = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
            var cipher = aes.EncryptCbc(System.Text.Encoding.UTF8.GetBytes("Server=db;"), iv);
            var legacy = "ENC:" + Convert.ToBase64String(new byte[] { 1 }.Concat(salt).Concat(iv).Concat(cipher).ToArray());

            Assert.Throws<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() => ETL_SQL.Common.CryptoUtils.Decrypt(legacy, password));
        }

        [Fact]
        public void TestEncryptedStringTamperDetection()
        {
            string encrypted = CryptoUtils.Encrypt("secret-value", "Secret123!");
            byte[] payload = Convert.FromBase64String(encrypted.Substring("ENC:".Length));
            payload[^1] ^= 0x01;
            string tampered = "ENC:" + Convert.ToBase64String(payload);

            Assert.ThrowsAny<Exception>(() => CryptoUtils.Decrypt(tampered, "Secret123!"));
        }

        [Fact]
        public async Task TestEvaluatorDecryption()
        {
            string original = "dummy_connection_string";
            string pass = "MasterPass";
            string encrypted = CryptoUtils.Encrypt(original, pass);

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.MasterPassword = pass;

            var lexer = new Lexer($"CREATE CONNECTION SecureConn AS MOCKDB();");
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            await eval.Evaluate(script);

            Assert.True(eval.Connections.ContainsKey("SecureConn"), "Connection not created.");
        }

        [Fact]
        public void TestEncryptionAtRest()
        {
            string password = "MyPass";
            string script = "CREATE CONNECTION MyConn AS MSSQL('Server=localhost;User=sa;Password=secret;');";
            string encryptedScript = EncryptionLogic(script, password);

            Assert.Contains("ENC:", encryptedScript);
            Assert.DoesNotContain("Password=secret;", encryptedScript);
        }

        [Fact]
        public void TestDecryptionOnLoad()
        {
            string password = "MyPass";
            // We need a real encrypted string for this to work
            string original = "Server=localhost;Password=secret;";
            string encrypted = CryptoUtils.Encrypt(original, password);
            string script = $"CREATE CONNECTION MyConn AS MSSQL('{encrypted}');";

            string decryptedScript = DecryptionLogic(script, password);
            Assert.Contains(original, decryptedScript);
        }

        private static string EncryptionLogic(string content, string password)
        {
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("CREATE CONNECTION", StringComparison.OrdinalIgnoreCase) &&
                    !lines[i].Contains("ENCRYPT=OFF", StringComparison.OrdinalIgnoreCase) &&
                    !lines[i].Contains("'ENC:", StringComparison.OrdinalIgnoreCase))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(lines[i], @"('\s*[^']+\s*')", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var target = match.Value.Trim('\'');
                        var encrypted = CryptoUtils.Encrypt(target, password);
                        lines[i] = lines[i].Replace(match.Value, $"'{encrypted}'");
                    }
                }
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static string DecryptionLogic(string content, string password)
        {
            if (content.Contains("'ENC:", StringComparison.OrdinalIgnoreCase))
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(content, @"'ENC:[^']+'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    try
                    {
                        var encrypted = match.Value.Trim('\'');
                        var decrypted = CryptoUtils.Decrypt(encrypted, password);
                        content = content.Replace(match.Value, $"'{decrypted}'");
                    }
                    catch { /* ignore */ }
                }
            }
            return content;
        }

        [Fact]
        public void TestSafeZoneRunawayProtection()
        {
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance);
            security.IsTestMode = false; // Force restriction enforcement for verification
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "etlsql-runaway-" + Guid.NewGuid().ToString("N"));
            var safePath = System.IO.Path.Combine(root, "MyProject");
            var neutralPath = System.IO.Path.Combine(root, "Data");

            security.ApprovedSafeZones.Add(safePath);

            // 1. FileSystem Limit (100)
            // Should allow override in safe zone
            security.CheckRunawayProtection(OperationType.FileSystem, 101, 1, allowLargeCount: true, allowDeepRecursion: false, path: System.IO.Path.Combine(safePath, "file.csv"));

            // Should FAIL override in neutral zone
            Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                security.CheckRunawayProtection(OperationType.FileSystem, 101, 1, allowLargeCount: true, allowDeepRecursion: false, path: System.IO.Path.Combine(neutralPath, "file.csv"))
            );

            // 2. EngineInternal/Mock Limit (100,000)
            // Should allow up to 100k without override
            security.CheckRunawayProtection(OperationType.EngineInternal, 99999, 1, allowLargeCount: false, allowDeepRecursion: false);

            // Should fail at 100,001 without override
            Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                security.CheckRunawayProtection(OperationType.EngineInternal, 100001, 1, allowLargeCount: false, allowDeepRecursion: false)
            );

            // Should allow override for Internal if requested (though usually not needed)
            security.CheckRunawayProtection(OperationType.EngineInternal, 100001, 1, allowLargeCount: true, allowDeepRecursion: false);

            // 3. Recursion Limit (5)
            // Should allow up to 5 without override
            security.CheckRunawayProtection(OperationType.EngineInternal, 1, 5, allowLargeCount: false, allowDeepRecursion: false);

            // Should fail at 6 without override
            Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                security.CheckRunawayProtection(OperationType.EngineInternal, 1, 6, allowLargeCount: false, allowDeepRecursion: false)
            );

            // Should allow override in safe zone
            security.CheckRunawayProtection(OperationType.EngineInternal, 1, 6, allowLargeCount: false, allowDeepRecursion: true, path: System.IO.Path.Combine(safePath, "file.csv"));

            // Should FAIL override in neutral zone
            Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                security.CheckRunawayProtection(OperationType.EngineInternal, 1, 6, allowLargeCount: false, allowDeepRecursion: true, path: System.IO.Path.Combine(neutralPath, "file.csv"))
            );
        }

        [Fact]
        public void TestLinuxPathBlocking()
        {
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance);

            // Should block Linux system paths even on Windows
            Assert.Throws<ETL_SQL.Services.SecurityException>(() => security.ValidatePath("/etc/passwd"));
            Assert.Throws<ETL_SQL.Services.SecurityException>(() => security.ValidatePath("/usr/bin/bash"));
            Assert.Throws<ETL_SQL.Services.SecurityException>(() => security.ValidatePath("/var/log/syslog"));
        }

        [Fact]
        public void TestExtensionBlacklistStrictness()
        {
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance);

            // Should block .exe even if allowUnknown (override flag) is TRUE
            Assert.Throws<ETL_SQL.Services.SecurityException>(() => security.ValidateFileType("C:\\Safe\\tool.exe", allowUnknown: true));
            Assert.Throws<ETL_SQL.Services.SecurityException>(() => security.ValidateFileType("C:\\Safe\\driver.sys", allowUnknown: true));
        }

        [Fact]
        public void AdditionalBlockedExtensions_FromConfiguration_AreDenyOnly()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:AdditionalBlockedExtensions:0"] = "backup"
                })
                .Build();
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance);
            security.UpdateFromConfiguration(config);

            Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                security.ValidateFileType("C:\\Safe\\daily.backup", allowUnknown: true));
        }

        [Fact]
        public void AdditionalBlockedPaths_FromConfiguration_BlockConfiguredSegments()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:AdditionalBlockedPaths:0"] = "quarantine"
                })
                .Build();
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance)
            {
                IsTestMode = false
            };
            security.UpdateFromConfiguration(config);

            Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                security.ValidatePath("C:\\Data\\quarantine\\extract.csv"));
        }

        [Fact]
        public void TestEnvironmentFolderProtection()
        {
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance);

            // Should block sensitive environment folders, wherever the home folder is
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Assert.Throws<ETL_SQL.Services.SecurityException>(() => security.ValidatePath(System.IO.Path.Combine(home, ".ssh", "id_rsa")));
            Assert.Throws<ETL_SQL.Services.SecurityException>(() => security.ValidatePath(System.IO.Path.Combine(home, ".aws", "credentials")));
        }

        [Fact]
        public void TestInternalBypass()
        {
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance);

            // Enable internal bypass
            security.IsInternalOperation = true;

            // Should now allow restricted files (even in bin or restricted extensions)
            security.ValidatePath("C:\\Safe\\test.etlsession");
            security.ValidatePath("C:\\Safe\\recovery.recovery.json");
            security.ValidatePath("C:\\Safe\\sess_temp\\data.json");
            security.ValidatePath("C:\\Windows\\System32\\kernel32.dll"); // egregious case that bypasses all
        }

        [Fact]
        public void ApplicationFiles_StayProtected_WhenTheInstallFolderIsASafeZone()
        {
            // The installers approve the install folder as a safe zone, and the services run
            // user-authored scripts in-process. The service's own configuration, keys, databases
            // and binaries must still be out of reach.
            var installDir = AppDomain.CurrentDomain.BaseDirectory;
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance) { IsTestMode = false };
            security.ApprovedSafeZones.Add(installDir);

            foreach (var name in new[]
                     {
                         "appsettings.json", "appsettings.secrets.json", "appsettings.Production.json",
                         "portal.db", "portal.db-wal", "etlsql.db", "first-run-password.txt",
                         "ETL-SQL.Portal.dll", "ETL-SQL.Portal.runtimeconfig.json", "ETL-SQL.Portal.deps.json"
                     })
            {
                Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                    security.ValidatePath(System.IO.Path.Combine(installDir, name)));
            }
            Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                security.ValidatePath(System.IO.Path.Combine(installDir, ".portal-keys", "key-1.xml")));
            // The machine key file is protected wherever it lives.
            Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                security.ValidatePath(System.IO.Path.Combine(installDir, "home", "etl-sql", "machine.key")));
            // The secrets file is protected wherever a deployment keeps it.
            Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                security.ValidatePath(System.IO.Path.Combine(installDir, "data", "appsettings.secrets.json")));

            // Data the services keep under the install folder stays reachable.
            security.ValidatePath(System.IO.Path.Combine(installDir, "data", "datasets", "sales.parquet"));
            security.ValidatePath(System.IO.Path.Combine(installDir, "Reports", "sales.json"));
            security.ValidatePath(System.IO.Path.Combine(installDir, "export.csv"));

            // Unrestricted mode does not reopen them; only engine-internal operations bypass the check.
            security.ProtectionMode = ETL_SQL.Services.PathProtectionMode.Unrestricted;
            Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                security.ValidatePath(System.IO.Path.Combine(installDir, "appsettings.secrets.json")));
            security.IsInternalOperation = true;
            security.ValidatePath(System.IO.Path.Combine(installDir, "appsettings.secrets.json"));
        }

        [Fact]
        public void TestHardwareLockedEncryption()
        {
            var plainText = "Sensitive Session State Data";
            var entropy = "Session-123-Unique-Entropy";

            // 1. Protect data
            var protectedData = ETL_SQL.Common.CryptoUtils.Protect(plainText, entropy);
            Assert.StartsWith(OperatingSystem.IsWindows() ? "DPAPI:" : "MACHINE:", protectedData);

            // 2. Unprotect data (should work for same user/machine)
            var decryptedData = ETL_SQL.Common.CryptoUtils.Unprotect(protectedData, entropy);
            Assert.Equal(plainText, decryptedData);

            // 3. Fail with wrong entropy (DPAPI raises CryptographicException; the machine key's
            // authentication check raises ExecutionException)
            Assert.ThrowsAny<Exception>(() =>
                ETL_SQL.Common.CryptoUtils.Unprotect(protectedData, "Wrong-Entropy")
            );
        }

        [Fact]
        public void MachineGenericEncryption_RoundTrips_AndDetectsTampering()
        {
            var plain = System.Text.Encoding.UTF8.GetBytes("host-bound machine secret payload");

            var protectedBytes = CryptoUtils.ProtectGeneric(plain, entropy: null);
            Assert.Equal(plain, CryptoUtils.UnprotectGeneric(protectedBytes, entropy: null));

            // Authenticated: flipping any ciphertext/tag byte must fail closed, not return garbage.
            var tampered = (byte[])protectedBytes.Clone();
            tampered[^1] ^= 0xFF;
            Assert.ThrowsAny<Exception>(() => CryptoUtils.UnprotectGeneric(tampered, entropy: null));
        }

        [Fact]
        public void MachineGenericEncryption_RefusesLegacyCbcOnlyBlob()
        {
            var plain = System.Text.Encoding.UTF8.GetBytes("legacy session state");

            // Reproduce the pre-hardening format: raw machine key as the AES key, IV prepended, no MAC.
            byte[] key = CryptoUtils.GetMachineKey(entropy: null);
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            using var ms = new System.IO.MemoryStream();
            ms.Write(aes.IV, 0, aes.IV.Length);
            using (var enc = aes.CreateEncryptor())
            using (var cs = new CryptoStream(ms, enc, CryptoStreamMode.Write))
            {
                cs.Write(plain, 0, plain.Length);
            }
            var legacyBlob = ms.ToArray();

            Assert.Throws<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() => CryptoUtils.UnprotectGeneric(legacyBlob, entropy: null));
        }

        [Fact]
        public void MountedMachineKeyIsAuthoritativeSoDisposableWorkersCanRehydrateCheckpoints()
        {
            // A disposable worker's LocalApplicationData is its own single-use scratch. If the local
            // random key file won, every attempt would protect state under material no other sandbox
            // — including the next attempt of the same tenant — could ever reproduce.
            var keyFile = Path.Combine(Path.GetTempPath(), $"etlsql-machine-key-{Guid.NewGuid():N}");
            File.WriteAllText(keyFile, new string('k', 64));
            var otherKeyFile = Path.Combine(Path.GetTempPath(), $"etlsql-machine-key-{Guid.NewGuid():N}");
            File.WriteAllText(otherKeyFile, new string('m', 64));
            try
            {
                Environment.SetEnvironmentVariable("ETLSQL_MACHINE_KEY_FILE", keyFile);
                var plain = System.Text.Encoding.UTF8.GetBytes("checkpointed session state");
                var sealedBytes = CryptoUtils.ProtectGeneric(plain, entropy: "tenant-a");

                // A second worker holding the same mounted key reads it back.
                Assert.Equal(plain, CryptoUtils.UnprotectGeneric(sealedBytes, entropy: "tenant-a"));

                // A worker holding different key material cannot, so the mount is still the boundary.
                Environment.SetEnvironmentVariable("ETLSQL_MACHINE_KEY_FILE", otherKeyFile);
                Assert.ThrowsAny<Exception>(
                    () => CryptoUtils.UnprotectGeneric(sealedBytes, entropy: "tenant-a"));

                // A configured-but-unusable key fails rather than silently falling back to host-local
                // material, which would protect data under a key the fleet cannot reproduce.
                var shortKeyFile = Path.Combine(Path.GetTempPath(), $"etlsql-machine-key-{Guid.NewGuid():N}");
                File.WriteAllText(shortKeyFile, "too-short");
                Environment.SetEnvironmentVariable("ETLSQL_MACHINE_KEY_FILE", shortKeyFile);
                Assert.ThrowsAny<Exception>(() => CryptoUtils.GetMachineKey(entropy: null));
                File.Delete(shortKeyFile);
            }
            finally
            {
                Environment.SetEnvironmentVariable("ETLSQL_MACHINE_KEY_FILE", null);
                File.Delete(keyFile);
                File.Delete(otherKeyFile);
            }
        }

        [Fact]
        public void TestScriptImmutability()
        {
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance);

            // Should block writing to native script types
            Assert.Throws<SecurityException>(() => security.ValidateWriteAccess("test.etlsql"));
            Assert.Throws<SecurityException>(() => security.ValidateWriteAccess("report.rptsql"));
            Assert.Throws<SecurityException>(() => security.ValidateWriteAccess("db_setup.sql"));
            Assert.Throws<SecurityException>(() => security.ValidateWriteAccess("run.sh"));

            // Should allow reading/general access (ValidatePath doesn't block extensions, only ValidateWriteAccess does)
            security.ValidatePath("C:\\Safe\\test.etlsql");

            // Should allow writing to data assets
            security.ValidateWriteAccess("C:\\Safe\\data.csv");
            security.ValidateWriteAccess("C:\\Safe\\results.json");

            // Internal bypass should allow even scripts (for session logic)
            security.IsInternalOperation = true;
            security.ValidateWriteAccess("internal.etlsql");
        }

        [Fact]
        public void TestRestrictedMode_AllowsTemp()
        {
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance);
            security.IsTestMode = false;
            security.ProtectionMode = PathProtectionMode.Restricted;

            if (OperatingSystem.IsWindows())
            {
                // Per-user Windows temp is under AppData, which Restricted mode protects.
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                Assert.Throws<SecurityException>(() => security.ValidatePath(
                    System.IO.Path.Combine(profile, "AppData", "Local", "Temp", "data.csv")));
                security.ValidatePath("C:\\tmp\\data.csv");
                security.ValidatePath("C:\\temp\\log.txt");
            }
            else
            {
                security.ValidatePath("/tmp/session.json");
            }

            // Should still block OS directories
            Assert.Throws<SecurityException>(() => security.ValidatePath(SystemFile));
            Assert.Throws<SecurityException>(() => security.ValidatePath("/etc/passwd"));
        }

        [Fact]
        public void TestDefinedMode_EnforcesSafeZones()
        {
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance);
            security.IsTestMode = false;
            security.ProtectionMode = PathProtectionMode.Defined;
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "etlsql-defined-" + Guid.NewGuid().ToString("N"));
            var safePath = System.IO.Path.Combine(root, "ApprovedZone");
            security.ApprovedSafeZones.Add(safePath);

            // Should allow safe zone
            security.ValidatePath(System.IO.Path.Combine(safePath, "data.csv"));

            // Should block everything else (even temp)
            Assert.Throws<SecurityException>(() => security.ValidatePath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "data.csv")));
            Assert.Throws<SecurityException>(() => security.ValidatePath(System.IO.Path.Combine(root, "Data", "file.txt")));
        }

        [Fact]
        public void TestUnrestrictedMode_BypassAll()
        {
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance);
            security.IsTestMode = false;
            security.ProtectionMode = PathProtectionMode.Unrestricted;

            // Should allow EVERYTHING
            security.ValidatePath("C:\\Windows\\system.ini");
            security.ValidatePath("/etc/passwd");
            security.ValidatePath("C:\\Users\\chuck\\.ssh\\id_rsa");
        }

        [Fact]
        public void TestSafeZone_LogAuditForSensitivePaths()
        {
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance);
            security.IsTestMode = false;
            security.ProtectionMode = PathProtectionMode.Restricted;

            var systemZone = System.IO.Path.GetDirectoryName(SystemFile)!;
            security.ApprovedSafeZones.Add(systemZone);

            // Should allow because it's in an Approved Safe Zone, despite being sensitive
            security.ValidatePath(SystemFile);
        }

        /// <summary>A file in this OS's protected system folder.</summary>
        private static string SystemFile => OperatingSystem.IsWindows() ? "C:\\Windows\\system.ini" : "/etc/hosts";
    }
}
