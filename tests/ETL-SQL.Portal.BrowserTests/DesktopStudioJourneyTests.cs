using System.Diagnostics;
using System.Text.Json;
using ETL_SQL.WorkstationEditor;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class DesktopStudioJourneyTests(PortalBrowserFixture fixture)
{
    private const string InitialScript = """
        CREATE CONNECTION sample_data AS MOCKDB();
        SELECT UserID, UserName INTO #users FROM sample_data.Users;
        CREATE VISUAL UsersTable AS TABLE (
          SOURCE = #users,
          MAPPINGS (USER_ID = UserID, USER_NAME = UserName)
        );
        CREATE PAGE Main AS DASHBOARD (
          LAYOUT (STRUCTURE = 'A', MAP ('A' = UsersTable))
        );
        """;

    [Fact]
    public async Task AuthenticatedDesktopHosts_AuthoringShutdownRelaunchAndProjectWindows()
    {
        using var firstWorkspace = new TempWorkspace();
        using var secondWorkspace = new TempWorkspace();
        var firstFile = Path.Combine(firstWorkspace.Root, "users.rptsql");
        var secondFile = Path.Combine(secondWorkspace.Root, "other.etlsql");
        await File.WriteAllTextAsync(firstFile, InitialScript);
        await File.WriteAllTextAsync(secondFile, "SELECT 2 AS ProjectTwo;");
        RunGit(firstWorkspace.Root, "init");
        RunGit(firstWorkspace.Root, "config", "user.email", "studio-browser@example.invalid");
        RunGit(firstWorkspace.Root, "config", "user.name", "Studio Browser");
        RunGit(firstWorkspace.Root, "add", "users.rptsql");
        RunGit(firstWorkspace.Root, "commit", "-m", "Add users report");

        await using var firstHost = WorkstationEditorApp.Create([], Options(firstWorkspace.Root, firstFile, "first-token"));
        await firstHost.StartAsync();

        await using var firstWindow = await fixture.NewSessionAsync();
        var firstPage = firstWindow.Page;
        await firstPage.GotoAsync(StudioUrl(firstHost, "first-token"));
        await WaitForStudioAsync(firstWindow);

        await firstPage.EvaluateAsync(
            """
            () => {
                const button = document.querySelector("[data-activity='catalog']");
                if (button.classList.contains('active')) button.click();
                button.click();
            }
            """);
        var connectionButton = firstPage.Locator("[data-connection='sample_data']");
        await connectionButton.WaitForAsync();
        await connectionButton.ClickAsync();
        await firstPage.Locator("[data-table='Users']").ClickAsync();
        await firstPage.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].studioContext.snapshot?.rowCount > 0");
        await firstPage.EvaluateAsync("() => { window.__STUDIO__.state.selectedVisualId = 'UsersTable'; }");
        await firstPage.Locator("[data-field='UserName']").ClickAsync();
        await firstPage.Locator("[data-filter-dialog-apply]").ClickAsync();
        await firstPage.Locator("[data-filter-value='UserName']").First.CheckAsync();
        await firstPage.WaitForFunctionAsync("() => window.__STUDIO__.state.editorInstance.getValue().includes('ETL-SQL-STUDIO-FILTER')");

        const string editMarker = "-- production desktop browser journey";
        await firstPage.EvaluateAsync(
            "marker => window.__STUDIO__.state.editorInstance.setValue(window.__STUDIO__.state.editorInstance.getValue() + `\n${marker}\n`)",
            editMarker);
        await firstPage.Locator("[data-activity='git']").ClickAsync();
        var headComparison = firstPage.Locator("[data-git-revision='HEAD']");
        await headComparison.WaitForAsync();
        await headComparison.ClickAsync();
        var gitDiff = firstPage.Locator(".etlsql-studio-git-diff-modal");
        await gitDiff.WaitForAsync();
        await gitDiff.Locator("[role='table']").WaitForAsync();
        var workingDiffCells = gitDiff.Locator(".etlsql-studio-git-diff-cell.is-right");
        Assert.True(await workingDiffCells.CountAsync() > 0, await gitDiff.InnerTextAsync());
        Assert.DoesNotContain(await gitDiff.Locator(".etlsql-studio-git-diff-cell.is-left").AllInnerTextsAsync(),
            text => text.Contains(editMarker, StringComparison.Ordinal));
        Assert.Contains(await workingDiffCells.AllInnerTextsAsync(),
            text => text.Contains(editMarker, StringComparison.Ordinal));
        await gitDiff.Locator("[data-git-diff-close]").ClickAsync();
        await firstPage.Locator("[data-action='run']").ClickAsync();
        await firstPage.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].studioContext.resultsTrace.some(item => item.type === 'results')");
        await firstPage.Locator("[data-action='save']").ClickAsync();
        await firstPage.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === false");

        await firstPage.ReloadAsync();
        await WaitForStudioAsync(firstWindow);
        Assert.Contains(editMarker,
            await firstPage.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"),
            StringComparison.Ordinal);

        await firstPage.Locator(".etlsql-studio-tab.active .etlsql-tab-title").DblClickAsync();
        var renameInput = firstPage.Locator(".etlsql-tab-rename-input");
        await renameInput.FillAsync("renamed-users");
        await renameInput.PressAsync("Enter");
        await firstPage.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].path === 'renamed-users.rptsql'");
        var renamedFile = Path.Combine(firstWorkspace.Root, "renamed-users.rptsql");
        Assert.False(File.Exists(firstFile));
        Assert.True(File.Exists(renamedFile));

        await firstPage.Locator("[data-explorer-new-folder='']").ClickAsync();
        await firstPage.Locator(".etlsql-feedback-field input").FillAsync("archive");
        await firstPage.Locator(".etlsql-feedback-btn-primary").ClickAsync();
        var archiveFolder = firstPage.Locator("[data-explorer-folder='archive']");
        await archiveFolder.WaitForAsync();
        await firstPage.Locator("[data-explorer-file='renamed-users.rptsql']").DragToAsync(archiveFolder);
        await firstPage.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].path === 'archive/renamed-users.rptsql'");
        Assert.True(File.Exists(Path.Combine(firstWorkspace.Root, "archive", "renamed-users.rptsql")));

        await archiveFolder.HoverAsync();
        await archiveFolder.Locator("[data-explorer-rename='archive']").ClickAsync();
        await firstPage.Locator(".etlsql-feedback-field input").FillAsync("completed");
        await firstPage.Locator(".etlsql-feedback-btn-primary").ClickAsync();
        var completedFolder = firstPage.Locator("[data-explorer-folder='completed']");
        await completedFolder.WaitForAsync();
        await firstPage.Locator("[data-explorer-file='completed/renamed-users.rptsql']")
            .DragToAsync(firstPage.Locator("[data-explorer-root-drop]"));
        await firstPage.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].path === 'renamed-users.rptsql'");
        await completedFolder.HoverAsync();
        await completedFolder.Locator("[data-explorer-delete='completed']").ClickAsync();
        await firstPage.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await firstPage.WaitForFunctionAsync("() => !window.__STUDIO__.state.workspaceFolders.some(folder => folder.path === 'completed')");
        Assert.True(File.Exists(renamedFile));
        Assert.False(Directory.Exists(Path.Combine(firstWorkspace.Root, "completed")));

        await using var secondHost = WorkstationEditorApp.Create([], Options(secondWorkspace.Root, secondFile, "second-token"));
        await secondHost.StartAsync();
        await using var secondWindow = await fixture.NewSessionAsync();
        var secondPage = secondWindow.Page;
        await secondPage.GotoAsync(StudioUrl(secondHost, "second-token"));
        await WaitForStudioAsync(secondWindow);
        var firstLifecycle = await LifecycleAsync(firstPage);
        var secondLifecycle = await LifecycleAsync(secondPage);
        Assert.NotEqual(firstLifecycle.GetProperty("instanceId").GetString(), secondLifecycle.GetProperty("instanceId").GetString());
        Assert.Equal(Path.GetFullPath(firstWorkspace.Root), firstLifecycle.GetProperty("workspaceRoot").GetString());
        Assert.Equal(Path.GetFullPath(secondWorkspace.Root), secondLifecycle.GetProperty("workspaceRoot").GetString());
        Assert.Contains("ProjectTwo",
            await secondPage.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"),
            StringComparison.Ordinal);

        await firstPage.EvaluateAsync(
            "() => window.__STUDIO__.state.editorInstance.setValue(window.__STUDIO__.state.editorInstance.getValue() + '\\n-- unsaved exit check\\n')");
        await firstPage.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === true");

        var firstStopping = ApplicationStoppingAsync(firstHost);
        await firstPage.Locator("[data-action='exit']").ClickAsync();
        var exitConfirmation = firstPage.Locator(".etlsql-feedback-dialog");
        await exitConfirmation.WaitForAsync();
        Assert.Contains("unsaved changes", await exitConfirmation.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        await exitConfirmation.GetByRole(AriaRole.Button, new() { Name = "Exit Without Saving" }).ClickAsync();
        await firstStopping.WaitAsync(TimeSpan.FromSeconds(10));
        await firstHost.StopAsync();

        await using var relaunchedHost = WorkstationEditorApp.Create([], Options(firstWorkspace.Root, renamedFile, "relaunch-token"));
        await relaunchedHost.StartAsync();
        await using var relaunchedWindow = await fixture.NewSessionAsync();
        await relaunchedWindow.Page.GotoAsync(StudioUrl(relaunchedHost, "relaunch-token"));
        await WaitForStudioAsync(relaunchedWindow);
        Assert.Contains(editMarker,
            await relaunchedWindow.Page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"),
            StringComparison.Ordinal);

        Assert.Empty(firstWindow.PageErrors);
        Assert.Empty(secondWindow.PageErrors);
        Assert.Empty(relaunchedWindow.PageErrors);
    }

    private static WorkstationEditorOptions Options(string root, string file, string token) =>
        new(root, file, 0, false, token, StudioMode: true, InstanceId: Guid.NewGuid().ToString("D"));

    private static string StudioUrl(Microsoft.AspNetCore.Builder.WebApplication app, string token) =>
        $"{WorkstationEditorApp.GetListeningUrl(app)}/studio?token={Uri.EscapeDataString(token)}";

    private static async Task WaitForStudioAsync(BrowserSession session)
    {
        try
        {
            await session.Page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
                new PageWaitForFunctionOptions { Timeout = 20_000 });
        }
        catch (TimeoutException ex)
        {
            var title = await session.Page.TitleAsync();
            var body = await session.Page.Locator("body").InnerTextAsync();
            throw new Xunit.Sdk.XunitException(
                $"Studio did not boot at {session.Page.Url}. "
                + $"Title: {title}. Body: {body[..Math.Min(body.Length, 500)]}. "
                + $"Page errors: {string.Join(" | ", session.PageErrors)}. "
                + $"Console errors: {string.Join(" | ", session.ConsoleErrors)}. "
                + $"Failed requests: {string.Join(" | ", session.FailedRequests)}.", ex);
        }
    }

    private static Task ApplicationStoppingAsync(Microsoft.AspNetCore.Builder.WebApplication app)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Lifetime.ApplicationStopping.Register(() => completion.TrySetResult());
        return completion.Task;
    }

    private static Task<JsonElement> LifecycleAsync(IPage page) => page.EvaluateAsync<JsonElement>(
        """
        async () => {
            const token = new URLSearchParams(location.search).get('token');
            const response = await fetch('/api/studio/lifecycle', {
                headers: { 'X-ETLSQL-EDITOR-TOKEN': token }
            });
            return response.json();
        }
        """);

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("Git command timed out.");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git command failed: {output.GetAwaiter().GetResult()}{error.GetAwaiter().GetResult()}");
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "etlsql-studio-browser", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A failed assertion should not be hidden by best-effort test cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // A failed assertion should not be hidden by best-effort test cleanup.
            }
        }
    }
}
