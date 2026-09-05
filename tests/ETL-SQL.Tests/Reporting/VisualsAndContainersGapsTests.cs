using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.ReportHosting;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public sealed class VisualsAndContainersGapsTests
{
    [Fact]
    public async Task ImageAccessibilityRule_FlagsMissingAlt_PassesWhenPresent()
    {
        var missingAltScript = @"
CREATE VISUAL LogoWithoutAlt AS IMAGE (
  OPTIONS (URL = 'https://example.com/logo.png')
);
";
        var scriptMissing = new Parser(new Lexer(missingAltScript).Tokenize()).Parse();
        var rule = new ImageAccessibilityRule();
        var missingResults = (await rule.AnalyzeAsync(scriptMissing, null!)).ToList();

        var missingWarning = Assert.Single(missingResults);
        Assert.Equal("RPT4001", missingWarning.Code);
        Assert.Equal(LintSeverity.Warning, missingWarning.Severity);
        Assert.Contains("LogoWithoutAlt", missingWarning.Message);

        var withAltScript = @"
CREATE VISUAL LogoWithAlt AS IMAGE (
  OPTIONS (
    URL = 'https://example.com/logo.png',
    ALT = 'Official Company Brand Logo'
  )
);
";
        var scriptWith = new Parser(new Lexer(withAltScript).Tokenize()).Parse();
        var withResults = (await rule.AnalyzeAsync(scriptWith, null!)).ToList();
        Assert.Empty(withResults);
    }

    [Fact]
    public async Task Actions_ResetParameters_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"reset_params_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
DECLARE @cat VARCHAR = 'Electronics';
DECLARE @subcat VARCHAR = 'Phones';

CREATE BUTTON ResetAllBtn AS (
  TITLE = 'Reset All Filters',
  ACTIONS (
    ON_CLICK = RESET_PARAMETERS(@cat, @subcat)
  )
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = ResetAllBtn));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            Assert.True(manifest.Error == null, manifest.Error);

            var button = manifest.Buttons!.Single(b => b.Name == "ResetAllBtn");
            var action = Assert.Single(button.Actions);
            Assert.Equal("ON_CLICK", action.Trigger);
            Assert.Equal("RESET_PARAMETERS", action.Type);
            Assert.NotNull(action.ResetParameters);
            Assert.Contains("@cat", action.ResetParameters);
            Assert.Contains("@subcat", action.ResetParameters);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Actions_OpenUrl_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"open_url_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
CREATE BUTTON ExternalLinkBtn AS (
  TITLE = 'Visit Portal',
  ACTIONS (
    ON_CLICK = OPEN_URL('https://portal.example.com', TARGET = '_blank')
  )
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = ExternalLinkBtn));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            Assert.True(manifest.Error == null, manifest.Error);

            var button = manifest.Buttons!.Single(b => b.Name == "ExternalLinkBtn");
            var action = Assert.Single(button.Actions);
            Assert.Equal("ON_CLICK", action.Trigger);
            Assert.Equal("OPEN_URL", action.Type);
            Assert.Equal("https://portal.example.com", action.Url);
            Assert.Equal("_blank", action.Target);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Actions_ShowAndHideModal_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"modal_actions_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
CREATE BUTTON OpenModalBtn AS (
  TITLE = 'Filter Options',
  ACTIONS (ON_CLICK = SHOW_MODAL('FilterDialog'))
);

