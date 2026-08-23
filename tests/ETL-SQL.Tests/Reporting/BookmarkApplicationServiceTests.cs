using System.Linq;
using ETL_SQL.Core.Reporting;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// Pure resolve/validate/reconcile coverage for the server-side bookmark application service.
/// Exercises the real reconciliation against a constructed manifest — no HTTP, no evaluator.
/// </summary>
public class BookmarkApplicationServiceTests
{
    private static ReportManifest ManifestWith()
    {
        var m = new ReportManifest();
        m.Pages.Add(new PageManifest { Name = "Main" });
        m.Pages.Add(new PageManifest { Name = "Detail" });
        m.Visuals.Add(new VisualManifest { Name = "Chart1" });
        m.Containers = new() { new ContainerManifest { Name = "Panel" } };
        m.ParameterMetadata["@year"] = new ParameterMetadataManifest { Name = "@year", Type = "INT" };
        m.ParameterMetadata["@region"] = new ParameterMetadataManifest { Name = "@region", Type = "VARCHAR" };
        m.Parameters["@year"] = "2026";
        m.Parameters["@region"] = "All";
        return m;
    }

    [Fact]
    public void Reconcile_ValidState_ProducesNoWarnings()
    {
        var m = ManifestWith();
        var req = new ResolvedReportState { ActivePage = "Detail" };
        req.Parameters["@region"] = ReportStateValue.FromString("West");
        req.Parameters["@year"] = ReportStateValue.FromNumber(2026m);
        req.Collapsed["Panel"] = true;
        req.Visible["Chart1"] = false;

        var r = BookmarkApplicationService.Reconcile(m, req);

        Assert.False(r.HasWarnings);
        Assert.Equal("Detail", r.State.ActivePage);
        Assert.Equal(2026m, r.State.Parameters["@year"].NumberValue);
        Assert.True(r.State.Collapsed["Panel"]);
        Assert.False(r.State.Visible["Chart1"]);
    }

    [Fact]
    public void Reconcile_UnknownPage_IsDroppedWithWarning()
    {
        var m = ManifestWith();
        var req = new ResolvedReportState { ActivePage = "Ghost" };
        var r = BookmarkApplicationService.Reconcile(m, req);
        Assert.Null(r.State.ActivePage);
        Assert.Contains(r.Warnings, w => w.Contains("Ghost"));
    }

    [Fact]
    public void Reconcile_UnknownParameterAndObject_AreDroppedWithWarnings()
    {
        var m = ManifestWith();
        var req = new ResolvedReportState();
        req.Parameters["@ghost"] = ReportStateValue.FromString("x");
        req.Visible["GhostVisual"] = true;
        req.Collapsed["GhostPanel"] = true;

        var r = BookmarkApplicationService.Reconcile(m, req);

        Assert.False(r.State.Parameters.ContainsKey("@ghost"));
        Assert.False(r.State.Visible.ContainsKey("GhostVisual"));
        Assert.False(r.State.Collapsed.ContainsKey("GhostPanel"));
        Assert.Equal(3, r.Warnings.Count);
    }

    [Fact]
    public void Reconcile_CoercesNumericStringIntoIntParameter()
    {
        var m = ManifestWith();
        var req = new ResolvedReportState();
        req.Parameters["@year"] = ReportStateValue.FromString("2030"); // legacy string into INT
        var r = BookmarkApplicationService.Reconcile(m, req);
        Assert.Equal(ReportStateValueKind.Number, r.State.Parameters["@year"].Kind);
        Assert.Equal(2030m, r.State.Parameters["@year"].NumberValue);
        Assert.False(r.HasWarnings);
    }

    [Fact]
    public void Reconcile_IncompatibleTypeIsDroppedWithWarning()
    {
        var m = ManifestWith();
        var req = new ResolvedReportState();
        req.Parameters["@year"] = ReportStateValue.FromString("twenty"); // not numeric
        var r = BookmarkApplicationService.Reconcile(m, req);
        Assert.False(r.State.Parameters.ContainsKey("@year"));
        Assert.Contains(r.Warnings, w => w.Contains("@year"));
    }

    [Fact]
    public void Reconcile_ScriptHashMismatch_FlagsDrift()
    {
        var m = ManifestWith();
        var req = new ResolvedReportState { ScriptHash = "oldhash" };
        var r = BookmarkApplicationService.Reconcile(m, req, currentScriptHash: "newhash");
        Assert.True(r.HasDrift);
        Assert.Contains(r.Warnings, w => w.Contains("different version"));
    }

    [Fact]
    public void ResolveAuthorBookmark_FindsByName_CaseInsensitive()
    {
        var m = ManifestWith();
        m.Bookmarks = new()
        {
            new BookmarkManifest { Name = "WestCoast", State = new ResolvedReportState { ActivePage = "Detail" } }
        };
        Assert.NotNull(BookmarkApplicationService.ResolveAuthorBookmark(m, "westcoast"));
        Assert.Null(BookmarkApplicationService.ResolveAuthorBookmark(m, "nope"));
    }
}
