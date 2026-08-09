#nullable enable

using System.Text.Json;

namespace ETL_SQL.TestSupport;

/// <summary>
/// Writes opt-in machine-readable scenario evidence for the deployment-profile certification
/// runner. Normal test runs do not create files.
/// </summary>
public static class DeploymentCertificationEvidenceWriter
{
    public const string EvidenceDirectoryEnvironmentVariable =
        "ETLSQL_DEPLOYMENT_CERT_EVIDENCE_DIR";

    public static async Task WriteAsync(
        string scenarioId,
        object evidence,
        CancellationToken cancellationToken = default)
    {
        var directory = Environment.GetEnvironmentVariable(EvidenceDirectoryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(directory)) return;

        var safeName = string.Concat(scenarioId.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, safeName + ".json");
        var temporaryPath = path + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        var payload = JsonSerializer.Serialize(evidence, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(temporaryPath, payload, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
