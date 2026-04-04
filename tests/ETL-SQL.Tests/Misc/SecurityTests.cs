using Xunit;
using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace ETL_SQL.Tests
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
    }
}
