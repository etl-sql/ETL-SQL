using System.Net;
using System.Net.Http.Json;
using ETL_SQL.WorkstationEditor;
using Xunit;

namespace ETL_SQL.Tests.WorkstationEditor;

public sealed class WorkstationEditorTests
{
    [Fact]
    public void Workspace_RejectsTraversalAndNonScriptFiles()
    {
        using var temp = new TempWorkspace();
        var workspace = new WorkstationWorkspace(temp.Root, readOnly: false);

        Assert.Throws<UnauthorizedAccessException>(() => workspace.ResolveEditablePath("../escape.etlsql"));
        Assert.Throws<UnauthorizedAccessException>(() => workspace.ResolveEditablePath("notes.txt"));
    }

    [Fact]
    public async Task Workspace_ReadWrite_StaysInsideRoot()
    {
        using var temp = new TempWorkspace();
        var workspace = new WorkstationWorkspace(temp.Root, readOnly: false);

        await workspace.WriteTextAsync("nested/pipeline.etlsql", "SELECT 1;", CancellationToken.None);

        Assert.Equal("SELECT 1;", await workspace.ReadTextAsync("nested/pipeline.etlsql", CancellationToken.None));
        Assert.Contains(workspace.ListFiles(), file => file.Path == "nested/pipeline.etlsql");
    }

    [Fact]
    public async Task Workspace_RejectsDirectorySymlinkEscape()
    {
        using var temp = new TempWorkspace();
        var outside = Path.Combine(Path.GetTempPath(), $"etlsql-editor-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            var link = Path.Combine(temp.Root, "linked");
            if (!TryCreateDirectorySymlink(link, outside))
                return;

            var workspace = new WorkstationWorkspace(temp.Root, readOnly: false);

            Assert.Throws<UnauthorizedAccessException>(() => workspace.ResolveEditablePath("linked/escape.etlsql"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                workspace.WriteTextAsync("linked/escape.etlsql", "SELECT 1;", CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(outside, "escape.etlsql")));
        }
        finally
        {
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Api_RequiresSessionToken()
    {
        using var temp = new TempWorkspace();
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "pipeline.etlsql"), "SELECT 1;");
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };

        var unauthenticated = await client.GetAsync("/api/workspace");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var authenticated = new HttpRequestMessage(HttpMethod.Get, "/api/workspace");
        authenticated.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        var authenticatedResponse = await client.SendAsync(authenticated);

        Assert.Equal(HttpStatusCode.OK, authenticatedResponse.StatusCode);
    }

    [Fact]
    public async Task Api_ReadOnlyWorkspace_RejectsSave()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, true, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var save = new HttpRequestMessage(HttpMethod.Put, "/api/files");
        save.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        save.Content = JsonContent.Create(new SaveFileRequest("pipeline.etlsql", "SELECT 1;"));

        var response = await client.SendAsync(save);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Analysis_UsesSharedLinterDiagnostics()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var analyze = new HttpRequestMessage(HttpMethod.Post, "/api/analyze");
        analyze.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        analyze.Content = JsonContent.Create(new AnalyzeRequest("SELECT * FROM #stage;", "pipeline.etlsql"));

