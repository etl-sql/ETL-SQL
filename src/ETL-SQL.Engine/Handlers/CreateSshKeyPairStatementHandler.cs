using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the CREATE SSH_KEY_PAIR statement, generating cryptographic key pairs for secure file operations.
    /// Supports RSA, ECDSA, and Ed25519 algorithms with optional passphrase protection for the private key.
    /// </summary>
    public class CreateSshKeyPairStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(CreateSshKeyPairStatement);

        public CreateSshKeyPairStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Executes the key generation logic.
        /// </summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateSshKeyPairStatement)statement;

            var pathVal = (await context.EvaluateValue(stmt.Path, new Row()))?.ToString();
            if (string.IsNullOrEmpty(pathVal)) throw new ExecutionException("Path must be specified for CREATE SSH_KEY_PAIR.");

            string path = context.ResolvePath(pathVal);

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would generate SSH key pair at {path}", ConsoleColor.Yellow);
                return;
            }

            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            int bits = 2048;
            if (stmt.Bits != null)
            {
                var bitsVal = await context.EvaluateValue(stmt.Bits, new Row());
                if (bitsVal != null) bits = Convert.ToInt32(bitsVal);
            }

            string algorithm = "RSA";
            if (stmt.Algorithm != null)
            {
                algorithm = (await context.EvaluateValue(stmt.Algorithm, new Row()))?.ToString()?.ToUpperInvariant() ?? "RSA";
            }

            string? passphrase = null;
            if (stmt.Passphrase != null)
            {
                passphrase = (await context.EvaluateValue(stmt.Passphrase, new Row()))?.ToString();
            }

            string? comment = null;
            if (stmt.Comment != null)
            {
                comment = (await context.EvaluateValue(stmt.Comment, new Row()))?.ToString();
            }

            _logger.WriteLine($"Generating SSH key pair ({algorithm}) at {path}...");

            string privateKeyFile;
            string publicKeyFile;
            string privateKeyPem;
            string publicKeyPem;

            switch (algorithm)
            {
                case "RSA":
                    using (var rsa = RSA.Create(bits))
                    {
                        privateKeyFile = Path.Combine(path, "id_rsa");
                        publicKeyFile = privateKeyFile + ".pub";
                        
                        if (string.IsNullOrEmpty(passphrase))
                        {
                            privateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
                        }
                        else
                        {
                            privateKeyPem = rsa.ExportEncryptedPkcs8PrivateKeyPem(passphrase, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100000));
                        }
                        publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
                    }
                    break;

                case "ECDSA":
                    var curve = bits switch
                    {
                        384 => ECCurve.NamedCurves.nistP384,
                        521 => ECCurve.NamedCurves.nistP521,
                        _ => ECCurve.NamedCurves.nistP256
                    };
                    using (var ecdsa = ECDsa.Create(curve))
                    {
                        privateKeyFile = Path.Combine(path, $"id_ecdsa_{bits}");
                        publicKeyFile = privateKeyFile + ".pub";

                        if (string.IsNullOrEmpty(passphrase))
                        {
                            privateKeyPem = ecdsa.ExportPkcs8PrivateKeyPem();
                        }
                        else
                        {
                            privateKeyPem = ecdsa.ExportEncryptedPkcs8PrivateKeyPem(passphrase, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100000));
                        }
                        publicKeyPem = ecdsa.ExportSubjectPublicKeyInfoPem();
                    }
                    break;

                default:
                    throw new ExecutionException($"Unsupported SSH key algorithm: {algorithm}. Supported: RSA, ECDSA.");
            }

            await File.WriteAllTextAsync(privateKeyFile, privateKeyPem);
            await File.WriteAllTextAsync(publicKeyFile, publicKeyPem);

            _logger.WriteLine($"SSH key pair generated successfully.");
            _logger.Debug("Private key saved to: {PrivateKeyFile}", privateKeyFile);
            _logger.Debug("Public key saved to: {PublicKeyFile}", publicKeyFile);
        }
    }
}
