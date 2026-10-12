using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Metadata;
using ETL_SQL.Core.Services;
using ETL_SQL.LSP;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace ETL_SQL.LanguageServer.Tests;

public sealed class BugAuditTests
{
    private static readonly DocumentUri Uri = DocumentUri.From("untitled:audit.etlsql");

    [Theory]
    [InlineData(0, 500)]
    [InlineData(500, 500)]
    [InlineData(0, -1)]
    [InlineData(-1, 0)]
    public async Task CompletionAcceptsStaleOrInvalidPositions(int line, int character)
    {
        var store = new DocumentStateStore();
        store.UpdateText(Uri, "SEL");
        var metadata = new MetadataManager(ETL_SQL.Common.NullLogger.Instance, new ETL_SQL.Data.ConnectorRegistry());
        var provider = new CompletionProvider(NullLogger<CompletionProvider>.Instance, store,
            new LanguageService(metadata, new LanguageHelpRegistry()), new DatasetStore(NullLogger<DatasetStore>.Instance));
        var result = await provider.Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(Uri),
            Position = new Position(line, character)
        }, CancellationToken.None);
        Assert.NotNull(result);
        if (line == 0 && character == 500) Assert.Contains(result, item => item.Label == "SELECT");
    }

    [Fact]
    public async Task DebouncedFailureIsObservedAndLaterWorkStillRuns()
    {
        var log = new Mock<ILogger>();
        var factory = new Mock<ILoggerFactory>();
        factory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(log.Object);
        var handler = Handler(factory.Object);
        var pending = new ConcurrentDictionary<DocumentUri, CancellationTokenSource>();
        var failure = new InvalidOperationException("analysis failed");
        await ScheduleAsync(handler, pending, 0, () => Task.FromException(failure));
        Assert.Empty(pending);
        log.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), failure,
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        var ran = false;
        await ScheduleAsync(handler, pending, 0, () => { ran = true; return Task.CompletedTask; });
        Assert.True(ran);
    }

    [Fact]
    public async Task ReplacementCancelsOnlyTheOlderWork()
    {
        var handler = Handler(NullLoggerFactory.Instance);
        var pending = new ConcurrentDictionary<DocumentUri, CancellationTokenSource>();
        var oldRan = false;
        var old = ScheduleAsync(handler, pending, Timeout.Infinite, () => { oldRan = true; return Task.CompletedTask; });
        var currentRan = false;
        var current = ScheduleAsync(handler, pending, 0, () => { currentRan = true; return Task.CompletedTask; });
        await Task.WhenAll(old, current).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(oldRan);
        Assert.True(currentRan);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task CloseCancelsBothAnalysisQueues()
    {
        var handler = Handler(NullLoggerFactory.Instance);
        var tasks = new System.Collections.Generic.List<Task>();
        foreach (var field in new[] { "_debouncers", "_lintDebouncers" })
        {
            var pending = (ConcurrentDictionary<DocumentUri, CancellationTokenSource>)typeof(TextDocumentHandler)
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(handler);
            tasks.Add(ScheduleAsync(handler, pending, Timeout.Infinite, () => throw new Exception("closed work ran")));
        }
        await handler.Handle(new DidCloseTextDocumentParams { TextDocument = new TextDocumentIdentifier(Uri) }, CancellationToken.None);
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static TextDocumentHandler Handler(ILoggerFactory factory) => new(factory,
        new MetadataManager(ETL_SQL.Common.NullLogger.Instance, new ETL_SQL.Data.ConnectorRegistry()), new DocumentStateStore());

    private static Task ScheduleAsync(TextDocumentHandler handler, ConcurrentDictionary<DocumentUri, CancellationTokenSource> pending,
        int delay, Func<Task> action) => (Task)typeof(TextDocumentHandler)
        .GetMethod("ScheduleDebouncedAsync", BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(handler, new object[] { pending, Uri, delay, action });
}
