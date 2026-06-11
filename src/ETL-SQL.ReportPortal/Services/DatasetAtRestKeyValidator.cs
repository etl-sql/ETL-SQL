namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Validates the portal at-rest key configuration. Pure (no host dependency) so the fail-closed rules
/// can be unit-tested directly. <see cref="DatasetAtRestKeyValidationService"/> enforces the result at
/// startup. The portal must not silently fall back to host-bound ENCRYPT=MACHINE in production.
/// </summary>
public static class DatasetAtRestKeyValidator
{
    public enum Severity { Ok, Warn, Fatal }

    public readonly record struct Result(Severity Severity, string? Message)
    {
        public static readonly Result Ok = new(Severity.Ok, null);
        public static Result Warn(string m)  => new(Severity.Warn, m);
        public static Result Fatal(string m) => new(Severity.Fatal, m);
    }

    /// <summary>Minimum decoded key length: 256 bits.</summary>
    public const int MinKeyBytes = 32;

    public static Result Validate(DatasetConfig config)
    {
        var key = config.AtRestKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            return config.AllowMachineFallback
                ? Result.Warn(
                    "Portal:Dataset:AtRestKey is not set — dataset caches use host-bound ENCRYPT=MACHINE " +
                    "encryption and are NOT portable across hosts. Set a key for production.")
                : Result.Fatal(
                    "Portal:Dataset:AtRestKey is required. Set a base64 key (32+ bytes), or set " +
                    "Portal:Dataset:AllowMachineFallback=true for dev/standalone (host-bound caches).");
        }

        byte[] decoded;
        try
        {
            decoded = System.Convert.FromBase64String(key);
        }
        catch (System.FormatException)
        {
            return Result.Fatal("Portal:Dataset:AtRestKey is not valid base64.");
        }

        if (decoded.Length < MinKeyBytes)
            return Result.Fatal(
                $"Portal:Dataset:AtRestKey decodes to {decoded.Length} bytes; at least {MinKeyBytes} bytes (256 bits) are required.");

        return Result.Ok;
    }
}
