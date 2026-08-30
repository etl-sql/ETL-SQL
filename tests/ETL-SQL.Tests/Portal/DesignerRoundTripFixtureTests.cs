using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Xunit;

namespace ETL_SQL.Tests.Portal;

/// <summary>
/// Byte-preservation evidence for the surgical designer patcher, driven by checked-in fixtures
/// rather than generated scripts.
///
/// <see cref="ReportDesignerLosslessFuzzTests"/> proves the property over randomized input; this lane
/// proves it over the authored shapes a real report actually contains — comments, CTEs, datasets,
/// pages, visuals, filters, bookmarks, both line-ending conventions, and the transiently invalid
/// scripts a split-screen author types on the way to a valid one. Fixtures are read from disk, so the
/// assertion is against the same bytes a checkout produces, and their line endings are pinned in
/// <c>.gitattributes</c> so a Windows checkout cannot quietly change what is being compared.
/// </summary>
public class DesignerRoundTripFixtureTests
{
    private readonly DesignerScriptPatcher _patcher = new();
    private readonly DesignerAnalysisService _analysis = new();

    /// <summary>
    /// Every category the round-trip evidence is required to cover, mapped to the fixture that
    /// carries it. Directory discovery alone would pass an empty directory; this list is what makes a
    /// dropped or unstaged fixture a failure instead of a silently smaller run.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RequiredValidFixtures =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["comments"] = "comments.rptsql",
            ["CTEs"] = "ctes.rptsql",
            ["datasets"] = "datasets.rptsql",
            ["pages"] = "pages.rptsql",
            ["visuals"] = "visuals.rptsql",
            ["filters"] = "filters.rptsql",
            ["bookmarks"] = "bookmarks.rptsql",
            ["LF line endings"] = "line-endings-lf.rptsql",
            ["CRLF line endings"] = "line-endings-crlf.rptsql",
        };

    private static readonly IReadOnlyDictionary<string, string> RequiredInvalidFixtures =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["unclosed parenthesis"] = "unclosed-paren.rptsql",
            ["half-typed visual"] = "incomplete-visual.rptsql",
            ["unterminated string"] = "unterminated-string.rptsql",
            ["half-typed page map"] = "partial-page-map.rptsql",
        };

    private static string FixtureRoot
    {
        get
        {
            var current = AppDomain.CurrentDomain.BaseDirectory;
            while (current != null)
            {
                if (File.Exists(Path.Combine(current, "ETL-SQL.slnx")))
                    return Path.Combine(current, "tests", "fixtures", "reporting", "designer-round-trip");
                current = Path.GetDirectoryName(current);
            }
            throw new DirectoryNotFoundException("Could not locate repository root containing ETL-SQL.slnx.");
        }
    }

    public static TheoryData<string> ValidFixtures
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in RequiredValidFixtures.Values.OrderBy(n => n, StringComparer.Ordinal))
                data.Add(name);
            return data;
        }
    }

    public static TheoryData<string> InvalidFixtures
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in RequiredInvalidFixtures.Values.OrderBy(n => n, StringComparer.Ordinal))
                data.Add(name);
            return data;
        }
    }

    // Read the file as bytes and decode without any newline translation, so what the test compares is
    // what is on disk. File.ReadAllText would be enough today, but this makes the intent explicit.
    private static string ReadFixture(string relativePath)
    {
        var path = Path.Combine(FixtureRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing round-trip fixture: {path}");
        var bytes = File.ReadAllBytes(path);
        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            $"{relativePath} has a UTF-8 BOM; fixtures are compared byte for byte and must not carry one.");
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static (int Crlf, int LoneLf, int LoneCr) LineEndingHistogram(string text)
    {
        var crlf = 0;
        var loneLf = 0;
        var loneCr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                else loneCr++;
            }
            else if (text[i] == '\n')
            {
                loneLf++;
            }
        }
        return (crlf, loneLf, loneCr);
    }

    [Fact]
    public void EveryRequiredFixtureIsPresentOnDisk()
    {
        foreach (var (category, file) in RequiredValidFixtures)
            Assert.True(File.Exists(Path.Combine(FixtureRoot, file)), $"No round-trip fixture for {category}.");

        foreach (var (category, file) in RequiredInvalidFixtures)
        {
            Assert.True(
                File.Exists(Path.Combine(FixtureRoot, "invalid", file)),
                $"No invalid-edit fixture for {category}.");
        }
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void FixtureParsesWithoutError(string fixture)
    {
        var parsed = _analysis.Parse(ReadFixture(fixture), 100);
        Assert.Null(parsed.Error);
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void ReadingAndWritingBackWithoutEditsIsByteForByteIdentical(string fixture)
    {
        var script = ReadFixture(fixture);
        var state = _analysis.Parse(script, 100).DesignState;

        Assert.Equal(script, _patcher.Patch(script, state));
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void RepeatedNoOpRoundTripsNeverDrift(string fixture)
    {
        var script = ReadFixture(fixture);
        var current = script;

        for (var cycle = 0; cycle < 10; cycle++)
        {
            var state = _analysis.Parse(current, 100).DesignState;
            current = _patcher.Patch(current, state);
            Assert.Equal(script, current);
        }
    }

    /// <summary>
    /// The real lossless claim: change one property, change it back, and the file must be the original
    /// bytes again. Anything the designer state does not model — comments, CTEs, trivia, line endings —
    /// has to survive both legs for this to hold.
    /// </summary>
    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void EditingOneVisualTitleAndRevertingItRestoresTheOriginalBytes(string fixture)
    {
        var script = ReadFixture(fixture);
        var original = _analysis.Parse(script, 100).DesignState;

        var target = original.Pages.SelectMany(p => p.Visuals).FirstOrDefault(v => v.Title != null);
        Assert.True(target != null, $"{fixture} has no titled visual, so it proves nothing about edits.");

        var edited = _patcher.Patch(script, WithVisualTitle(original, target!.Id, "Round-trip probe"));
        Assert.NotEqual(script, edited);
        Assert.Contains("Round-trip probe", edited);
        Assert.Null(_analysis.Parse(edited, 100).Error);

        var editedState = _analysis.Parse(edited, 100).DesignState;
        var reverted = _patcher.Patch(edited, WithVisualTitle(editedState, target.Id, target.Title!));

        Assert.Equal(script, reverted);
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void LineEndingsSurviveAnEdit(string fixture)
    {
        var script = ReadFixture(fixture);
        var state = _analysis.Parse(script, 100).DesignState;
        var target = state.Pages.SelectMany(p => p.Visuals).First(v => v.Title != null);

        var edited = _patcher.Patch(script, WithVisualTitle(state, target.Id, "Line ending probe"));

        var before = LineEndingHistogram(script);
        var after = LineEndingHistogram(edited);
        Assert.Equal(0, before.LoneCr);
        Assert.Equal(0, after.LoneCr);

        // An edit may add or remove lines, but it must never convert one convention into the other.
        if (before.Crlf > 0)
            Assert.Equal(0, after.LoneLf);
        else
            Assert.Equal(0, after.Crlf);
    }

    /// <summary>
    /// A fixture that parses is not evidence about invalid edits, so prove the invalid corpus is
    /// actually invalid before asserting the patcher no-ops on it.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidFixtures))]
    public void InvalidFixtureIsGenuinelyUnparseable(string fixture)
    {
        var script = ReadFixture(Path.Combine("invalid", fixture));
        Assert.NotNull(_analysis.Parse(script, 100).Error);
    }

    [Theory]
    [MemberData(nameof(InvalidFixtures))]
    public void InvalidIntermediateEditIsALosslessNoOpAndNeverThrows(string fixture)
    {
        var script = ReadFixture(Path.Combine("invalid", fixture));

        // The state the client is holding is the last valid one; it does not describe this text.
        var lastValidState = _analysis.Parse(ReadFixture("visuals.rptsql"), 100).DesignState;

        string? result = null;
        Assert.Null(Record.Exception(() => result = _patcher.Patch(script, lastValidState)));
        Assert.Equal(script, result);
    }

    /// <summary>
    /// Not every broken script is a rejected script. The parser recovers from some damage and returns
    /// an AST with error diagnostics, and the patcher used to run against that recovered AST — writing
    /// a document that no longer parses over one that did. Whatever the patcher returns for these, it
    /// must be something the parser accepts.
    /// </summary>
    [Theory]
    [InlineData("unbalanced-clause-paren.rptsql")]
    public void ARecoveredParseIsNeverPatchedIntoACorruptDocument(string fixture)
    {
        var script = ReadFixture(Path.Combine("recovered", fixture));
        var lastValidState = _analysis.Parse(ReadFixture("visuals.rptsql"), 100).DesignState;

        string? patched = null;
        Assert.Null(Record.Exception(() => patched = _patcher.Patch(script, lastValidState)));
        Assert.Equal(script, patched);
    }

    /// <summary>
    /// A grid written across several lines is several rows. Reading it as one row put every visual in
    /// the same cell, and the patcher then wrote the whole page back as a single collapsed slot.
    /// </summary>
    [Fact]
    public void AGridWrittenAcrossLinesKeepsItsRows()
    {
        var state = _analysis.Parse(ReadFixture("visuals.rptsql"), 100).DesignState;
        var visuals = Assert.Single(state.Pages).Visuals;

        Assert.Equal(6, visuals.Count);
        Assert.Equal(3, visuals.Select(v => v.GridRow).Distinct().Count());
        Assert.Equal(2, visuals.Select(v => v.GridCol).Distinct().Count());
    }

    private static DesignerStateDto WithVisualTitle(DesignerStateDto state, string visualId, string title) =>
        state with
        {
            Pages = state.Pages
                .Select(p => p with
                {
                    Visuals = p.Visuals
                        .Select(v => v.Id == visualId ? v with { Title = title } : v)
                        .ToList()
                })
                .ToList()
        };
}
