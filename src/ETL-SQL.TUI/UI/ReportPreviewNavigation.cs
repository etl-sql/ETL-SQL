namespace ETL_SQL.TUI.UI;

public enum ReportPreviewNavigationTarget
{
    None,
    PreviousControl,
    ActivateControl,
    NextControl,
    RunReport,
    PreviousPage,
    NextPage
}

/// <summary>Shared geometry for the report-preview header buttons and their mouse hit targets.</summary>
public static class ReportPreviewNavigation
{
    public const int ButtonWidth = 3;
    public const int ControlButtonCount = 3;
    public const int PageButtonCount = 2;
    public const int RunButtonWidth = 5;
    public const int RightBorderPadding = 2;
    public const int MinimumPageNavigationWidth = 11;

    public static int ControlStartX(int width, bool hasMultiplePages) =>
        width - RightBorderPadding
        - (hasMultiplePages ? PageButtonCount * ButtonWidth : 0)
        - ControlButtonCount * ButtonWidth;

    public static int PageStartX(int width) =>
        width - RightBorderPadding - PageButtonCount * ButtonWidth;

    public static int RunStartX(int width, bool hasControls, bool hasMultiplePages)
    {
        var runEnd = hasControls
            ? ControlStartX(width, hasMultiplePages)
            : width - RightBorderPadding - (hasMultiplePages ? PageButtonCount * ButtonWidth : 0);
        return runEnd - RunButtonWidth;
    }

    public static ReportPreviewNavigationTarget HitTest(
        int x,
        int width,
        bool hasControls,
        bool hasMultiplePages)
    {
        var runStart = RunStartX(width, hasControls, hasMultiplePages);
        if (runStart >= 0 && x >= runStart && x < runStart + RunButtonWidth)
            return ReportPreviewNavigationTarget.RunReport;

        if (hasMultiplePages && width >= MinimumPageNavigationWidth)
        {
            var pageStart = PageStartX(width);
            if (x >= pageStart && x < pageStart + ButtonWidth)
                return ReportPreviewNavigationTarget.PreviousPage;
            if (x >= pageStart + ButtonWidth && x < pageStart + 2 * ButtonWidth)
                return ReportPreviewNavigationTarget.NextPage;
        }

        if (!hasControls) return ReportPreviewNavigationTarget.None;

        var controlStart = ControlStartX(width, hasMultiplePages);
        if (controlStart < 0) return ReportPreviewNavigationTarget.None;
        if (x >= controlStart && x < controlStart + ButtonWidth)
            return ReportPreviewNavigationTarget.PreviousControl;
        if (x >= controlStart + ButtonWidth && x < controlStart + 2 * ButtonWidth)
            return ReportPreviewNavigationTarget.ActivateControl;
        if (x >= controlStart + 2 * ButtonWidth && x < controlStart + 3 * ButtonWidth)
            return ReportPreviewNavigationTarget.NextControl;

        return ReportPreviewNavigationTarget.None;
    }
}