CREATE BUTTON CloseModalBtn AS (
  TITLE = 'Dismiss',
  ACTIONS (ON_CLICK = HIDE_MODAL('FilterDialog'))
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A B', MAP ('A' = OpenModalBtn, 'B' = CloseModalBtn));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            Assert.True(manifest.Error == null, manifest.Error);

            var openBtn = manifest.Buttons!.Single(b => b.Name == "OpenModalBtn");
            var openAction = Assert.Single(openBtn.Actions);
            Assert.Equal("SHOW_MODAL", openAction.Type);
            Assert.Equal("FilterDialog", openAction.ModalName);

            var closeBtn = manifest.Buttons!.Single(b => b.Name == "CloseModalBtn");
            var closeAction = Assert.Single(closeBtn.Actions);
            Assert.Equal("HIDE_MODAL", closeAction.Type);
            Assert.Equal("FilterDialog", closeAction.ModalName);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Container_TabsWithIconsBadgesAndPosition_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"tabs_container_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
CREATE BUTTON Tab1Btn AS (TITLE = 'Tab1');
CREATE BUTTON Tab2Btn AS (TITLE = 'Tab2');

CREATE CONTAINER SalesTabs AS TABS (
  LAYOUT (
    MAP (
      'Overview' = Tab1Btn (ICON = 'dashboard', BADGE = 'New'),
      'Regional' = Tab2Btn (ICON = 'globe', BADGE = '12')
    )
  ),
  OPTIONS (
    TAB_POSITION = 'LEFT',
    SHOW_ACTIVE_COUNT = ON,
    REFRESH = 45
  )
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = SalesTabs));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            Assert.True(manifest.Error == null, manifest.Error);

            var container = manifest.Containers!.Single(c => c.Name == "SalesTabs");
            Assert.Equal("TABS", container.ContainerType.ToUpperInvariant());
            Assert.Equal("LEFT", container.Options!["TAB_POSITION"]);
            Assert.Equal("ON", container.Options["SHOW_ACTIVE_COUNT"]);
            Assert.Equal(45, container.Refresh);

            Assert.NotNull(container.SlotDetails);
            Assert.Equal(2, container.SlotDetails.Count);

            Assert.True(container.SlotDetails.ContainsKey("Overview"));
            var slot1 = container.SlotDetails["Overview"];
            Assert.Equal("dashboard", slot1.Icon);
            Assert.Equal("New", slot1.Badge);
            Assert.Equal("Tab1Btn", slot1.Visual);

            Assert.True(container.SlotDetails.ContainsKey("Regional"));
            var slot2 = container.SlotDetails["Regional"];
            Assert.Equal("globe", slot2.Icon);
            Assert.Equal("12", slot2.Badge);
            Assert.Equal("Tab2Btn", slot2.Visual);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Container_AccordionWithDefaultOpen_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"accordion_container_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
CREATE BUTTON Panel1Btn AS (TITLE = 'P1');
CREATE BUTTON Panel2Btn AS (TITLE = 'P2');

CREATE CONTAINER MetricAccordion AS ACCORDION (
  LAYOUT (
    MAP (
      'Summary' = Panel1Btn (ICON = 'chart'),
      'Breakdown' = Panel2Btn
    )
  ),
  OPTIONS (
    DEFAULT_OPEN = 'Summary',
    SHOW_ACTIVE_COUNT = ON
  )
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = MetricAccordion));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            Assert.True(manifest.Error == null, manifest.Error);

            var container = manifest.Containers!.Single(c => c.Name == "MetricAccordion");
            Assert.Equal("ACCORDION", container.ContainerType.ToUpperInvariant());
            Assert.Equal("Summary", container.Options!["DEFAULT_OPEN"]);
            Assert.Equal("ON", container.Options["SHOW_ACTIVE_COUNT"]);

            Assert.True(container.SlotDetails!.ContainsKey("Summary"));
            var summarySlot = container.SlotDetails["Summary"];
            Assert.Equal("chart", summarySlot.Icon);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Container_ModalDrawerAndCollapsibleBox_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"containers_misc_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
CREATE BUTTON BtnContent AS (TITLE = 'Inner Content');

CREATE CONTAINER SettingsModal AS MODAL (
  TITLE = 'Configuration Settings',
  LAYOUT (MAP ('Body' = BtnContent)),
  OPTIONS (DEFAULT = OPEN)
);

CREATE CONTAINER FilterDrawer AS DRAWER (
  LAYOUT (MAP ('DrawerSlot' = BtnContent)),
  OPTIONS (
    POSITION = RIGHT,
    DEFAULT = OPEN
  )
);

