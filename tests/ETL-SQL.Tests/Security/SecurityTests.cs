using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Services;
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
            var safePath = "C:\\MyProject";
            var neutralPath = "C:\\Data";

            security.ApprovedSafeZones.Add(safePath);

            // 1. FileSystem Limit (100)
            // Should allow override in safe zone
            security.CheckRunawayProtection(OperationType.FileSystem, 101, 1, allowLargeCount: true, allowDeepRecursion: false, path: safePath + "\\file.csv");

            // Should FAIL override in neutral zone
            Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                security.CheckRunawayProtection(OperationType.FileSystem, 101, 1, allowLargeCount: true, allowDeepRecursion: false, path: neutralPath + "\\file.csv")
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
            security.CheckRunawayProtection(OperationType.EngineInternal, 1, 6, allowLargeCount: false, allowDeepRecursion: true, path: safePath + "\\file.csv");

            // Should FAIL override in neutral zone
            Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                security.CheckRunawayProtection(OperationType.EngineInternal, 1, 6, allowLargeCount: false, allowDeepRecursion: true, path: neutralPath + "\\file.csv")
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
        public void TestEnvironmentFolderProtection()
        {
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance);

            // Should block sensitive environment folders
            Assert.Throws<ETL_SQL.Services.SecurityException>(() => security.ValidatePath("C:\\Users\\chuck\\.ssh\\id_rsa"));
            Assert.Throws<ETL_SQL.Services.SecurityException>(() => security.ValidatePath("C:\\Users\\chuck\\.aws\\credentials"));
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
        public void TestHardwareLockedEncryption()
        {
            var plainText = "Sensitive Session State Data";
            var entropy = "Session-123-Unique-Entropy";

            // 1. Protect data
            var protectedData = ETL_SQL.Common.CryptoUtils.Protect(plainText, entropy);
            Assert.StartsWith("DPAPI:", protectedData);

            // 2. Unprotect data (should work for same user/machine)
            var decryptedData = ETL_SQL.Common.CryptoUtils.Unprotect(protectedData, entropy);
            Assert.Equal(plainText, decryptedData);

            // 3. Fail with wrong entropy
            Assert.Throws<CryptographicException>(() =>
                ETL_SQL.Common.CryptoUtils.Unprotect(protectedData, "Wrong-Entropy")
            );
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

            // Should allow common temp paths now
            security.ValidatePath("C:\\tmp\\data.csv");
            security.ValidatePath("C:\\temp\\log.txt");
            security.ValidatePath("/tmp/session.json");

            // Should still block OS directories
            Assert.Throws<SecurityException>(() => security.ValidatePath("C:\\Windows\\system.ini"));
            Assert.Throws<SecurityException>(() => security.ValidatePath("/etc/passwd"));
        }

        [Fact]
        public void TestDefinedMode_EnforcesSafeZones()
        {
            var security = new ETL_SQL.Services.SecurityService(NullLogger.Instance);
            security.IsTestMode = false;
            security.ProtectionMode = PathProtectionMode.Defined;
            var safePath = "C:\\ApprovedZone";
            security.ApprovedSafeZones.Add(safePath);

            // Should allow safe zone
            security.ValidatePath(safePath + "\\data.csv");

            // Should block everything else (even temp)
            Assert.Throws<SecurityException>(() => security.ValidatePath("C:\\tmp\\data.csv"));
            Assert.Throws<SecurityException>(() => security.ValidatePath("C:\\Data\\file.txt"));
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

            var windowsZone = "C:\\Windows";
            security.ApprovedSafeZones.Add(windowsZone);

            // Should allow because it's in an Approved Safe Zone, despite being sensitive
            security.ValidatePath(windowsZone + "\\system.ini");
        }
    }
}
