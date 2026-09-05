using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Engine;
using ETL_SQL.ReportHosting;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public sealed class FilterControlsGapsTests
{
    [Fact]
    public async Task Slicer_NewOptionsAndMappings_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"slicer_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
SELECT 'A1' AS Code, 'Alpha One' AS DisplayName, 'https://example.com/a1.png' AS Thumbnail INTO #items;
INSERT INTO #items VALUES ('B2', 'Beta Two', 'https://example.com/b2.png');

DECLARE @selected_item VARCHAR = 'A1';

CREATE VISUAL ItemSlicer AS SLICER (
  SOURCE   = #items,
  MAPPINGS (
    VALUE = Code,
    LABEL = DisplayName,
    IMAGE = Thumbnail
  ),
  OPTIONS  (
    MODE           = MULTI,
    LAYOUT         = 'TILE',
    SEARCHABLE     = ON,
    MAX_OPTIONS    = 5,
    SORT           = ALPHA,
    IMAGE_SIZE     = '32px',
    IMAGE_POSITION = TOP,
    IMAGE_FIT      = cover
  ),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@selected_item, value))
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = ItemSlicer));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "ItemSlicer");
            Assert.Null(visual.Error);

            Assert.Equal("MULTI", visual.Options["MODE"]);
            Assert.Equal("TILE", visual.Options["LAYOUT"]);
            Assert.Equal("True", visual.Options["SEARCHABLE"]);
            Assert.Equal("5", visual.Options["MAX_OPTIONS"]);
            Assert.Equal("ALPHA", visual.Options["SORT"]);
            Assert.Equal("32px", visual.Options["IMAGE_SIZE"]);
            Assert.Equal("TOP", visual.Options["IMAGE_POSITION"]);
            Assert.Equal("cover", visual.Options["IMAGE_FIT"]);
            Assert.Equal("Thumbnail", visual.Options["mapping:image"]);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task MultiSelect_ListDefaultAndSelectAllOptions_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"multiselect_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
SELECT 'East' AS Region INTO #regions;
INSERT INTO #regions VALUES ('West');
INSERT INTO #regions VALUES ('North');

DECLARE @selected_regions LIST = ('East', 'West');

CREATE VISUAL RegionPicker AS MULTISELECT (
  SOURCE   = #regions,
  MAPPINGS (VALUE = Region),
  OPTIONS  (
    DEFAULT          = ('East', 'West'),
    LAYOUT           = CHIPS,
    SEARCHABLE       = ON,
    SHOW_SELECT_ALL  = ON,
    SELECT_ALL_LABEL = 'Select All Regions',
    CLEAR_ALL_LABEL  = 'Clear All Regions',
    MAX_OPTIONS      = 10
  ),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@selected_regions, value))
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = RegionPicker));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "RegionPicker");
            Assert.Null(visual.Error);

            Assert.Equal("CHIPS", visual.Options["LAYOUT"]);
            Assert.Equal("True", visual.Options["SEARCHABLE"]);
            Assert.Equal("True", visual.Options["SHOW_SELECT_ALL"]);
            Assert.Equal("Select All Regions", visual.Options["SELECT_ALL_LABEL"]);
            Assert.Equal("Clear All Regions", visual.Options["CLEAR_ALL_LABEL"]);
            Assert.Equal("10", visual.Options["MAX_OPTIONS"]);

            Assert.Contains("East", visual.Options["DEFAULT"]);
            Assert.Contains("West", visual.Options["DEFAULT"]);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task DatePicker_RangeModeDynamicBoundsAndBlackout_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"datepicker_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
SELECT '2026-01-15' AS OrderDate INTO #orders;
INSERT INTO #orders VALUES ('2026-06-20');
INSERT INTO #orders VALUES ('2026-11-05');

DECLARE @start_date DATE = '2026-01-01';
DECLARE @end_date   DATE = '2026-12-31';

CREATE VISUAL RangePicker AS DATEPICKER (
  SOURCE   = #orders,
  OPTIONS  (
    MODE           = RANGE,
    MIN            = SOURCE_MIN(OrderDate),
    MAX            = SOURCE_MAX(OrderDate),
    FORMAT         = 'YYYY-MM-DD',
    DISABLED_DATES = ('2026-12-25', '2026-01-01'),
    DISABLED_DAYS  = (SAT, SUN),
    WEEK_START     = MON,
    DISPLAY        = INLINE
  ),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@start_date, @end_date, value))
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = RangePicker));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "RangePicker");
            Assert.Null(visual.Error);

            Assert.Equal("RANGE", visual.Options["MODE"]);
            Assert.Equal("2026-01-15", visual.Options["MIN"]);
            Assert.Equal("2026-11-05", visual.Options["MAX"]);
            Assert.Equal("YYYY-MM-DD", visual.Options["FORMAT"]);
            Assert.Equal("MON", visual.Options["WEEK_START"]);
            Assert.Equal("INLINE", visual.Options["DISPLAY"]);

            var action = visual.Actions.First(a => a.Type == "SET_PARAMETER");
            Assert.Equal("@start_date", action.ParameterName);
            Assert.Equal("@end_date", action.SecondaryParameterName);

            Assert.Contains("2026-12-25", visual.Options["DISABLED_DATES"]);
            Assert.Contains("SAT", visual.Options["DISABLED_DAYS"]);
            Assert.Contains("SUN", visual.Options["DISABLED_DAYS"]);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task RelDatePicker_QuickPicksAndFiscalPeriods_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"reldate_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
