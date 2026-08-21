using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Interfaces;
using ETL_SQL.Core.Metadata;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Functions;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.ReportBuilder;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Spectre.Console;
using Spectre.Console.Rendering;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Reporting.TerminalSemantics;

public record TerminalSnapshotResult(
    string RawOutput,
    string NormalizedText,
    int Width,
    int LineCount,
    string ChecksumSha256);

public static class TerminalSnapshotHarness
{
    private static readonly Regex AnsiEscapeRegex = new(
        @"\x1B\[[0-9;]*[a-zA-Z]|\x1B\([a-zA-Z]",
        RegexOptions.Compiled);

    public static string GetRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "ETL-SQL.slnx")) || Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }
            current = Path.GetDirectoryName(current);
        }
        return Directory.GetCurrentDirectory();
    }

    public static TerminalSnapshotResult CaptureSnapshot(
        IRenderable renderable,
        int width,
        bool preserveAnsi = false)
    {
        using var stringWriter = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = preserveAnsi ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = preserveAnsi ? ColorSystemSupport.TrueColor : ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(stringWriter)
        });

        console.Profile.Width = width;
        console.Write(renderable);

        var raw = stringWriter.ToString();
        var normalized = NormalizeSnapshot(raw, stripAnsi: !preserveAnsi);
        var lines = normalized.Split('\n');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        return new TerminalSnapshotResult(
            RawOutput: raw,
            NormalizedText: normalized,
            Width: width,
            LineCount: lines.Length,
            ChecksumSha256: Convert.ToHexString(hash));
    }

    public static string NormalizeSnapshot(string text, bool stripAnsi = true)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = text;
        if (stripAnsi)
        {
            result = AnsiEscapeRegex.Replace(result, string.Empty);
        }

        // Normalize newlines to \n and trim trailing line whitespace
        var lines = result.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var trimmedLines = lines.Select(l => l.TrimEnd());

        return string.Join("\n", trimmedLines).Trim();
    }

    public static async Task<(Script Ast, ReportManifest Manifest, Evaluator Evaluator)> CompileFixtureAsync(string fixtureFileName)
    {
        var repoRoot = GetRepoRoot();
        var fixturePath = Path.Combine(repoRoot, "tests", "fixtures", "reporting", "terminal-semantics", fixtureFileName);

        if (!File.Exists(fixturePath))
            throw new FileNotFoundException($"Terminal semantic fixture not found: {fixturePath}");

        var script = await File.ReadAllTextAsync(fixturePath);
        var tokens = new Lexer(script).Tokenize();
        var ast = new CoreParser(tokens, script).Parse();

        if (ast.Diagnostics.Any(d => d.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error))
        {
            var errors = string.Join("; ", ast.Diagnostics.Select(d => d.Message));
            throw new InvalidOperationException($"Fixture '{fixtureFileName}' failed to parse: {errors}");
        }

        var evaluator = CreateTerminalEvaluator();
        await evaluator.Evaluate(ast);

        var manifestBuilder = new ManifestBuilder(evaluator);
        var manifest = await manifestBuilder.BuildAsync(fixturePath);

        return (ast, manifest, evaluator);
    }

    private static Evaluator CreateTerminalEvaluator()
    {
        var services = new ServiceCollection();
        var logger = NullLogger.Instance;
        var sec = new ETL_SQL.Services.SecurityService(logger) { IsTestMode = true };
        var connRegistry = new ConnectorRegistry();
        connRegistry.Register(new ETL_SQL.Connectors.MockDb.MockDbConnector());
        connRegistry.Register(new ETL_SQL.Connectors.FlatFile.FlatFileConnector());

        services.AddSingleton<Common.ILogger>(logger);
        services.AddSingleton(sec);
        services.AddSingleton<IConnectorRegistry>(connRegistry);
        services.AddSingleton<IFunctionRegistry, FunctionRegistry>();
        services.AddSingleton<ILineageTracker, LineageTracker>();
        services.AddSingleton<IDockerManager>(new Mock<IDockerManager>().Object);
        services.AddSingleton<ISessionStateManager>(new SessionStateManager(logger, sec, new Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object, new SqliteSessionMetadataStoreFactory(), null));
        services.AddSingleton<ILanguageHelpRegistry, LanguageHelpRegistry>();
        services.AddSingleton<EvaluatorComponentRegistry>();
        services.AddSingleton<IReportContext, ReportRegistry>();
        services.AddTransient<Evaluator>();

        var handlers = new[]
        {
            typeof(DeclareStatementHandler),
            typeof(SetVariableStatementHandler),
            typeof(SelectStatementHandler),
            typeof(InsertStatementHandler),
            typeof(ExecutePushdownStatementHandler),
            typeof(CreateTableStatementHandler),
            typeof(CreateConnectionStatementHandler),
            typeof(CreateVisualStatementHandler),
            typeof(CreatePageStatementHandler),
            typeof(CreateDatasetStatementHandler),
            typeof(CreateContainerStatementHandler),
            typeof(CreateNavigationStatementHandler),
            typeof(CreateButtonStatementHandler),
            typeof(CreateStyleStatementHandler),
            typeof(CreateThemeStatementHandler),
            typeof(SetReportMetadataStatementHandler),
            typeof(ExportReportStatementHandler)
        };

        foreach (var h in handlers)
        {
            services.AddTransient(typeof(IStatementHandler), h);
            services.AddTransient(h);
        }

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<Evaluator>();
    }
}
