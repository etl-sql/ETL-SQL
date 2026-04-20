using Xunit;
using System;
using System.Threading.Tasks;
using System.Security.Cryptography;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.App;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace ETL_SQL.Tests.Integration
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
            
            var lexer = new Lexer($"CREATE CONNECTION SecureConn ON MOCKDB('{encrypted}');");
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
            string script = "CREATE CONNECTION MyConn ON MSSQL('Server=localhost;User=sa;Password=secret;');";
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
            string script = $"CREATE CONNECTION MyConn ON MSSQL('{encrypted}');";
            
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
            
            // Should allow override in safe zone
            security.CheckRunawayProtection(101, 1, allowLargeCount: true, allowDeepRecursion: false, path: safePath + "\\file.csv");
            
            // Should FAIL override in neutral zone
            Assert.Throws<ETL_SQL.Services.SecurityException>(() => 
                security.CheckRunawayProtection(101, 1, allowLargeCount: true, allowDeepRecursion: false, path: neutralPath + "\\file.csv")
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
    }
}
