using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using ETL_SQL.Core.Common;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Cross-cutting dataset security assertions that do not require a running portal.
    /// Authorization and lifecycle rows of the matrix remain in the portal integration tests.
    /// </summary>
    public sealed class DatasetSecurityMatrixTests : IDisposable
    {
        private readonly string _root =
            Path.Combine(Path.GetTempPath(), "etlsql_dataset_matrix_" + Guid.NewGuid().ToString("N"));

        public DatasetSecurityMatrixTests()
        {
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        [Fact]
        public void AtRestPassword_SameKeyRoundTrips_SwappedKeyFails()
        {
            const string atRestKey = "portal-at-rest-key-a";
            var plain = WritePlaintext();
            var encrypted = Path.Combine(_root, "at-rest.enc");
            var decrypted = Path.Combine(_root, "at-rest.dec");

            Password(atRestKey).EncryptFile(plain, encrypted);

            AssertCiphertextDiffers(plain, encrypted);
            Password(atRestKey).DecryptFile(encrypted, decrypted);
            Assert.Equal(File.ReadAllBytes(plain), File.ReadAllBytes(decrypted));

            var wrongOutput = Path.Combine(_root, "wrong-at-rest.dec");
            Assert.ThrowsAny<CryptographicException>(
                () => Password("portal-at-rest-key-b").DecryptFile(encrypted, wrongOutput));
        }

        [Fact]
        public void PasswordTransport_RightPasswordRoundTrips_WrongPasswordFails()
        {
            const string transportPassword = "portable-transport-password";
            var plain = WritePlaintext();
            var encrypted = Path.Combine(_root, "password-export.enc");
            var decrypted = Path.Combine(_root, "password-export.dec");

            Password(transportPassword).EncryptFile(plain, encrypted);

            AssertCiphertextDiffers(plain, encrypted);
            Password(transportPassword).DecryptFile(encrypted, decrypted);
            Assert.Equal(File.ReadAllBytes(plain), File.ReadAllBytes(decrypted));

            var wrongOutput = Path.Combine(_root, "wrong-password.dec");
            Assert.ThrowsAny<CryptographicException>(
                () => Password("incorrect-transport-password").DecryptFile(encrypted, wrongOutput));
        }

        [Fact]
        public void KeyFileTransport_PublicPrivateRoundTrips_MissingAndWrongKeysFail()
        {
            var (publicKey, privateKey) = WriteRsaKeyPair("transport");
            var (_, wrongPrivateKey) = WriteRsaKeyPair("wrong");
            var plain = WritePlaintext();
            var encrypted = Path.Combine(_root, "keyfile-export.enc");
            var decrypted = Path.Combine(_root, "keyfile-export.dec");

            KeyFile(publicKey).EncryptFile(plain, encrypted);

            AssertCiphertextDiffers(plain, encrypted);
            KeyFile(privateKey).DecryptFile(encrypted, decrypted);
            Assert.Equal(File.ReadAllBytes(plain), File.ReadAllBytes(decrypted));

            var missingOutput = Path.Combine(_root, "missing-key.dec");
            Assert.Throws<FileNotFoundException>(
                () => KeyFile(Path.Combine(_root, "missing.pem")).DecryptFile(encrypted, missingOutput));

            var wrongOutput = Path.Combine(_root, "wrong-key.dec");
            Assert.ThrowsAny<CryptographicException>(
                () => KeyFile(wrongPrivateKey).DecryptFile(encrypted, wrongOutput));
        }

        private string WritePlaintext()
        {
            var path = Path.Combine(_root, "dataset.parquet");
            File.WriteAllBytes(path, "PAR1-dataset-security-matrix-payload"u8.ToArray());
            return path;
        }

        private (string PublicKey, string PrivateKey) WriteRsaKeyPair(string name)
        {
            using var rsa = RSA.Create(2048);
            var publicPath = Path.Combine(_root, $"{name}.pub.pem");
            var privatePath = Path.Combine(_root, $"{name}.private.pem");
            File.WriteAllText(publicPath, rsa.ExportRSAPublicKeyPem());
            File.WriteAllText(privatePath, rsa.ExportRSAPrivateKeyPem());
            return (publicPath, privatePath);
        }

        private static EncryptionOptions Password(string password) =>
            new(new Dictionary<string, string>
            {
                ["ENCRYPT"] = "PASSWORD",
                ["PASSWORD"] = password
            });

        private static EncryptionOptions KeyFile(string path) =>
            new(new Dictionary<string, string>
            {
                ["ENCRYPT"] = "KEYFILE",
                ["KEYFILE"] = path
            });

        private static void AssertCiphertextDiffers(string plain, string encrypted)
        {
            Assert.True(File.Exists(encrypted));
            Assert.NotEqual(File.ReadAllBytes(plain), File.ReadAllBytes(encrypted));
        }
    }
}
