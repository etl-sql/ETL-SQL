using ETL_SQL.Common;
using ETL_SQL.Reporting;
using Moq;

namespace ETL_SQL.Tests.Reporting;

public class PdfTemporaryImageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialImageIsDeletedWhenWriteFailsOrIsCancelled(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var exporter = new FailingExporter(cancellation, cancel);
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporter.ExportAsync(Manifest(), cancellation.Token));
        else
            await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(Manifest(), cancellation.Token));
        Assert.NotNull(exporter.PathWritten);
        Assert.False(File.Exists(exporter.PathWritten));
    }

    [Fact]
    public async Task CleanupFailureIsLoggedAndCannotLookSuccessful()
    {
        using var cancellation = new CancellationTokenSource();
        var logger = new Mock<ILogger>();
        var exporter = new FailingExporter(cancellation, false, logger.Object) { FailDelete = true };
        try
        {
            var failure = await Assert.ThrowsAsync<AggregateException>(() => exporter.ExportAsync(Manifest()));
            Assert.Contains("operator cleanup", failure.Message);
            Assert.True(File.Exists(exporter.PathWritten));
            logger.Verify(l => l.Warning(It.Is<string>(s => s.Contains("cleanup failed")), It.IsAny<object?[]>()), Times.Once);
        }
        finally { if (exporter.PathWritten != null) File.Delete(exporter.PathWritten); }
    }

    private static ReportManifest Manifest() => new()
    {
        Visuals = [new VisualManifest { Name = "Image", VisualType = "IMAGE", Options = new() { ["SRC"] = "data:image/png;base64,AQID" } }]
    };

    private sealed class FailingExporter(CancellationTokenSource cancellation, bool cancel, ILogger? logger = null) : PdfExporter(logger)
    {
        public string? PathWritten { get; private set; }
        public bool FailDelete { get; init; }
        protected override async Task WriteTemporaryImageAsync(string path, byte[] bytes, CancellationToken cancellationToken)
        {
            PathWritten = path;
            await File.WriteAllBytesAsync(path, bytes.AsMemory(0, 1).ToArray(), cancellationToken);
            Assert.True(File.Exists(path));
            if (cancel)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            throw new IOException("Synthetic partial-write failure");
        }
        protected override void DeleteTemporaryImage(string path)
        {
            if (FailDelete) throw new IOException("Synthetic delete failure");
            base.DeleteTemporaryImage(path);
        }
    }
}