DECLARE @start_period VARCHAR = 'FQS';
DECLARE @end_period   VARCHAR = 'FQE';

CREATE VISUAL FiscalPeriodPicker AS RELDATEPICKER (
  OPTIONS (
    MODE              = RANGE,
    FISCAL_YEAR_START = 10,
    QUICK_PICKS       = (
      'This Qtr' = 'FQS',
      'Last Qtr' = 'FQ-1',
      'This Year' = 'FYS'
    )
  ),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@start_period, @end_period, value))
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = FiscalPeriodPicker));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "FiscalPeriodPicker");
            Assert.Null(visual.Error);

            Assert.Equal("RANGE", visual.Options["MODE"]);
            Assert.Equal("10", visual.Options["FISCAL_YEAR_START"]);

            var action = visual.Actions.First(a => a.Type == "SET_PARAMETER");
            Assert.Equal("@start_period", action.ParameterName);
            Assert.Equal("@end_period", action.SecondaryParameterName);

            Assert.Contains("This Qtr", visual.Options["QUICK_PICKS"]);
            Assert.Contains("FQS", visual.Options["QUICK_PICKS"]);
            Assert.Contains("Last Qtr", visual.Options["QUICK_PICKS"]);

            // Test RelDateResolver C# fiscal calculations
            // Fiscal year starts in October (month 10)
            // Q1: Oct, Nov, Dec
            // Q2: Jan, Feb, Mar
            // Q3: Apr, May, Jun
            // Q4: Jul, Aug, Sep
            var baseDate = new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Local); // May 2026 -> Q3 of FY2026
            var fqs = RelDateResolver.Resolve("FQS", DayOfWeek.Sunday, baseDate, fiscalYearStart: 10);
            Assert.Equal(new DateTime(2026, 4, 1), fqs.Date);

            var fqe = RelDateResolver.Resolve("FQE", DayOfWeek.Sunday, baseDate, fiscalYearStart: 10);
            Assert.Equal(new DateTime(2026, 6, 30), fqe.Date);

            var prevQtr = RelDateResolver.Resolve("FQ-1", DayOfWeek.Sunday, baseDate, fiscalYearStart: 10);
            Assert.Equal(new DateTime(2026, 1, 1), prevQtr.Date);

            var fys = RelDateResolver.Resolve("FYS", DayOfWeek.Sunday, baseDate, fiscalYearStart: 10);
            Assert.Equal(new DateTime(2025, 10, 1), fys.Date);

            var fye = RelDateResolver.Resolve("FYE", DayOfWeek.Sunday, baseDate, fiscalYearStart: 10);
            Assert.Equal(new DateTime(2026, 9, 30), fye.Date);

            var futureQtr = RelDateResolver.Resolve("FQ+1", DayOfWeek.Sunday, baseDate, fiscalYearStart: 10);
            Assert.Equal(new DateTime(2026, 7, 1), futureQtr.Date);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Slider_RangeModeDataTicksAndFormatting_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"slider_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
SELECT 100 AS PriceTier INTO #tiers;
INSERT INTO #tiers VALUES (250);
INSERT INTO #tiers VALUES (500);
INSERT INTO #tiers VALUES (1000);

DECLARE @min_price DECIMAL = 100;
DECLARE @max_price DECIMAL = 1000;

CREATE VISUAL PriceSlider AS SLIDER (
  SOURCE   = #tiers,
  MAPPINGS (VALUE = PriceTier),
  OPTIONS  (
    MODE        = RANGE,
    FORMAT      = 'C0',
    SHOW_TICKS  = ON,
    TICK_LABELS = ON,
    FIRE_ON     = RELEASE
  ),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@min_price, @max_price, value))
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = PriceSlider));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "PriceSlider");
            Assert.Null(visual.Error);

            Assert.Equal("RANGE", visual.Options["MODE"]);
            Assert.Equal("C0", visual.Options["FORMAT"]);
            Assert.Equal("True", visual.Options["SHOW_TICKS"]);
            Assert.Equal("True", visual.Options["TICK_LABELS"]);
            Assert.Equal("RELEASE", visual.Options["FIRE_ON"]);

            var action = visual.Actions.First(a => a.Type == "SET_PARAMETER");
            Assert.Equal("@min_price", action.ParameterName);
            Assert.Equal("@max_price", action.SecondaryParameterName);

            // DATA_TICKS should contain the sorted tiers
            Assert.True(visual.Options.ContainsKey("DATA_TICKS"));
            var ticks = JsonSerializer.Deserialize<decimal[]>(visual.Options["DATA_TICKS"]);
            Assert.NotNull(ticks);
            Assert.Equal(new decimal[] { 100m, 250m, 500m, 1000m }, ticks);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Search_MatchModeAndMinChars_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"search_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
