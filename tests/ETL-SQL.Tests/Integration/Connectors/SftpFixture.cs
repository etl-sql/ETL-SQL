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
            _keyDir = Path.Combine(Path.GetTempPath(), $"sftp_keys_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_keyDir);

            var (privatePem, authorizedKeyLine) = GenerateRsaKeyPair();
            PrivateKeyPath = Path.Combine(_keyDir, "test_id_rsa.pem");
            File.WriteAllText(PrivateKeyPath, privatePem);

            var encryptedPem = GenerateEncryptedPem(privatePem, TestPassphrase);
            EncryptedPrivateKeyPath = Path.Combine(_keyDir, "test_id_rsa_enc.pem");
            File.WriteAllText(EncryptedPrivateKeyPath, encryptedPem);

            // No bind mount — Windows Docker Desktop maps host directories with 777
            // permissions, causing sshd StrictModes to reject authorized_keys.
            // We inject the key via ExecAsync after startup instead.
            _container = new ContainerBuilder("atmoz/sftp:latest")
                .WithName("etl-sql-sftp")
                .WithLabel("test-suite", "ETL-SQL.Integration")
                .WithCommand($"{TestUser}:{TestPassword}:::{RemoteUploadDir}")
                .WithPortBinding(22, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server listening on"))
                .Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(22);

            // Inject the public key directly into authorized_keys with correct ownership
            // and permissions so sshd accepts it regardless of host OS.
            var key = authorizedKeyLine.Trim();
            var result = await _container.ExecAsync(new[]
            {
                "/bin/sh", "-c",
                $"mkdir -p /home/{TestUser}/.ssh" +
                $" && printf '%s\\n' '{key}' >> /home/{TestUser}/.ssh/authorized_keys" +
                $" && chmod 700 /home/{TestUser}/.ssh" +
                $" && chmod 600 /home/{TestUser}/.ssh/authorized_keys" +
                $" && chown -R {TestUser} /home/{TestUser}/.ssh"
            });

            if (result.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Failed to inject SFTP authorized key (exit {result.ExitCode}): {result.Stderr}");
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
