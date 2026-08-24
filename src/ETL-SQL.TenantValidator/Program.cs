using System.Text.Json;
using ETL_SQL.Core.Portability;

if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
{
    Console.WriteLine("Usage: etl-sql-tenant-validator --bundle PATH [--operator-key PATH] " +
                      "[--require-signature] [--recipient-key PATH]\n" +
                      "Set ETLSQL_TENANT_RECIPIENT_PASSPHRASE when the recipient key is protected.");
    return args.Length == 0 ? 2 : 0;
}

string? Value(string name)
{
    var index = Array.IndexOf(args, name);
    if (index < 0) return null;
    if (index + 1 == args.Length) throw new ArgumentException($"{name} requires a value.");
    return args[index + 1];
}

try
{
    var bundle = Value("--bundle") ?? throw new ArgumentException("--bundle is required.");
    var result = await TenantBundleValidator.ValidateAsync(bundle, new TenantBundleValidator.Options(
        OperatorPublicKeyFile: Value("--operator-key"),
        RequireSignature: args.Contains("--require-signature", StringComparer.Ordinal),
        RecipientPrivateKeyFile: Value("--recipient-key"),
        RecipientPassphrase: Environment.GetEnvironmentVariable("ETLSQL_TENANT_RECIPIENT_PASSPHRASE")));
    Console.WriteLine(JsonSerializer.Serialize(new
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
    Console.Error.WriteLine(ex.Message);
    return 2;
}