CREATE CONTAINER CollapsiblePanel AS BOX (
  COLLAPSIBLE = ON,
  LAYOUT (MAP ('BoxSlot' = BtnContent)),
  OPTIONS (DEFAULT = CLOSED)
);

CREATE PAGE Overview AS DASHBOARD (
  STRUCTURE = 'A B C',
  MAP ('A' = SettingsModal, 'B' = FilterDrawer, 'C' = CollapsiblePanel)
);
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            Assert.True(manifest.Error == null, manifest.Error);

            var modal = manifest.Containers!.Single(c => c.Name == "SettingsModal");
            Assert.Equal("MODAL", modal.ContainerType.ToUpperInvariant());
            Assert.Equal("Configuration Settings", modal.Title);

            var drawer = manifest.Containers!.Single(c => c.Name == "FilterDrawer");
            Assert.Equal("DRAWER", drawer.ContainerType.ToUpperInvariant());
            Assert.Equal("RIGHT", drawer.Options?["POSITION"]);
            Assert.Equal("OPEN", drawer.Options?["DEFAULT"]);

            var box = manifest.Containers!.Single(c => c.Name == "CollapsiblePanel");
            Assert.Equal("BOX", box.ContainerType.ToUpperInvariant());
            Assert.True(box.IsCollapsible);
            Assert.Equal("CLOSED", box.Options?["DEFAULT"]);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Button_VariantsIconsDisabledAndMultiAction_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"button_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
DECLARE @is_admin INT = 0;
DECLARE @status VARCHAR = 'Pending';

CREATE BUTTON SaveButton AS (
  TITLE = 'Commit Changes',
  OPTIONS (
    VARIANT = DANGER,
    ICON = 'check',
    ICON_POSITION = RIGHT,
    DISABLED = '@is_admin = 0',
    SHOW_SPINNER = ON,
    MODE = TOGGLE,
    ON_VALUE = 'Confirmed',
    OFF_VALUE = 'Pending',
    CONFIRM = 'Are you sure you want to commit these changes?'
  ),
  ACTIONS (
    ON_CLICK = (
      SET_PARAMETER(@status, 'Committed'),
      OPEN_URL('https://example.com/audit', TARGET = '_blank')
    )
  )
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = SaveButton));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            Assert.True(manifest.Error == null, manifest.Error);

            var button = manifest.Buttons!.Single(b => b.Name == "SaveButton");
            Assert.Equal("DANGER", button.Options["VARIANT"]);
            Assert.Equal("check", button.Options["ICON"]);
            Assert.Equal("RIGHT", button.Options["ICON_POSITION"]);
            Assert.Equal("@is_admin = 0", button.Options["DISABLED"]);
            Assert.Equal("True", button.Options["SHOW_SPINNER"]);
            Assert.Equal("TOGGLE", button.Options["MODE"]);
            Assert.Equal("Confirmed", button.Options["ON_VALUE"]);
            Assert.Equal("Pending", button.Options["OFF_VALUE"]);
            Assert.Equal("Are you sure you want to commit these changes?", button.Options["CONFIRM"]);

            Assert.Equal(2, button.Actions.Count);
            Assert.Equal("SET_PARAMETER", button.Actions[0].Type);
            Assert.Equal("@status", button.Actions[0].ParameterName);
            Assert.Equal("OPEN_URL", button.Actions[1].Type);
            Assert.Equal("https://example.com/audit", button.Actions[1].Url);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Image_GalleryColumnsAspectRatioAndFallback_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"image_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
SELECT 'https://example.com/img1.jpg' AS PhotoUrl INTO #gallery_data;
INSERT INTO #gallery_data VALUES ('https://example.com/img2.jpg');