DECLARE @search_term VARCHAR = '';

CREATE VISUAL FastSearch AS SEARCH (
  OPTIONS (
    PLACEHOLDER = 'Search accounts...',
    MATCH_MODE  = CONTAINS,
    MIN_CHARS   = 3,
    SHOW_CLEAR  = ON
  ),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@search_term, value))
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = FastSearch));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "FastSearch");
            Assert.Null(visual.Error);

            Assert.Equal("Search accounts...", visual.Options["PLACEHOLDER"]);
            Assert.Equal("CONTAINS", visual.Options["MATCH_MODE"]);
            Assert.Equal("3", visual.Options["MIN_CHARS"]);
            Assert.Equal("True", visual.Options["SHOW_CLEAR"]);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Checkbox_NewOptionsAndToggle_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"checkbox_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
DECLARE @is_active VARCHAR = 'N';

CREATE VISUAL ActiveToggle AS CHECKBOX (
  TITLE   = 'Status Filter',
  OPTIONS (
    LABEL         = 'Active Only',
    DISPLAY_STYLE = TOGGLE,
    TRUE_VALUE    = 'Y',
    FALSE_VALUE   = 'N',
    DEFAULT       = ON
  ),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@is_active, value))
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = ActiveToggle));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "ActiveToggle");
            Assert.Null(visual.Error);

            Assert.Equal("Active Only", visual.Options["LABEL"]);
            Assert.Equal("TOGGLE", visual.Options["DISPLAY_STYLE"]);
            Assert.Equal("Y", visual.Options["TRUE_VALUE"]);
            Assert.Equal("N", visual.Options["FALSE_VALUE"]);
            Assert.Equal("True", visual.Options["DEFAULT"]);

            var action = Assert.Single(visual.Actions);
            Assert.Equal("ON_CHANGE", action.Trigger);
            Assert.Equal("@is_active", action.ParameterName);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Textbox_MultilinePatternAndOnSubmit_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"textbox_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
DECLARE @comment VARCHAR = '';

CREATE VISUAL CommentBox AS TEXTBOX (
  TITLE   = 'Comments',
  OPTIONS (
    LABEL              = 'User Feedback',
    MULTILINE          = ON,
    ROWS               = 4,
    MAX_LENGTH         = 250,
    PATTERN            = '^[A-Za-z0-9 ]*$',
    VALIDATION_MESSAGE = 'Alphanumeric only'
  ),
  ACTIONS (ON_SUBMIT = SET_PARAMETER(@comment, value))
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = CommentBox));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "CommentBox");
            Assert.Null(visual.Error);

            Assert.Equal("User Feedback", visual.Options["LABEL"]);
            Assert.Equal("True", visual.Options["MULTILINE"]);
            Assert.Equal("4", visual.Options["ROWS"]);
            Assert.Equal("250", visual.Options["MAX_LENGTH"]);
            Assert.Equal("^[A-Za-z0-9 ]*$", visual.Options["PATTERN"]);
            Assert.Equal("Alphanumeric only", visual.Options["VALIDATION_MESSAGE"]);

            var action = Assert.Single(visual.Actions);
            Assert.Equal("ON_SUBMIT", action.Trigger);
            Assert.Equal("@comment", action.ParameterName);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Numberbox_StepperFormatPrefixSuffixAndOnSubmit_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"numberbox_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
DECLARE @threshold DECIMAL = 50.0;

CREATE VISUAL ThresholdInput AS NUMBERBOX (
  TITLE   = 'Threshold Setting',
  OPTIONS (
    LABEL        = 'Min Threshold',
    MIN          = 0,
    MAX          = 500,
    STEP         = 5,
    SHOW_STEPPER = ON,
    FORMAT       = 'C2',
    PREFIX       = '$',
    SUFFIX       = ' USD'
  ),
  ACTIONS (ON_SUBMIT = SET_PARAMETER(@threshold, value))
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = ThresholdInput));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "ThresholdInput");
            Assert.Null(visual.Error);

            Assert.Equal("Min Threshold", visual.Options["LABEL"]);
            Assert.Equal("0", visual.Options["MIN"]);
            Assert.Equal("500", visual.Options["MAX"]);
            Assert.Equal("5", visual.Options["STEP"]);
            Assert.Equal("True", visual.Options["SHOW_STEPPER"]);
            Assert.Equal("C2", visual.Options["FORMAT"]);
            Assert.Equal("$", visual.Options["PREFIX"]);
            Assert.Equal(" USD", visual.Options["SUFFIX"]);

            var action = Assert.Single(visual.Actions);
            Assert.Equal("ON_SUBMIT", action.Trigger);
            Assert.Equal("@threshold", action.ParameterName);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }
}

