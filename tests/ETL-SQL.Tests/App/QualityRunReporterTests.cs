using System.Text.Json;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Quality;

namespace ETL_SQL.Tests.App;

public sealed class QualityRunReporterTests
{
    [Fact]
    public async Task Evidence_IsVersionedCountsOnlyAndRedacted()
    {
        var report = new DataQualityReport();
        report.RecordRowValidated();
        report.RecordRowWarned();
        report.RecordFailure("email", "MATCHES .+@.+", FailAction.Warn, "person@example.test", true, "steward");
        var evidence = QualityRunReporter.Create(
            "pipeline.etlsql", 1, "FAILED", 1, report, "PASSWORD=super-secret");

        var path = Path.Combine(Path.GetTempPath(), $"quality-evidence-{Guid.NewGuid():N}.json");
        try
        {
            await QualityRunReporter.WriteAsync(path, evidence);
            var json = await File.ReadAllTextAsync(path);
            using var document = JsonDocument.Parse(json);

            Assert.Equal("1.0", document.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal(1, document.RootElement.GetProperty("totalFailures").GetInt64());
            Assert.DoesNotContain("person@example.test", json, StringComparison.Ordinal);
            Assert.DoesNotContain("super-secret", json, StringComparison.Ordinal);
            Assert.False(document.RootElement.GetProperty("ruleFailures")[0].TryGetProperty("samples", out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Summary_IsStableAndDoesNotContainSamples()
    {
        var report = new DataQualityReport();
        report.RecordRowValidated();
        report.RecordRowQuarantined();
        report.RecordFailure("ssn", "NOT NULL", FailAction.Quarantine, "123-45-6789", true);
        var evidence = QualityRunReporter.Create("pipeline.etlsql", 0, "COMPLETED", 1, report);
        using var writer = new StringWriter();

        QualityRunReporter.WriteSummary(writer, evidence);

        var text = writer.ToString();
        Assert.Contains("Data Quality Summary", text, StringComparison.Ordinal);
        Assert.Contains("ssn | QUARANTINE | NOT NULL | 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("123-45-6789", text, StringComparison.Ordinal);
    }
}
