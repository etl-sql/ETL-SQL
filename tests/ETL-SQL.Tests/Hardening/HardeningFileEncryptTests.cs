using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Hardening
{
    public class FileEncryptionTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _plainFile;
        private readonly string _encryptedFile;
        private readonly string _decryptedFile;
        private readonly string _publicKeyFile;
        private readonly string _privateKeyFile;
        private readonly string _passphraseKeyFile;
        private const string Passphrase = "TestPassphrase123!";

        public FileEncryptionTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "ETL_SQL_EncryptionTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _plainFile = Path.Combine(_testDir, "plain.txt");
            _encryptedFile = Path.Combine(_testDir, "encrypted.dat");
            _decryptedFile = Path.Combine(_testDir, "decrypted.txt");
            _publicKeyFile = Path.Combine(_testDir, "id_rsa.pub");
            _privateKeyFile = Path.Combine(_testDir, "id_rsa");
            _passphraseKeyFile = Path.Combine(_testDir, "id_rsa_pass");

            File.WriteAllText(_plainFile, "Hello World! This is a secret message.");

            // Generate RSA keys
            using var rsa = RSA.Create(2048);
            File.WriteAllText(_publicKeyFile, rsa.ExportRSAPublicKeyPem());
            File.WriteAllText(_privateKeyFile, rsa.ExportRSAPrivateKeyPem());
            File.WriteAllText(_passphraseKeyFile, rsa.ExportEncryptedPkcs8PrivateKeyPem(Passphrase, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100000)));
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true);
        }

        private static class PKCS7ExportFormat
        {
            public const bool PublicKey = true;
            public const bool PrivateKey = false;
        }

        [Fact]
        public void CryptoUtils_PasswordEncryption_SHA256()
        {
            string pwd = "Password123!";
            CryptoUtils.EncryptFile(_plainFile, _encryptedFile, pwd, true, HashAlgorithmName.SHA256);
            Assert.True(File.Exists(_encryptedFile));
            Assert.NotEqual(File.ReadAllText(_plainFile), File.ReadAllText(_encryptedFile));

            CryptoUtils.DecryptFile(_encryptedFile, _decryptedFile, pwd, true, HashAlgorithmName.SHA256);
            Assert.Equal(File.ReadAllText(_plainFile), File.ReadAllText(_decryptedFile));
        }

        [Fact]
        public void CryptoUtils_PasswordEncryption_SHA512()
        {
            string pwd = "Password123!";
            CryptoUtils.EncryptFile(_plainFile, _encryptedFile, pwd, true, HashAlgorithmName.SHA512);

            CryptoUtils.DecryptFile(_encryptedFile, _decryptedFile, pwd, true, HashAlgorithmName.SHA512);
            Assert.Equal(File.ReadAllText(_plainFile), File.ReadAllText(_decryptedFile));
        }

        [Fact]
        public void CryptoUtils_PasswordEncryption_DetectsTampering()
        {
            string pwd = "Password123!";
            CryptoUtils.EncryptFile(_plainFile, _encryptedFile, pwd, true, HashAlgorithmName.SHA256);

            var encrypted = File.ReadAllBytes(_encryptedFile);
            encrypted[^1] ^= 0x01;
            File.WriteAllBytes(_encryptedFile, encrypted);

            Assert.ThrowsAny<Exception>(() =>
                CryptoUtils.DecryptFile(_encryptedFile, _decryptedFile, pwd, true, HashAlgorithmName.SHA256));
        }

        [Fact]
        public void CryptoUtils_SshKeyEncryption_NoPassphrase()
        {
            CryptoUtils.EncryptFileWithSsh(_plainFile, _encryptedFile, _publicKeyFile, true);
            Assert.True(File.Exists(_encryptedFile));

            CryptoUtils.DecryptFileWithSsh(_encryptedFile, _decryptedFile, _privateKeyFile, true);
            Assert.Equal(File.ReadAllText(_plainFile), File.ReadAllText(_decryptedFile));
        }

        [Fact]
        public void CryptoUtils_SshKeyEncryption_WithPassphrase()
        {
            CryptoUtils.EncryptFileWithSsh(_plainFile, _encryptedFile, _publicKeyFile, true);

            // Decrypt with correct passphrase
            CryptoUtils.DecryptFileWithSsh(_encryptedFile, _decryptedFile, _passphraseKeyFile, true, Passphrase);
            Assert.Equal(File.ReadAllText(_plainFile), File.ReadAllText(_decryptedFile));

            // Decrypt with WRONG passphrase should throw
            Assert.ThrowsAny<Exception>(() => CryptoUtils.DecryptFileWithSsh(_encryptedFile, _decryptedFile, _passphraseKeyFile, true, "wrong"));
        }

        [Fact]
        public async Task Linter_EncryptionRules()
        {
            var sql = @"
                CREATE CONNECTION c1 AS FLATFILE('data.csv', ENCRYPT=ON);
                CREATE CONNECTION c2 AS FLATFILE('data.csv', ENCRYPT=ON, PASSWORD='abc');
                CREATE CONNECTION c3 AS FLATFILE('data.csv', ENCRYPT=ON, KEYFILE='k.pem');
                CREATE CONNECTION c4 AS FLATFILE('data.csv', ENCRYPT=ON, PASSWORD='abc', ALGORITHM='INVALID');
            ";

            var lexer = new Lexer(sql);
            var parser = new ETL_SQL.Core.Parser.Parser(lexer.Tokenize(), sql);
            var script = parser.Parse();

            var linter = new Linter();
            linter.AddRule(new ConnectionEncryptionRule());
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            var errors = new System.Collections.Generic.List<LintResult>(results);

            // c1: Missing password/keyfile
            Assert.Contains(errors, r => r.Message.Contains("requires either a PASSWORD or a KEYFILE"));

            // c2, c3: Valid
            // c4: Invalid algorithm
            Assert.Contains(errors, r => r.Message.Contains("Unsupported encryption algorithm 'INVALID'"));
        }
    }

    internal class DefaultLintContext : ILintContext
    {
        public IMetadataProvider? Metadata { get; set; }
        public string DocumentUri { get; set; } = "test://file.sql";
    }
}
