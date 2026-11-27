using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using Xunit;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Reporting.Conformance;

/// <summary>
/// Declarative SLT-style negative conformance runner for <c>.rptsql</c> reporting scripts.
/// </summary>
/// <remarks>
/// Scripts in <c>tests/fixtures/reporting/negative/*.rptsql</c> remain valid <c>.rptsql</c> documents
/// whose assertions live in comment directives:
/// <list type="bullet">
///   <item><description><c>-- statement ok</c> — setup SQL accumulated for subsequent test cases.</description></item>
///   <item><description><c>-- statement error [parse|semantic|domain] &lt;expected substring&gt;</c> —
///   isolated block that must fail during the requested stage with a message containing the substring.</description></item>
/// </list>
/// </remarks>
public sealed class ReportingNegativeConformanceTests
{
    public enum FailureStage
    {
        Any,
        Parse,
        Semantic,
        Domain
    }

    public sealed record NegativeRecord(
        bool ExpectError,
        FailureStage Stage,
        string ExpectedSubstring,
        string Body,
        int LineNumber);

    public static string FixtureDirectory =>
        Path.Combine(RepresentativeVisualConformanceHarness.GetRepoRoot(), "tests", "fixtures", "reporting", "negative");

    public static IEnumerable<object[]> Fixtures()
    {
        if (!Directory.Exists(FixtureDirectory))
        {
            yield return ["NO-NEGATIVE-FIXTURES-DISCOVERED"];
            yield break;
        }

        foreach (var path in Directory.GetFiles(FixtureDirectory, "*.rptsql")
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            yield return [Path.GetFileName(path)];
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task NegativeFixture_FailsAtExpectedStageWithExpectedMessage(string fixtureFileName)
    {
        Assert.NotEqual("NO-NEGATIVE-FIXTURES-DISCOVERED", fixtureFileName);
        var path = Path.Combine(FixtureDirectory, fixtureFileName);
        var records = ParseRecords(await File.ReadAllLinesAsync(path));
        Assert.NotEmpty(records);
        await RunRecordsAsync(records, path);
    }

    [Fact]
    public async Task Runner_FailsClosedWhenStatementErrorUnexpectedlySucceeds()
    {
        var records = ParseRecords([
            "-- statement ok",
            "SELECT 'Jan' AS Month, 100 AS Revenue INTO #valid;",
            "-- statement error domain requires positive values",
            "CREATE VISUAL ValidBar AS BAR (SOURCE = #valid, MAPPINGS (X = Month, Y = Revenue));",
            "CREATE PAGE Main AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = ValidBar));"
        ]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunRecordsAsync(records, "inline-positive.rptsql"));
        Assert.Contains("succeeded", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runner_FailsClosedWhenFailureStageDoesNotMatch()
    {
        var records = ParseRecords([
            "-- statement ok",
            "SELECT 'Jan' AS Month, 100 AS Revenue INTO #data;",
            "-- statement error domain requires positive values",
            "CREATE VISUAL BadParse AS CUSTOM (SOURCE = #data, CHART (COORDINATE (TYPE = CARTESIAN), LAYERS (b = HEXAGON (ENCODE (X = Month (TYPE = NOMINAL), Y = Revenue (TYPE = QUANTITATIVE))))));"
        ]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunRecordsAsync(records, "wrong-stage.rptsql"));
        Assert.Contains("expected stage Domain", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<NegativeRecord> ParseRecords(IReadOnlyList<string> lines)
    {
        var records = new List<NegativeRecord>();
        bool? currentIsError = null;
        var currentStage = FailureStage.Any;
        var currentExpected = string.Empty;
        var currentLine = 1;
        var buffer = new StringBuilder();

        void Flush()
        {
            if (currentIsError is null)
            {
                buffer.Clear();
                return;
            }
            var body = buffer.ToString().Trim();
            buffer.Clear();
            if (!string.IsNullOrWhiteSpace(body))
                records.Add(new NegativeRecord(currentIsError.Value, currentStage, currentExpected, body, currentLine));
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                var directive = trimmed[2..].Trim();
                if (directive.Equals("statement ok", StringComparison.OrdinalIgnoreCase))
                {
                    Flush();
                    currentIsError = false;
                    currentStage = FailureStage.Any;
                    currentExpected = string.Empty;
                    currentLine = i + 1;
                    continue;
                }
                if (directive.StartsWith("statement error", StringComparison.OrdinalIgnoreCase))
                {
                    Flush();
                    currentIsError = true;
                    currentLine = i + 1;
                    var remainder = directive["statement error".Length..].Trim();
                    var firstSpace = remainder.IndexOf(' ');
                    var head = firstSpace < 0 ? remainder : remainder[..firstSpace];
                    if (TryParseStage(head, out var parsedStage))
                    {
                        currentStage = parsedStage;
                        currentExpected = firstSpace < 0 ? string.Empty : remainder[(firstSpace + 1)..].Trim();
                    }
                    else
                    {
                        currentStage = FailureStage.Any;
                        currentExpected = remainder;
                    }
                    continue;
                }
            }

            if (currentIsError is null && !string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                currentIsError = false;
                currentStage = FailureStage.Any;
                currentExpected = string.Empty;
                currentLine = i + 1;
            }

            buffer.AppendLine(raw);
        }

        Flush();
        return records;
    }

    private static bool TryParseStage(string token, out FailureStage stage)
    {
        switch (token.ToUpperInvariant())
        {
            case "PARSE":
                stage = FailureStage.Parse;
                return true;
            case "SEMANTIC":
                stage = FailureStage.Semantic;
                return true;
            case "DOMAIN":
                stage = FailureStage.Domain;
                return true;
            case "ANY":
                stage = FailureStage.Any;
                return true;
            default:
                stage = FailureStage.Any;
                return false;
        }
    }

    private static async Task RunRecordsAsync(IReadOnlyList<NegativeRecord> records, string sourcePath)
    {
        var prelude = new StringBuilder();
        var rule = new AdvancedChartAuthoringRule();

        foreach (var record in records)
        {
            if (!record.ExpectError)
            {
                prelude.AppendLine(record.Body);
                var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileScriptAsync(
                    prelude.ToString(), sourcePath, $"{Path.GetFileName(sourcePath)} line {record.LineNumber}");
                var okErrors = manifest.Visuals
                    .Where(v => !string.IsNullOrWhiteSpace(v.Error))
                    .Select(v => $"{v.Name}: {v.Error}")
                    .ToList();
                if (okErrors.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(sourcePath)} line {record.LineNumber}: 'statement ok' failed visual build: {string.Join(" | ", okErrors)}");
                }
                continue;
            }

            var combinedScript = prelude.Length == 0
                ? record.Body
                : $"{prelude}\n{record.Body}";

            var (actualStage, messages) = await EvaluateNegativeAsync(combinedScript, sourcePath, rule);
            if (actualStage is null || messages.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(sourcePath)} line {record.LineNumber}: statement unexpectedly succeeded (expected {record.Stage} error containing '{record.ExpectedSubstring}').");
            }

            if (record.Stage != FailureStage.Any && actualStage != record.Stage)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(sourcePath)} line {record.LineNumber}: expected stage {record.Stage}, but failed at {actualStage} with: {string.Join(" | ", messages)}");
            }

            if (!string.IsNullOrWhiteSpace(record.ExpectedSubstring) &&
                !messages.Any(m => m.Contains(record.ExpectedSubstring, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(sourcePath)} line {record.LineNumber}: expected {record.Stage} error containing '{record.ExpectedSubstring}', got: {string.Join(" | ", messages)}");
            }
        }
    }

    private static async Task<(FailureStage? Stage, List<string> Messages)> EvaluateNegativeAsync(
        string scriptText,
        string sourcePath,
        AdvancedChartAuthoringRule authoringRule)
    {
        Script ast;
        try
        {
            var tokens = new Lexer(scriptText).Tokenize();
            ast = new CoreParser(tokens, scriptText).Parse();
        }
        catch (Exception ex) when (ex is SyntaxException or InvalidOperationException)
        {
            return (FailureStage.Parse, [ex.Message]);
        }

        var parseErrors = ast.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Message)
            .ToList();
        if (parseErrors.Count > 0)
        {
            return (FailureStage.Parse, parseErrors);
        }

        var semanticMessages = new List<string>();
        foreach (var visual in ast.Statements.OfType<CreateVisualStatement>())
        {
            semanticMessages.AddRange(AdvancedChartSemanticValidator.Validate(visual).Select(d => d.Message));
        }

        var lintResults = await authoringRule.AnalyzeAsync(ast, new DefaultLintContext());
        foreach (var lint in lintResults.Where(r => r.Severity == LintSeverity.Error))
        {
            if (!semanticMessages.Contains(lint.Message, StringComparer.Ordinal))
                semanticMessages.Add(lint.Message);
        }

        if (semanticMessages.Count > 0)
        {
            return (FailureStage.Semantic, semanticMessages);
        }

        try
        {
            var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileScriptAsync(
                scriptText, sourcePath, Path.GetFileName(sourcePath));
            var domainMessages = manifest.Visuals
                .Where(v => !string.IsNullOrWhiteSpace(v.Error))
                .Select(v => v.Error!)
                .ToList();
            return domainMessages.Count > 0
                ? (FailureStage.Domain, domainMessages)
                : (null, []);
        }
        catch (Exception ex)
        {
            return (FailureStage.Domain, [ex.Message]);
        }
    }
}
