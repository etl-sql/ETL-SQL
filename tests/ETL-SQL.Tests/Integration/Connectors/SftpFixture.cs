using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;
using DotNet.Testcontainers.Configurations;

namespace ETL_SQL.Tests.Integration.Connectors
{
    /// <summary>
    /// Starts a real atmoz/sftp container and exposes credentials, port, and a generated
    /// RSA key pair for the SFTP integration test suite.
    /// </summary>
    public class SftpFixture : IAsyncLifetime
    {
        public const string TestUser = "sftpuser";
        public const string TestPassword = "sftppass";
        public const string RemoteUploadDir = "upload";

        private IContainer? _container;
        private string? _keyDir;

        public const string TestPassphrase = "sftp-test-passphrase-123";

        public string Host => "localhost";
        public int Port { get; private set; }
        public string PrivateKeyPath { get; private set; } = "";
        public string EncryptedPrivateKeyPath { get; private set; } = "";

        public async Task InitializeAsync()
        {
            // Generate a 2048-bit RSA key pair; write private key to a temp file and public
            // key to a directory that gets volume-mounted into /home/user/.ssh/keys — the
            // atmoz/sftp entrypoint appends everything in that directory to authorized_keys.
            _keyDir = Path.Combine(Path.GetTempPath(), $"sftp_keys_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_keyDir);

            var (privatePem, authorizedKeyLine) = GenerateRsaKeyPair();
            PrivateKeyPath = Path.Combine(_keyDir, "test_id_rsa.pem");
            File.WriteAllText(PrivateKeyPath, privatePem);
            File.WriteAllText(Path.Combine(_keyDir, "test_id_rsa.pub"), authorizedKeyLine);

            // Also write a passphrase-encrypted version of the same key pair so the
            // passphrase auth test can use it.  Both keys share the same authorized_keys
            // entry because they have the same public component.
            var encryptedPem = GenerateEncryptedPem(privatePem, TestPassphrase);
            EncryptedPrivateKeyPath = Path.Combine(_keyDir, "test_id_rsa_enc.pem");
            File.WriteAllText(EncryptedPrivateKeyPath, encryptedPem);

            _container = new ContainerBuilder("atmoz/sftp:latest")
                // Format: user:pass[:uid[:gid[:dir[,dir…]]]]
                .WithCommand($"{TestUser}:{TestPassword}:::{RemoteUploadDir}")
                .WithPortBinding(22, true)
                .WithBindMount(_keyDir, $"/home/{TestUser}/.ssh/keys", DotNet.Testcontainers.Configurations.AccessMode.ReadOnly)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server listening on"))
                .Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(22);
        }

        public async Task DisposeAsync()
        {
            if (_container != null)
                await _container.StopAsync();

            if (_keyDir != null && Directory.Exists(_keyDir))
                Directory.Delete(_keyDir, recursive: true);
        }

        // ── Key generation ────────────────────────────────────────────────────

        /// <summary>
        /// Re-exports <paramref name="unencryptedPem"/> as a PKCS#8 AES-256-CBC encrypted PEM
        /// using the given passphrase.  SSH.NET 2024+ reads this format natively.
        /// </summary>
        private static string GenerateEncryptedPem(string unencryptedPem, string passphrase)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(unencryptedPem);
            return rsa.ExportEncryptedPkcs8PrivateKeyPem(
                Encoding.UTF8.GetBytes(passphrase),
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000));
        }

        private static (string privatePem, string authorizedKeyLine) GenerateRsaKeyPair()
        {
            using var rsa = RSA.Create(2048);
            // Traditional PKCS#1 RSA private key — SSH.NET reads this format natively.
            var privatePem = rsa.ExportRSAPrivateKeyPem();
            // OpenSSH authorized_keys format: "ssh-rsa <base64> comment"
            var pubKeyBytes = EncodeOpenSshPublicKey(rsa.ExportParameters(false));
            var authorizedKeyLine = $"ssh-rsa {Convert.ToBase64String(pubKeyBytes)} etlsql-test\n";
            return (privatePem, authorizedKeyLine);
        }

        /// <summary>
        /// Encodes an RSA public key in the SSH wire format used by authorized_keys.
        /// Layout: [4-byte-BE-length][bytes("ssh-rsa")][mpint(e)][mpint(n)]
        /// </summary>
        private static byte[] EncodeOpenSshPublicKey(RSAParameters p)
        {
            using var ms = new MemoryStream();
            WriteSshString(ms, Encoding.ASCII.GetBytes("ssh-rsa"));
            WriteSshMpint(ms, p.Exponent!);
            WriteSshMpint(ms, p.Modulus!);
            return ms.ToArray();
        }

        private static void WriteSshString(MemoryStream ms, byte[] data)
        {
            var lenBytes = BitConverter.GetBytes(data.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(lenBytes);
            ms.Write(lenBytes, 0, 4);
            ms.Write(data, 0, data.Length);
        }

        private static void WriteSshMpint(MemoryStream ms, byte[] data)
        {
            // Strip leading zero bytes (but keep at least one byte).
            int start = 0;
            while (start < data.Length - 1 && data[start] == 0) start++;
            var trimmed = data[start..];

            // Positive mpint must not have its high bit set — prepend 0x00 if it does.
            if ((trimmed[0] & 0x80) != 0)
            {
                var padded = new byte[trimmed.Length + 1];
                trimmed.CopyTo(padded, 1);
                trimmed = padded;
            }
            WriteSshString(ms, trimmed);
        }
    }

    [CollectionDefinition("SFTP collection")]
    public class SftpCollection : ICollectionFixture<SftpFixture> { }
}
