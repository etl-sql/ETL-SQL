using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using PgpCore;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the CREATE PGP_KEY_PAIR statement, generating OpenPGP key pairs for secure file encryption and signing.
    /// Supports RSA keys with customizable bit length and identity.
    /// </summary>
    public class CreatePgpKeyPairStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(CreatePgpKeyPairStatement);

        public CreatePgpKeyPairStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreatePgpKeyPairStatement)statement;

            var pathVal = (await context.EvaluateValue(stmt.Path, new Row()))?.ToString();
            if (string.IsNullOrEmpty(pathVal)) throw new ExecutionException("Path must be specified for CREATE PGP_KEY_PAIR.");

            string path = context.ResolvePath(pathVal);

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would generate PGP key pair at {path}", ConsoleColor.Yellow);
                return;
            }

            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            int bits = 2048;
            if (stmt.Bits != null)
            {
                var bitsVal = await context.EvaluateValue(stmt.Bits, new Row());
                if (bitsVal != null) bits = Convert.ToInt32(bitsVal);
            }

            string identity = "ETL-SQL User <user@etl-sql.local>";
            if (stmt.Identity != null)
            {
                identity = (await context.EvaluateValue(stmt.Identity, new Row()))?.ToString() ?? identity;
            }

            string? passphrase = null;
            if (stmt.Passphrase != null)
            {
                passphrase = (await context.EvaluateValue(stmt.Passphrase, new Row(), decryptSensitive: true))?.ToString();
            }

            _logger.WriteLine($"Generating PGP key pair (RSA {bits}) for {identity} at {path}...");

            string privateKeyFile = Path.Combine(path, "private.asc");
            string publicKeyFile = Path.Combine(path, "public.asc");

            using (PGP pgp = new PGP())
            {
                await pgp.GenerateKeyAsync(new FileInfo(publicKeyFile), new FileInfo(privateKeyFile), identity, passphrase, bits);
            }

            _logger.WriteLine($"PGP key pair generated successfully.");
            _logger.Debug("Private key saved to: {PrivateKeyFile}", privateKeyFile);
            _logger.Debug("Public key saved to: {PublicKeyFile}", publicKeyFile);
        }
    }
}
