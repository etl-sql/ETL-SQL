using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI;

public class AutocompleteControllerTests
{
    private static EditorBuffer BufferWith(params string[] lines)
    {
        var buf = new EditorBuffer();
        buf.Load(lines);
        return buf;
    }

    // ── FindNextPlaceholder ───────────────────────────────────────────────────

    [Fact]
    public void FindNextPlaceholder_FindsFirstMarkerOnSameLine()
    {
        const string line = "CREATE VISUAL «Name» AS BAR (";
        var buf = BufferWith(line);
        var result = AutocompleteController.FindNextPlaceholder(buf, 0, 0);

        Assert.NotNull(result);
        Assert.Equal(0, result!.Value.Line);
        // Verify the span covers exactly «Name»
        var span = line.Substring(result.Value.StartCol, result.Value.EndCol - result.Value.StartCol);
        Assert.Equal("«Name»", span);
    }

    [Fact]
    public void FindNextPlaceholder_FindsMarkerOnSubsequentLine()
    {
        var buf = BufferWith(
            "CREATE VISUAL Name AS BAR (",
            "  SOURCE = («SELECT * FROM #data»)");
        var result = AutocompleteController.FindNextPlaceholder(buf, 0, 0);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Value.Line);
    }

    [Fact]
    public void FindNextPlaceholder_RespectsFromColOnCurrentLine()
    {
        // Two markers on the same line; start search after the first one
        var buf = BufferWith("«first» and «second»");
        var first = AutocompleteController.FindNextPlaceholder(buf, 0, 0);
        Assert.NotNull(first);
        Assert.Equal(0, first!.Value.StartCol);

        var second = AutocompleteController.FindNextPlaceholder(buf, 0, first.Value.EndCol);
        Assert.NotNull(second);
        Assert.Equal(12, second!.Value.StartCol);  // index of second «
    }

    [Fact]
    public void FindNextPlaceholder_ReturnsNullWhenNoMarkersExist()
    {
        var buf = BufferWith("SELECT 1 AS n;");
        Assert.Null(AutocompleteController.FindNextPlaceholder(buf, 0, 0));
    }

    [Fact]
    public void FindNextPlaceholder_ReturnsNullAfterLastMarker()
    {
        var buf = BufferWith("«only»");
        var first = AutocompleteController.FindNextPlaceholder(buf, 0, 0);
        Assert.NotNull(first);

        var none = AutocompleteController.FindNextPlaceholder(buf, 0, first!.Value.EndCol);
        Assert.Null(none);
    }

    // ── FindPrevPlaceholder ───────────────────────────────────────────────────

    [Fact]
    public void FindPrevPlaceholder_FindsMarkerBeforeCursor()
    {
        var buf = BufferWith("«first» and «second»");
        // Cursor is past the second marker — prev should find second
        var prev = AutocompleteController.FindPrevPlaceholder(buf, 0, 12);
        Assert.NotNull(prev);
        Assert.Equal(0, prev!.Value.Line);
        Assert.Equal(0, prev.Value.StartCol);  // «first»
    }

    [Fact]
    public void FindPrevPlaceholder_FindsMarkerOnEarlierLine()
    {
        var buf = BufferWith(
            "CREATE VISUAL «Name» AS BAR (",
            "  SOURCE = (SELECT *)");
        var prev = AutocompleteController.FindPrevPlaceholder(buf, 1, 0);

        Assert.NotNull(prev);
        Assert.Equal(0, prev!.Value.Line);
    }

    [Fact]
    public void FindPrevPlaceholder_ReturnsNullWhenNoMarkersExist()
    {
        var buf = BufferWith("SELECT 1 AS n;");
        Assert.Null(AutocompleteController.FindPrevPlaceholder(buf, 0, 5));
    }

    // ── EditorBuffer.SelectRange ──────────────────────────────────────────────

    [Fact]
    public void SelectRange_SetsSelectionAndCursorCorrectly()
    {
        const string line = "CREATE VISUAL «Name» AS BAR";
        // Compute the actual span so the test doesn't depend on hardcoded byte offsets
        int startCol = line.IndexOf('«');
        int endCol = line.IndexOf('»') + 1;
        var buf = BufferWith(line);
        buf.SelectRange(0, startCol, endCol);

        Assert.Equal(0, buf.CursorLine);
        Assert.Equal(endCol, buf.CursorColumn);
        Assert.Equal(0, buf.SelectionStartLine);
        Assert.Equal(startCol, buf.SelectionStartCol);

        var selected = buf.GetSelectedText();
        Assert.Equal("«Name»", selected);
    }
}