CREATE VISUAL ProductGallery AS IMAGE (
  SOURCE = #gallery_data,
  MAPPINGS (URL = PhotoUrl),
  OPTIONS (
    MODE = GALLERY,
    COLUMNS = 4,
    ASPECT_RATIO = '16:9',
    FALLBACK = 'https://example.com/placeholder.png',
    ALT = 'Product showcase photos'
  ),
  ACTIONS (
    ON_CLICK = OPEN_URL('https://example.com/catalog', TARGET = '_blank')
  )
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = ProductGallery));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            Assert.True(manifest.Error == null, manifest.Error);

            var visual = manifest.Visuals.Single(v => v.Name == "ProductGallery");
            Assert.Null(visual.Error);

            Assert.Equal("GALLERY", visual.Options["MODE"]);
            Assert.Equal("4", visual.Options["COLUMNS"]);
            Assert.Equal("16:9", visual.Options["ASPECT_RATIO"]);
            Assert.Equal("https://example.com/placeholder.png", visual.Options["FALLBACK"]);
            Assert.Equal("Product showcase photos", visual.Options["ALT"]);

            var action = Assert.Single(visual.Actions);
            Assert.Equal("OPEN_URL", action.Type);
            Assert.Equal("https://example.com/catalog", action.Url);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Text_InterpolationFormattingTypographyAndOverflow_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"text_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
SELECT 0.285 AS MarginRate, 'Acme Corp' AS ClientName INTO #metric_row;

CREATE VISUAL SummaryNotice AS TEXT (
  SOURCE = #metric_row,
  CONTENT = 'Client {ClientName} produced margin {MarginRate FORMAT ''0.0%''}',
  OPTIONS (
    MAX_LINES = 3,
    OVERFLOW = ELLIPSIS,
    FONT_SIZE = '16px',
    FONT_COLOR = '#1e3a8a',
    FONT_WEIGHT = BOLD
  ),
  ACTIONS (
    ON_CLICK = SHOW_MODAL('DetailDialog')
  )
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = SummaryNotice));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            Assert.True(manifest.Error == null, manifest.Error);

            var visual = manifest.Visuals.Single(v => v.Name == "SummaryNotice");
            Assert.Null(visual.Error);

            // Server-side interpolation rendered with formatted rate into DefaultValue
            Assert.Contains("Acme Corp", visual.DefaultValue);
            Assert.Contains("28.5%", visual.DefaultValue);

            Assert.Equal("3", visual.Options["MAX_LINES"]);
            Assert.Equal("ELLIPSIS", visual.Options["OVERFLOW"]);
            Assert.Equal("16px", visual.Options["FONT_SIZE"]);
            Assert.Equal("#1e3a8a", visual.Options["FONT_COLOR"]);
            Assert.Equal("BOLD", visual.Options["FONT_WEIGHT"]);

            var action = Assert.Single(visual.Actions);
            Assert.Equal("SHOW_MODAL", action.Type);
            Assert.Equal("DetailDialog", action.ModalName);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Controls_CrossCutting_VisibleDisabledReadOnlyDebounceDependsOn_CompilesToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"crosscutting_controls_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
DECLARE @region_filter VARCHAR = 'US-East';
DECLARE @search_term VARCHAR = '';

CREATE VISUAL SearchInput AS TEXTBOX (
  TITLE = 'Keyword Search',
  OPTIONS (
    DEBOUNCE = 300,
    DEPENDS_ON = (@region_filter),
    DISABLED = '@region_filter = ''None''',
    READ_ONLY = ON,
    VISIBLE = '@region_filter != ''Hidden'''
  ),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@search_term, value))
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = SearchInput));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            Assert.True(manifest.Error == null, manifest.Error);

            var visual = manifest.Visuals.Single(v => v.Name == "SearchInput");
            Assert.Null(visual.Error);

            Assert.Equal("300", visual.Options["DEBOUNCE"]);
            Assert.Contains("@region_filter", visual.Options["DEPENDS_ON"]);
            Assert.Equal("@region_filter = 'None'", visual.Options["DISABLED"]);
            Assert.Equal("True", visual.Options["READ_ONLY"]);
            Assert.Equal("@region_filter != 'Hidden'", visual.Options["VISIBLE"]);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }
}
