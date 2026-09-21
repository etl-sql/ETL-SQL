using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public class AsyncPdfExportTests
{
    [Theory]
    [InlineData(PdfExportMode.Static)]
    [InlineData(PdfExportMode.Hosted)]
    [InlineData(PdfExportMode.Browser)]
    [InlineData(PdfExportMode.Auto)]
    public async Task Export_AwaitsPendingBackend_AndForwardsCancellation(PdfExportMode mode)
    {
        using var cancellation = new CancellationTokenSource();
        var backend = new PendingExporter();
        var exporter = new ReportPdfExporter(backend, backend);
        var manifest = new ReportManifest();
        var options = new PdfExportOptions { Mode = mode, Host = "http://localhost/report" };

        var pending = exporter.ExportAsync(manifest, options, cancellation.Token);

        Assert.False(pending.IsCompleted);
        Assert.Same(manifest, backend.Manifest);
        Assert.Same(options, backend.Options);
        Assert.Equal(cancellation.Token, backend.Token);
        byte[] expected = [0x25, 0x50, 0x44, 0x46];
        backend.Completion.SetResult(expected);
        Assert.Same(expected, await pending);
    }

    [Fact]
    public async Task AutoExport_CallerCancellation_DoesNotStartFallback()
    {
        using var cancellation = new CancellationTokenSource();
        var backend = new PendingExporter();
        var fallback = new PendingExporter();
        var exporter = new ReportPdfExporter(fallback, backend);
        var warnings = new List<string>();
        var pending = exporter.ExportAsync(new ReportManifest(),
            new PdfExportOptions { Mode = PdfExportMode.Auto, Host = "http://localhost/report", Warn = warnings.Add },
            cancellation.Token);

        cancellation.Cancel();
        backend.Completion.SetCanceled(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(fallback.Manifest);
        Assert.Empty(warnings);
    }

    private sealed class PendingExporter : IReportPdfExporter
    {
        public TaskCompletionSource<byte[]> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ReportManifest? Manifest { get; private set; }
        public PdfExportOptions? Options { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<byte[]> ExportAsync(ReportManifest manifest, PdfExportOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Manifest = manifest;
            Options = options;
            Token = cancellationToken;
            return Completion.Task;
        }
    }
}
