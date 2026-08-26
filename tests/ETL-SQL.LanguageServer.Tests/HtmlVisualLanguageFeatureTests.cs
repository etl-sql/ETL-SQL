using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Metadata;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;
using ETL_SQL.LSP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace ETL_SQL.LanguageServer.Tests;

public sealed class HtmlVisualLanguageFeatureTests
{
    private const string ScriptText = """
        DECLARE @Region VARCHAR(20) = 'West';
        CREATE VISUAL NodeCard AS HTML (
          SOURCE = (SELECT NodeName, Status FROM #nodes),
          TEMPLATE = '<article><h3>{{NodeName}}</h3><p>{{@Region}}</p></article>',
          STYLE (CSS = '.node { color: var(--etl-text); }'),
          FALLBACK = 'Node {{NodeName}} in {{@Region}}'
        );
        """;

    [Fact]
    public async Task Completion_InsideTemplateOffersFieldsAndParameters()
    {
        var (provider, uri) = CompletionProvider();
        var position = PositionAfter(ScriptText, "{{Node");

        var result = await provider.Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = position,
            Context = new CompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
        }, CancellationToken.None);

        Assert.Contains(result, item => item.Label == "NodeName" && item.Kind == CompletionItemKind.Field);
    }

    [Fact]
    public async Task Completion_InsideCssOffersOnlyApprovedThemeTokens()
    {
        var (provider, uri) = CompletionProvider();
        var position = PositionAfter(ScriptText, "var(--etl-te");

        var result = await provider.Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = position,
            Context = new CompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
        }, CancellationToken.None);

        Assert.Contains(result, item => item.Label == "--etl-text");
        Assert.DoesNotContain(result, item => item.Label.StartsWith("--", StringComparison.Ordinal) && !item.Label.StartsWith("--etl-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("NodeName", "HTML field binding", "HTML-escaped")]
    [InlineData("@Region", "HTML parameter binding", "VARCHAR")]
    public async Task Hover_ExplainsTypedEscapedBindings(string anchor, string expectedKind, string expectedDetail)
    {
        var (provider, uri) = HoverProvider();

        var result = await provider.Handle(new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(ScriptText, anchor, occurrence: anchor == "NodeName" ? 1 : 1)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(expectedKind, result!.Contents.MarkupContent.Value);
        Assert.Contains(expectedDetail, result.Contents.MarkupContent.Value);
    }

    [Fact]
    public async Task RenameFieldUpdatesSourceTemplateAndFallbackWithoutTouchingHtmlText()
    {
        var (provider, uri) = RenameProvider();

        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(ScriptText, "NodeName", 1),
            NewName = "HostName"
        }, CancellationToken.None);

        var rewritten = Apply(ScriptText, result!, uri);
        Assert.DoesNotContain("NodeName", rewritten);
        Assert.Equal(3, Count(rewritten, "HostName"));
        Assert.Contains("<article><h3>", rewritten);
    }

    [Fact]
    public async Task RenameParameterUpdatesDeclarationTemplateAndFallback()
    {
        var (provider, uri) = RenameProvider();

        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(ScriptText, "Region", 0),
            NewName = "Area"
        }, CancellationToken.None);

        var rewritten = Apply(ScriptText, result!, uri);
        Assert.DoesNotContain("@Region", rewritten);
        Assert.Equal(3, Count(rewritten, "@Area"));
    }

    [Fact]
    public async Task RenameVisualUpdatesHtmlEmbedReference()
    {
        const string script = """
            CREATE VISUAL Metric AS CARD (SOURCE = (SELECT Value FROM #metrics));
            CREATE VISUAL Wrapper AS HTML (TEMPLATE = '<section>{{VISUAL(Metric)}}</section>');
            """;
        var (provider, uri) = RenameProvider(script);

        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(script, "Metric", 0),
            NewName = "Kpi"
        }, CancellationToken.None);

        var rewritten = Apply(script, result!, uri);
        Assert.Contains("CREATE VISUAL Kpi", rewritten);
        Assert.Contains("VISUAL(Kpi)", rewritten);
    }

    private static (CompletionProvider Provider, DocumentUri Uri) CompletionProvider()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var store = Store(ScriptText, out var uri);
        var metadata = new MetadataManager(ETL_SQL.Common.NullLogger.Instance, new ETL_SQL.Data.ConnectorRegistry());
        var help = new LanguageHelpRegistry();
        return (new CompletionProvider(loggerFactory.CreateLogger<CompletionProvider>(), store,
            new LanguageService(metadata, help), new DatasetStore(loggerFactory.CreateLogger<DatasetStore>())), uri);
    }

    private static (HoverProvider Provider, DocumentUri Uri) HoverProvider()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var store = Store(ScriptText, out var uri);
        return (new HoverProvider(loggerFactory.CreateLogger<HoverProvider>(), store,
            new ETL_SQL.Engine.Functions.FunctionRegistry(), new LanguageHelpRegistry(),
            new DatasetStore(loggerFactory.CreateLogger<DatasetStore>())), uri);
    }

    private static (ReportRenameProvider Provider, DocumentUri Uri) RenameProvider(string text = ScriptText)
    {
        var store = Store(text, out var uri);
        return (new ReportRenameProvider(store), uri);
    }

    private static DocumentStateStore Store(string text, out DocumentUri uri)
    {
        uri = DocumentUri.From("untitled:html-visual.rptsql");
        var store = new DocumentStateStore();
        var script = new Parser(new Lexer(text).Tokenize(), text).Parse();
        store.SetState(uri, text, script, new ETL_SQL.Core.LineageTracker(ETL_SQL.Common.NullLogger.Instance));
        return store;
    }

    private static string Apply(string text, WorkspaceEdit edit, DocumentUri uri)
    {
        foreach (var item in Assert.IsAssignableFrom<IEnumerable<TextEdit>>(edit.Changes![uri])
            .OrderByDescending(edit => edit.Range.Start.Line).ThenByDescending(edit => edit.Range.Start.Character))
        {
            var start = Offset(text, item.Range.Start);
            var end = Offset(text, item.Range.End);
            text = text[..start] + item.NewText + text[end..];
        }
        return text;
    }

    private static int Offset(string text, Position position)
    {
        var offset = 0;
        for (var line = 0; line < position.Line; line++) offset = text.IndexOf('\n', offset) + 1;
        return offset + (int)position.Character;
    }

    private static Position PositionAfter(string text, string anchor)
    {
        var position = PositionOf(text, anchor, 0);
        return new Position(position.Line, position.Character + anchor.Length);
    }

    private static Position PositionOf(string text, string anchor, int occurrence)
    {
        var offset = -1;
        for (var index = 0; index <= occurrence; index++) offset = text.IndexOf(anchor, offset + 1, StringComparison.Ordinal);
        Assert.True(offset >= 0);
        var before = text[..offset];
        return new Position(before.Count(character => character == '\n'), offset - (before.LastIndexOf('\n') + 1));
    }

    private static int Count(string text, string value) => System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(value)).Count;
}
