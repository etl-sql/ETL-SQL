using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Services;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the GENERATE JWT_SECRET statement.
/// Generates a random 256-bit key and encrypts it with the machine key. It does not change any
/// configuration file; the operator installs the value.
/// The secret is returned as a one-row result set and is never passed to a logger: a job's log is
/// kept, and a result set is only shown to whoever ran the statement.
/// </summary>
public class GenerateJwtSecretStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;

    public Type SupportedStatementType => typeof(GenerateJwtSecretStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        _logger.Info("Generating new JWT secret...");

        // 1. Generate 256-bit secret (32 bytes)
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        var secret = Convert.ToBase64String(bytes);

        // 2. Encrypt using Machine Key
        var machineKey = SecurityService.GetMachineKey();
        var encryptedSecret = CryptoUtils.Encrypt(secret, machineKey);

        var result = new ETL_SQL.Data.DataTable();
        result.SetColumns(new[] { "Secret", "EncryptedConfigValue" });
        var row = result.NewRow();
        row["Secret"] = secret;
        row["EncryptedConfigValue"] = encryptedSecret;
        await result.AddRowAsync(row);
        result.TotalRowsMatched = 1;

        context.LastResult = result;
        context.LastResultSets.Add(result);
        context.OnResultSet?.Invoke(result);
        if (!context.RedirectOutput) ResultFormatter.PrintBatch(result, true);

        context.Log("JWT secret generated. Record the Secret value in your password manager: it cannot be recovered from the encrypted value if the machine key changes.", ConsoleColor.Yellow);

        // COMPAT_BREAK: 0.22.0 — the statement no longer rewrites the running application's
        // appsettings.json. It wrote Security:JwtSecret, which nothing reads, and it let any hosted
        // script change the service's own configuration. Operators install the value with the CLI.
        context.Log("To install it, run 'etl-sql config setup-jwt --update' on the Portal host, or set Portal:Jwt:Secret to the encrypted value.", ConsoleColor.Yellow);
    }
}
