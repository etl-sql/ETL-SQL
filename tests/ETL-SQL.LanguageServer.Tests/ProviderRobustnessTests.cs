using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Metadata;
using ETL_SQL.Core.Services;
using ETL_SQL.LSP;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using Xunit;
using LSPRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace ETL_SQL.LanguageServer.Tests;

/// <summary>
/// Signature help, stale cursor/range coordinates, per-document cleanup, and fast/lint ordering.
/// </summary>
public sealed class ProviderRobustnessTests
{
    private static readonly DocumentUri Uri = DocumentUri.From("untitled:robust.etlsql");

    private static DocumentStateStore StoreWith(string text)
    {
        var store = new DocumentStateStore();
        store.UpdateText(Uri, text);
        return store;
    }

    private static SignatureHelpProvider SignatureHelp(DocumentStateStore store) => new(
        NullLogger<SignatureHelpProvider>.Instance,
        new MetadataManager(ETL_SQL.Common.NullLogger.Instance, new ETL_SQL.Data.ConnectorRegistry()),
        store);

    [Fact]
    public async Task SignatureHelp_HandleResolvesFromTheSyncedDocument()
    {
        var store = StoreWith("SELECT 1;\nSELECT SUBSTRING(name, 1");
        var result = await SignatureHelp(store).Handle(new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier(Uri),
            Position = new Position(1, 24)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.StartsWith("SUBSTRING(", result!.Signatures.Single().Label);
        Assert.Equal(1, result.ActiveParameter);
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(9, 0)]
    [InlineData(-1, 0)]
    public async Task SignatureHelp_HandleToleratesStalePositions(int line, int character)
    {
        var store = StoreWith("SELECT NOW(");
        var result = await SignatureHelp(store).Handle(new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier(Uri),
            Position = new Position(line, character)
        }, CancellationToken.None);
        if (line == 0) Assert.Equal(0, result!.ActiveParameter);
        else Assert.Null(result);
    }

    [Fact]
    public void SignatureHelp_ZeroArgumentFunctionNeverReportsNegativeActiveParameter()
    {
        var result = SignatureHelp(new DocumentStateStore()).Resolve("SELECT CUME_DIST(", 17);
        Assert.NotNull(result);
        Assert.Equal(0, result!.ActiveParameter);
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(0, -3)]
    [InlineData(-1, 0)]
    public async Task Definition_ToleratesOutOfRangeCoordinates(int line, int character)
    {
        var store = StoreWith("DECLARE @x INT = 1;\nPRINT @x;");
        var provider = new DefinitionProvider(NullLogger<DefinitionProvider>.Instance, store);
        await provider.Handle(new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier(Uri),
            Position = new Position(line, character)
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(0, -3)]
    [InlineData(-1, 0)]
    public async Task Hover_ToleratesOutOfRangeCoordinates(int line, int character)
    {
        var store = StoreWith("SELECT NOW();");
        var provider = new HoverProvider(NullLogger<HoverProvider>.Instance, store,
            new ETL_SQL.Engine.Functions.FunctionRegistry(), new LanguageHelpRegistry(),
            new DatasetStore(NullLogger<DatasetStore>.Instance));
        await provider.Handle(new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(Uri),
            Position = new Position(line, character)
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(0, 500, 0, 600)]
    [InlineData(0, 3, 0, 1)]
    [InlineData(0, 2, 1, 900)]
    public async Task RangeFormatting_ClampsStaleRanges(int startLine, int startChar, int endLine, int endChar)
    {
        var store = StoreWith("select 1;\nselect 2;");
        var config = new Mock<ILanguageServerConfiguration>();
        config.Setup(c => c.GetSection(It.IsAny<string>()))
            .Returns((string key) => new ConfigurationBuilder().Build().GetSection(key));
        var provider = new FormattingProvider(NullLogger<FormattingProvider>.Instance, store, config.Object);

        var result = await provider.Handle(new DocumentRangeFormattingParams
        {
            TextDocument = new TextDocumentIdentifier(Uri),
            Range = new LSPRange(startLine, startChar, endLine, endChar),
            Options = new FormattingOptions()
        }, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public void RemoveState_ClearsDiagnosticsAndNotebookContext()
    {
        var store = StoreWith("SELECT 1;");
        store.SetParserDiagnostics(Uri, [new Diagnostic { Message = "stale" }]);
        store.SetNotebookContext(Uri.ToString(), "DECLARE @p INT = 1;", "C:/nb.etlnb");

        store.RemoveState(Uri);

        Assert.Empty(store.GetParserDiagnostics(Uri));
        Assert.Equal("", store.GetNotebookPrefix(Uri.ToString()));
        Assert.Null(store.GetNotebookPath(Uri.ToString()));
        Assert.Null(store.GetDocumentText(Uri));
    }

    [Fact]
    public async Task LintPass_WaitsForTheFastPassOfTheSameText()
    {
        var store = new DocumentStateStore();
        var metadata = new MetadataManager(ETL_SQL.Common.NullLogger.Instance, new ETL_SQL.Data.ConnectorRegistry())
        {
            SchemaCacheDirectory = null,
            DisableBackgroundRefresh = true
        };
        var handler = new TextDocumentHandler(NullLoggerFactory.Instance, metadata, store);
        const string oldText = "SELECT 1;";
        const string newText = "SELECT 2;\nSELECT 3;";
        await handler.FastAnalyzeAsync(Uri, oldText);

        // A fast pass for the new text is still parsing when the lint debounce fires.
        var parsing = new TaskCompletionSource();
        var fastPasses = (ConcurrentDictionary<DocumentUri, (string Text, TaskCompletionSource Done)>)Field(handler, "_fastPasses");
        fastPasses[Uri] = (newText, parsing);
        var lint = (Task)typeof(TextDocumentHandler)
            .GetMethod("RunLintPassAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(handler, [Uri, newText])!;

        await Task.Delay(200);
        Assert.False(lint.IsCompleted, "lint ran against the previous document's AST");

        await handler.FastAnalyzeAsync(Uri, newText);
        parsing.SetResult();
        await lint.WaitAsync(TimeSpan.FromSeconds(30));

        var lintResults = (ConcurrentDictionary<DocumentUri, (string Text, List<Diagnostic> Diagnostics)>)Field(handler, "_lintResults");
        Assert.Equal(newText, lintResults[Uri].Text);
        Assert.Equal(2, store.GetState(Uri)!.Script.Statements.Count);
    }

    private static object Field(TextDocumentHandler handler, string name) =>
        typeof(TextDocumentHandler).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!;
}
