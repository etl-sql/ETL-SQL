using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Services;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the GENERATE JWT_SECRET statement.
/// Generates a random 256-bit key, encrypts it with the machine key, and updates the portal configuration.
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

        context.Log("JWT SETUP - CRITICAL SECURITY INFORMATION", ConsoleColor.Yellow);
        context.Log("GENERATED PLAIN-TEXT SECRET:", ConsoleColor.Red);
        context.Log(secret, ConsoleColor.Yellow);
        context.Log("ENCRYPTED CONFIG VALUE (Machine-Bound):", ConsoleColor.White);
        context.Log(encryptedSecret, ConsoleColor.Cyan);
        context.Log("IMPORTANT: Record the plain-text secret in your password manager. It cannot be recovered from the encrypted value if the machine key changes.", ConsoleColor.Red);

        // 4. Update appsettings.json if it exists in the execution directory
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        if (File.Exists(configPath))
        {
            try
            {
                string json = await File.ReadAllTextAsync(configPath);
                var root = JsonNode.Parse(json);
                if (root != null)
                {
                    var security = root["Security"];
                    if (security == null)
                    {
                        root["Security"] = new JsonObject();
                        security = root["Security"];
                    }
                    security!["JwtSecret"] = encryptedSecret;

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    await File.WriteAllTextAsync(configPath, root.ToJsonString(options));
                    context.Log("SUCCESS: Updated appsettings.json with the encrypted secret.", ConsoleColor.Green);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to update appsettings.json: {Message}", ex, ex.Message);
                context.Log($"ERROR: Could not update appsettings.json automatically: {ex.Message}", ConsoleColor.Red);
            }
        }
        else
        {
            context.Log("NOTE: appsettings.json not found in the application directory. Please update your configuration manually using the encrypted value above.", ConsoleColor.Yellow);
        }
    }
}