        var response = await client.SendAsync(analyze);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AnalyzeResponse>();
        Assert.NotNull(result);
        Assert.Contains(result!.Diagnostics, d => d.Code == "AvoidSelectStar");
    }

    [Fact]
    public async Task Completion_ReturnsLanguageSuggestions()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var complete = new HttpRequestMessage(HttpMethod.Post, "/api/complete");
        complete.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        complete.Content = JsonContent.Create(new CompleteRequest("SEL", 0, 3, "pipeline.etlsql"));

        var response = await client.SendAsync(complete);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CompleteResponse>();
        Assert.NotNull(result);
        Assert.Contains(result!.Items, item => item.Label == "SELECT");
    }

    [Fact]
    public async Task Completion_UsesDocumentConnectionMetadataForTables()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var complete = new HttpRequestMessage(HttpMethod.Post, "/api/complete");
        complete.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        complete.Content = JsonContent.Create(new CompleteRequest(
            "CREATE CONNECTION m AS MOCKDB();\nSELECT * FROM m.",
            1,
            "SELECT * FROM m.".Length,
            "pipeline.etlsql"));

        var response = await client.SendAsync(complete);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CompleteResponse>();
        Assert.NotNull(result);
        Assert.Contains(result!.Items, item => item.Label == "m.Users");
    }

    [Fact]
    public async Task Completion_ExpandsStarWithTableColumns()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var complete = new HttpRequestMessage(HttpMethod.Post, "/api/complete");
        complete.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        complete.Content = JsonContent.Create(new CompleteRequest(
            "CREATE CONNECTION m AS MOCKDB();\nSELECT u.* FROM m.Users AS u;",
            1,
            "SELECT u.*".Length,
            "pipeline.etlsql"));

        var response = await client.SendAsync(complete);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CompleteResponse>();
        Assert.NotNull(result);
        var expansion = Assert.Single(result!.Items, item => item.Label == "Expand columns");
        Assert.Contains("u.UserID, u.UserName, u.Email", expansion.InsertText);
        Assert.Equal("SELECT u.".Length, expansion.StartColumn);
        Assert.Equal("SELECT u.*".Length, expansion.EndColumn);
    }

    [Fact]
    public async Task Completion_ExpandsBareStarByReplacingStarAtCursorRightSide()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        const string line = "SELECT * FROM m.Users;";
        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var complete = new HttpRequestMessage(HttpMethod.Post, "/api/complete");
        complete.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        complete.Content = JsonContent.Create(new CompleteRequest(
            "CREATE CONNECTION m AS MOCKDB();\n" + line,
            1,
            "SELECT *".Length,
            "pipeline.etlsql"));

        var response = await client.SendAsync(complete);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CompleteResponse>();
        Assert.NotNull(result);
        var expansion = Assert.Single(result!.Items, item => item.Label == "Expand columns");
        Assert.Contains("m.Users.UserID", expansion.InsertText);
        Assert.Equal("SELECT ".Length, expansion.StartColumn);
        Assert.Equal("SELECT *".Length, expansion.EndColumn);
    }

    [Fact]
    public async Task Hover_ReturnsHelpMarkdown()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var hover = new HttpRequestMessage(HttpMethod.Post, "/api/hover");
        hover.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        hover.Content = JsonContent.Create(new HoverRequest("SELECT", "SELECT 1;", 0, 0, "pipeline.etlsql"));

        var response = await client.SendAsync(hover);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<HoverResponse>();
        Assert.NotNull(result);
        Assert.Contains("SELECT", result!.Markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Format_UsesSharedSqlFormatter()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var format = new HttpRequestMessage(HttpMethod.Post, "/api/format");
        format.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        format.Content = JsonContent.Create(new FormatRequest(
            "CREATE CONNECTION m AS MOCKDB(); SELECT * FROM m.Users WHERE UserID = 1;",
            "pipeline.etlsql"));

        var response = await client.SendAsync(format);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<FormatResponse>();
        Assert.NotNull(result);
        Assert.Empty(result!.Diagnostics);
        Assert.Contains("CREATE CONNECTION", result.Script);
        Assert.Contains("\nSELECT", result.Script);
    }

    private static bool TryCreateDirectorySymlink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is UnauthorizedAccessException ||
            ex is PlatformNotSupportedException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Run_ExecutesScriptAndReturnsRows()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var run = new HttpRequestMessage(HttpMethod.Post, "/api/run");
        run.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        run.Content = JsonContent.Create(new RunRequest("SELECT 1 AS Value;", null, "pipeline.etlsql", 100));

        var response = await client.SendAsync(run);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RunResponse>();
        Assert.NotNull(result);
        Assert.True(result!.Success, result.Message);
        Assert.Contains("Value", result.Columns);
        Assert.Single(result.Rows);
    }

    [Fact]
    public async Task Run_ExecutesMockDbConnectionScript()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var run = new HttpRequestMessage(HttpMethod.Post, "/api/run");
        run.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        run.Content = JsonContent.Create(new RunRequest(
            "CREATE CONNECTION m AS MOCKDB();\nSELECT * FROM m.Users;",
            null,
            "pipeline.etlsql",
            100));

        var response = await client.SendAsync(run);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RunResponse>();
        Assert.NotNull(result);
        Assert.True(result!.Success, result.Message);
        Assert.True(result.Rows.Count > 0, result.Message);
    }

    [Fact]
    public async Task Analysis_UsesDocumentConnectionMetadataForSchemaValidation()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var analyze = new HttpRequestMessage(HttpMethod.Post, "/api/analyze");
        analyze.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        analyze.Content = JsonContent.Create(new AnalyzeRequest(
            "CREATE CONNECTION m AS MOCKDB();\nSELECT * FROM m.Users;",
            "pipeline.etlsql"));

        var response = await client.SendAsync(analyze);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AnalyzeResponse>();
        Assert.NotNull(result);
        Assert.DoesNotContain(result!.Diagnostics, d =>
            d.Code == "SchemaValidation" ||
            d.Message.Contains("Table 'Users' not found", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "etl-sql-editor-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
