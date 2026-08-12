using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("Connector", "FILE")]
[Trait("Category", "TransactionalFileCertification")]
public sealed class TransactionalFilePublicationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "etlsql-transactional-file-" + Guid.NewGuid().ToString("N"));

    public TransactionalFilePublicationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void TransactionalStagesAreCollisionSafeAndRemainBesideTheTarget()
    {
        var target = Path.Combine(_directory, "out.csv");
        var stages = Enumerable.Range(0, 32)
            .Select(_ => FileConnectorPathHelper.GetStagingFilePath(target, transactional: true))
            .ToArray();

        Assert.Equal(32, stages.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(stages, stage => Assert.Equal(_directory, Path.GetDirectoryName(stage)));
        Assert.All(stages, stage => Assert.StartsWith(target + ".etl-stage-", stage, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PublishReplacesTheTargetWithoutDeletingItFirst()
    {
        var target = Path.Combine(_directory, "out.json");
        File.WriteAllText(target, "old");
        var stage = FileConnectorPathHelper.GetStagingFilePath(target, transactional: true);
        File.WriteAllText(stage, "new");

        FileConnectorPathHelper.PublishStagedFile(stage, target, transactional: true);

        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(File.Exists(stage));
    }

    [Fact]
    public void TransactionalPublishRejectsCrossDirectoryStagesAndPreservesTheTarget()
    {
        var other = Path.Combine(_directory, "other");
        Directory.CreateDirectory(other);
        var target = Path.Combine(_directory, "out.xml");
        var stage = Path.Combine(other, "out.xml.stage");
        File.WriteAllText(target, "prior");
        File.WriteAllText(stage, "candidate");

        Assert.Throws<InvalidOperationException>(() =>
            FileConnectorPathHelper.PublishStagedFile(stage, target, transactional: true));
        Assert.Equal("prior", File.ReadAllText(target));
        Assert.Equal("candidate", File.ReadAllText(stage));
    }

    [Fact]
    public void ReconciliationRemovesOnlyOldStagesForTheExactTarget()
    {
        var target = Path.Combine(_directory, "out.parquet");
        var oldStage = target + ".etl-stage-old";
        var freshStage = target + ".etl-stage-fresh";
        var unrelated = Path.Combine(_directory, "other.parquet.etl-stage-old");
        File.WriteAllText(oldStage, "old");
        File.WriteAllText(freshStage, "fresh");
        File.WriteAllText(unrelated, "unrelated");
        File.SetLastWriteTimeUtc(oldStage, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-2));

        var removed = FileConnectorPathHelper.ReconcileStaleStagingFiles(target, TimeSpan.FromDays(1));

        Assert.Equal(1, removed);
        Assert.False(File.Exists(oldStage));
        Assert.True(File.Exists(freshStage));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public async Task ConcurrentWritersUsePrivateStagesAndPublishOnlyCompleteValues()
    {
        var target = Path.Combine(_directory, "concurrent.csv");
        var values = Enumerable.Range(0, 12).Select(i => $"complete-{i}").ToArray();
        var stages = values.Select(value =>
        {
            var stage = FileConnectorPathHelper.GetStagingFilePath(target, transactional: true);
            File.WriteAllText(stage, value);
            return stage;
        }).ToArray();

        var successfulPublishes = new ConcurrentBag<string>();
        await Task.WhenAll(stages.Select(stage => Task.Run(() =>
        {
            try
            {
                FileConnectorPathHelper.PublishStagedFile(stage, target, transactional: true);
                successfulPublishes.Add(stage);
            }
            catch (IOException) { File.Delete(stage); }
            catch (UnauthorizedAccessException) { File.Delete(stage); }
        })));

        Assert.NotEmpty(successfulPublishes);
        Assert.Contains(File.ReadAllText(target), values);
        Assert.Empty(Directory.EnumerateFiles(_directory, "concurrent.csv.etl-stage-*"));
    }

    [Fact]
    public void CleanupFailureIsNonDestructiveAndCanBeReconciledLater()
    {
        var target = Path.Combine(_directory, "locked.csv");
        var stage = target + ".etl-stage-abandoned";
        File.WriteAllText(target, "published");
        File.WriteAllText(stage, "partial");
        File.SetLastWriteTimeUtc(stage, DateTime.UtcNow.AddDays(-2));

        if (OperatingSystem.IsWindows())
        {
            using (File.Open(stage, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Equal(0, FileConnectorPathHelper.ReconcileStaleStagingFiles(target, TimeSpan.FromDays(1)));
            Assert.True(File.Exists(stage));
        }

        Assert.Equal("published", File.ReadAllText(target));
        Assert.Equal(1, FileConnectorPathHelper.ReconcileStaleStagingFiles(target, TimeSpan.FromDays(1)));
        Assert.False(File.Exists(stage));
    }

    [Fact]
    public async Task MidStreamFailurePreservesPriorTargetAndCleansItsStage()
    {
        var target = Path.Combine(_directory, "failure.csv");
        await File.WriteAllTextAsync(target, "prior");
        var source = new FlatFileDataSource(
            SystemExecutionContext.Instance,
            target,
            new Dictionary<string, string> { ["TRANSACTIONAL"] = "ON" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.WriteBatches(FailingBatchesAsync(), append: false, CancellationToken.None));

        Assert.Equal("prior", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.EnumerateFiles(_directory, "failure.csv.etl-stage-*"));
    }

    [Fact]
    public async Task CancelledConnectorWritePreservesPriorTargetAndCleansItsStage()
    {
        var target = Path.Combine(_directory, "cancel.csv");
        await File.WriteAllTextAsync(target, "prior");
        var source = new FlatFileDataSource(
            SystemExecutionContext.Instance,
            target,
            new Dictionary<string, string> { ["TRANSACTIONAL"] = "ON" });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var batch = await BuildBatchAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            source.WriteBatches(new[] { batch }.ToAsyncEnumerable(), append: false, cancellation.Token));

        Assert.Equal("prior", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.EnumerateFiles(_directory, "cancel.csv.etl-stage-*"));
    }

    private static async Task<DataTable> BuildBatchAsync()
    {
        var table = new DataTable();
        table.SetColumns(new[] { "id" });
        var row = table.NewRow();
        row["id"] = 1;
        await table.AddRowAsync(row);
        return table;
    }

    private static async IAsyncEnumerable<DataTable> FailingBatchesAsync()
    {
        yield return await BuildBatchAsync();
        throw new InvalidOperationException("simulated producer failure");
    }
}
