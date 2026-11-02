using System.Text.Json;
using ETL_SQL.Core.Portability;

namespace ETL_SQL.TenantValidator;

internal static class Program
{
    private static Task<int> Main(string[] args) => TenantValidatorCommand.RunAsync(args, Console.Out, Console.Error);
}

/// <summary>
/// The validator's command line. Exit codes: 0 valid, 3 invalid, 2 the bundle could not be validated
/// (usage error, unreadable file, or a manifest the validator could not interpret).
/// </summary>
internal static class TenantValidatorCommand
{
    private static readonly string[] ValueOptions = ["--bundle", "--operator-key", "--recipient-key"];
    private static readonly string[] SwitchOptions = ["--require-signature"];

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
        {
            stdout.WriteLine("Usage: etl-sql-tenant-validator --bundle PATH [--operator-key PATH] " +
                             "[--require-signature] [--recipient-key PATH]\n" +
                             "Set ETLSQL_TENANT_RECIPIENT_PASSPHRASE when the recipient key is protected.");
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            var options = Parse(args);
            var bundle = options.GetValueOrDefault("--bundle") ?? throw new ArgumentException("--bundle is required.");
            var result = await TenantBundleValidator.ValidateAsync(bundle, new TenantBundleValidator.Options(
                OperatorPublicKeyFile: options.GetValueOrDefault("--operator-key"),
                RequireSignature: options.ContainsKey("--require-signature"),
                RecipientPrivateKeyFile: options.GetValueOrDefault("--recipient-key"),
                RecipientPassphrase: Environment.GetEnvironmentVariable("ETLSQL_TENANT_RECIPIENT_PASSPHRASE")));
            stdout.WriteLine(JsonSerializer.Serialize(new
            {
                valid = result.IsValid,
                tenant = result.Manifest?.TenantExportIdentity,
                consistencyPoint = result.Manifest?.ConsistencyPoint,
                findings = result.Findings
            }, new JsonSerializerOptions { WriteIndented = true }));
            return result.IsValid ? 0 : 3;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine(ex.Message);
            return 2;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A manifest the validator cannot interpret (a null where a list belongs, a count that
            // overflows) is a bundle it could not validate. That is a verdict for the caller, not a
            // crash with a stack trace and an exit code nobody scripted for.
            stderr.WriteLine($"The bundle could not be validated: {ex.GetType().Name}.");
            return 2;
        }
    }

    /// <summary>
    /// Refuses anything it does not recognise. Ignoring an unknown flag turned a misspelt
    /// <c>--require-signature</c> into a silent pass for an unsigned bundle, and taking the next flag
    /// as an option's value swallowed the flag the same way.
    /// </summary>
    private static Dictionary<string, string?> Parse(string[] args)
    {
        var parsed = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i];
            if (parsed.ContainsKey(name))
                throw new ArgumentException($"{name} is given more than once.");
            if (SwitchOptions.Contains(name, StringComparer.Ordinal))
            {
                parsed[name] = null;
            }
            else if (ValueOptions.Contains(name, StringComparer.Ordinal))
            {
                if (i + 1 == args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"{name} requires a value.");
                parsed[name] = args[++i];
            }
            else
            {
                throw new ArgumentException($"Unknown argument '{name}'. Run with --help for usage.");
            }
        }
        return parsed;
    }
}
