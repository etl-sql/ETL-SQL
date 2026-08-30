using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using ETL_SQL.Data;
using ETL_SQL.WorkstationEditor;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ETL_SQL.Tests.WorkstationEditor;

public sealed class WorkstationEditorTests
{
    [Theory]
    [InlineData("--profile")]
    [InlineData("--wat")]
    [InlineData("-x")]
    public void Options_RejectUnknownFlags(string flag)
    {
        // An unrecognised flag used to be ignored, and its value was then taken as the positional
        // path — so `--profile dev` silently opened a workspace called "dev". `--profile` in
        // particular was once in the documented command shape, so it is the likeliest to be typed.
        var ex = Assert.Throws<ArgumentException>(
            () => WorkstationEditorOptions.Parse([flag, "dev"], Directory.GetCurrentDirectory()));

        Assert.Contains(flag, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_StillParseTheSupportedFlags()
    {
        using var temp = new TempWorkspace();

        var options = WorkstationEditorOptions.Parse(
            [temp.Root, "--port", "1234", "--open", "--readonly"], Directory.GetCurrentDirectory());

        Assert.Equal(Path.GetFullPath(temp.Root), options.WorkspaceRoot);
        Assert.Equal(1234, options.Port);
        Assert.True(options.OpenBrowser);
        Assert.True(options.ReadOnly);
    }

    [Fact]
    public void Options_ParseStudioLifecycleFlags()
    {
        using var temp = new TempWorkspace();
        var instanceId = Guid.NewGuid();

        var options = WorkstationEditorOptions.Parse(
            [temp.Root, "--studio", "--instance-id", instanceId.ToString(), "--idle-timeout-minutes", "12"],
            Directory.GetCurrentDirectory());

        Assert.True(options.StudioMode);
        Assert.Equal(instanceId.ToString("D"), options.InstanceId);
        Assert.Equal(12, options.IdleShutdownMinutes);
    }

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
    public async Task Workspace_RenameFile_PreservesFolderAndExtension()
    {
        using var temp = new TempWorkspace();
        var workspace = new WorkstationWorkspace(temp.Root, readOnly: false);
        await workspace.WriteTextAsync("nested/pipeline.etlsql", "SELECT 1;", CancellationToken.None);

        var renamed = workspace.RenameFile("nested/pipeline.etlsql", "daily_load");

        Assert.Equal("nested/daily_load.etlsql", renamed.Path);
        Assert.False(File.Exists(Path.Combine(temp.Root, "nested", "pipeline.etlsql")));
        Assert.Equal("SELECT 1;", await workspace.ReadTextAsync(renamed.Path, CancellationToken.None));
    }

    [Fact]
    public async Task Workspace_RenameFile_RejectsFolderPathsAndCollisions()
    {
        using var temp = new TempWorkspace();
        var workspace = new WorkstationWorkspace(temp.Root, readOnly: false);
        await workspace.WriteTextAsync("pipeline.etlsql", "SELECT 1;", CancellationToken.None);
        await workspace.WriteTextAsync("existing.etlsql", "SELECT 2;", CancellationToken.None);

        Assert.Throws<ArgumentException>(() => workspace.RenameFile("pipeline.etlsql", "nested/moved.etlsql"));
        Assert.Throws<WorkspaceEntryConflictException>(() => workspace.RenameFile("pipeline.etlsql", "existing.etlsql"));
        Assert.Equal("SELECT 1;", await workspace.ReadTextAsync("pipeline.etlsql", CancellationToken.None));
    }

    [Fact]
    public async Task Workspace_FolderLifecycleAndFileMove_StayInsideRoot()
    {
        using var temp = new TempWorkspace();
        var workspace = new WorkstationWorkspace(temp.Root, readOnly: false);
        await workspace.WriteTextAsync("pipeline.etlsql", "SELECT 1;", CancellationToken.None);

        var folder = workspace.CreateFolder("archive");
        var moved = workspace.MoveFile("pipeline.etlsql", folder.Path);
        var renamed = workspace.RenameEntry(folder.Path, "completed", isDirectory: true);

        Assert.Equal("archive/pipeline.etlsql", moved.Path);
        Assert.Equal("completed", renamed.Path);
        Assert.Contains(workspace.ListFolders(), item => item.Path == "completed");
        Assert.Equal("SELECT 1;", await workspace.ReadTextAsync("completed/pipeline.etlsql", CancellationToken.None));

        workspace.DeleteEntry("completed", isDirectory: true);
        Assert.DoesNotContain(workspace.ListFolders(), item => item.Path == "completed");
        Assert.Empty(workspace.ListFiles());
        Assert.Throws<UnauthorizedAccessException>(() => workspace.CreateFolder("../outside"));
    }

    [Fact]
    public async Task Workspace_SaveRejectsAnExternalRevisionChange()
    {
        using var temp = new TempWorkspace();
        var workspace = new WorkstationWorkspace(temp.Root, readOnly: false);
        await workspace.WriteTextAsync("pipeline.etlsql", "SELECT 1;", CancellationToken.None);
        var opened = await workspace.ReadFileAsync("pipeline.etlsql", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "pipeline.etlsql"), "SELECT 2;");

        var error = await Assert.ThrowsAsync<WorkspaceSaveConflictException>(() =>
            workspace.WriteTextAsync("pipeline.etlsql", "SELECT 3;", opened.SourceRevision, CancellationToken.None));

        Assert.Contains("changed outside", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("SELECT 2;", await File.ReadAllTextAsync(Path.Combine(temp.Root, "pipeline.etlsql")));
    }

    [Fact]
    public void Lifecycle_TracksHeartbeatsDirtyDocumentsAndRuns()
    {
        using var temp = new TempWorkspace();
        var lifetime = new TestHostApplicationLifetime();
        var service = new StudioHostLifecycleService(
            new WorkstationEditorOptions(temp.Root, null, 0, false, "token", StudioMode: true),
            lifetime);

        service.Heartbeat(new StudioHeartbeatRequest("browser-1", Dirty: true));
        using var run = service.BeginRun();

        Assert.Equal(1, service.ConnectedClients);
        Assert.Equal(1, service.DirtyClients);
        Assert.Equal(1, service.ActiveRuns);
        Assert.False(service.TryRequestShutdown(force: false, out var reason));
        Assert.Contains("active run", reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(lifetime.StopRequested);
    }

    [Fact]
    public async Task Lifecycle_IdleShutdownWaitsUntilTheLastClientDisconnects()
    {
        using var temp = new TempWorkspace();
        var lifetime = new TestHostApplicationLifetime();
        var service = new StudioHostLifecycleService(
            new WorkstationEditorOptions(temp.Root, null, 0, false, "token", StudioMode: true),
            lifetime,
            TimeSpan.FromMilliseconds(30));
        await service.StartAsync(CancellationToken.None);
        service.Heartbeat(new StudioHeartbeatRequest("browser-1", Dirty: false));
        await Task.Delay(60);
        Assert.False(lifetime.StopRequested);

        service.Disconnect("browser-1");
        await Task.Delay(100);

        Assert.True(lifetime.StopRequested);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SessionRegistry_DiscoversHealthyProjectsAndRemovesStaleRecords()
    {
        using var workspace = new TempWorkspace();
        using var registryStorage = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            workspace.Root, null, 0, false, "registry-token", StudioMode: true));
        await app.StartAsync();
        var port = new Uri(WorkstationEditorApp.GetListeningUrl(app)).Port;
        var registry = new StudioSessionRegistry(registryStorage.Root);
        var healthyRecord = new StudioSessionRecord(
            Guid.NewGuid().ToString("D"), workspace.Root, Environment.ProcessId, port, DateTimeOffset.UtcNow,
            new StudioAuthenticationMetadata("X-ETLSQL-EDITOR-TOKEN", "registry-token"));
        await registry.WriteAsync(healthyRecord);
        await registry.WriteAsync(new StudioSessionRecord(
            Guid.NewGuid().ToString("D"), workspace.Root, int.MaxValue, port + 1, DateTimeOffset.UtcNow,
            new StudioAuthenticationMetadata("X-ETLSQL-EDITOR-TOKEN", "stale")));

        var sessions = await registry.ListHealthyAsync();

        var discovered = Assert.Single(sessions);
        Assert.Equal(healthyRecord.InstanceId, discovered.InstanceId);
        Assert.Equal(StudioSessionRegistry.NormalizeWorkspace(workspace.Root), discovered.WorkspaceRoot);
        Assert.Single(Directory.EnumerateFiles(registryStorage.Root, "*.json"));
    }

    [Fact]
    public async Task SessionRegistry_KeepsDifferentProjectsAndSameProjectInstancesSeparate()
    {
        using var firstWorkspace = new TempWorkspace();
        using var secondWorkspace = new TempWorkspace();
        using var registryStorage = new TempWorkspace();
        using var httpClient = new HttpClient(new AlwaysHealthyHandler());
        var registry = new StudioSessionRegistry(registryStorage.Root, httpClient);
        var firstProject = new StudioSessionRecord(
            Guid.NewGuid().ToString("D"), firstWorkspace.Root, Environment.ProcessId, 41001, DateTimeOffset.UtcNow,
            new StudioAuthenticationMetadata("X-ETLSQL-EDITOR-TOKEN", "first"));
        var independentInstance = firstProject with
        {
            InstanceId = Guid.NewGuid().ToString("D"),
            Port = 41002,
            Authentication = new StudioAuthenticationMetadata("X-ETLSQL-EDITOR-TOKEN", "independent")
        };
        var secondProject = firstProject with
        {
            InstanceId = Guid.NewGuid().ToString("D"),
            WorkspaceRoot = secondWorkspace.Root,
            Port = 41003,
            Authentication = new StudioAuthenticationMetadata("X-ETLSQL-EDITOR-TOKEN", "second")
        };
        await registry.WriteAsync(firstProject);
        await registry.WriteAsync(independentInstance);
        await registry.WriteAsync(secondProject);

        var sessions = await registry.ListHealthyAsync();

        Assert.Equal(3, sessions.Count);
        Assert.Equal(2, sessions.Count(record => record.WorkspaceRoot == StudioSessionRegistry.NormalizeWorkspace(firstWorkspace.Root)));
        Assert.Single(sessions, record => record.WorkspaceRoot == StudioSessionRegistry.NormalizeWorkspace(secondWorkspace.Root));
        Assert.Equal(3, sessions.Select(record => record.Port).Distinct().Count());
    }

    [Fact]
    public async Task LifecycleApi_RefusesOrdinaryShutdownWhileClientIsDirty()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token", StudioMode: true));
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };

        using var heartbeat = new HttpRequestMessage(HttpMethod.Post, "/api/studio/heartbeat");
        heartbeat.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        heartbeat.Content = JsonContent.Create(new StudioHeartbeatRequest("browser-1", Dirty: true));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(heartbeat)).StatusCode);

        using var shutdown = new HttpRequestMessage(HttpMethod.Post, "/api/studio/shutdown");
        shutdown.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        shutdown.Content = JsonContent.Create(new StudioShutdownRequest());
        var response = await client.SendAsync(shutdown);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("unsaved", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
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
    public async Task ConnectorSchema_ReturnsMockDbFromTheDesktopHostRegistry()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/connectors/schema");
        request.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var schemas = await response.Content.ReadFromJsonAsync<List<ConnectorSchemaDescriptor>>();
        var mockDb = Assert.Single(schemas!, schema => schema.ConnectorType == "MOCKDB");
        Assert.Empty(mockDb.Options);
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
    public async Task Analysis_DoesNotReturnSelectStarWarningByDefault()
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
        Assert.DoesNotContain(result!.Diagnostics, d => d.Code == "AvoidSelectStar");
    }

    [Theory]
    [InlineData("/api/script/dag")]
    [InlineData("/api/designer/dag")]
    public async Task ScriptDag_ReturnsDesignTimeFlow(string route)
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var dag = new HttpRequestMessage(HttpMethod.Post, route);
        dag.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        dag.Content = JsonContent.Create(new ScriptDagRequest(
            """
            CREATE CONNECTION m AS MOCKDB();
            SELECT UserID INTO #staging FROM m.Users;
            IF 1 = 1 BEGIN
              SELECT UserID INTO #accepted FROM #staging;
            END ELSE BEGIN
              SELECT UserID INTO #rejected FROM #staging;
            END;
            ASSERT (SELECT COUNT(*) FROM #accepted) > 0;
            """,
            "pipeline.etlsql"));

        var response = await client.SendAsync(dag);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"parsed\":true", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CONNECT m", body, StringComparison.Ordinal);
        Assert.Contains("SELECT INTO #staging", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"conditional\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"type\":\"validation\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"label\":\"TRUE\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"label\":\"ELSE\"", body, StringComparison.OrdinalIgnoreCase);
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
        // One table, no alias — bare column names. A connector-qualified prefix ("m.Users.") would
        // not survive pushdown to the remote server, so its absence is the point of the assertion.
        Assert.Contains("UserID, UserName, Email", expansion.InsertText);
        Assert.DoesNotContain("m.Users.", expansion.InsertText);
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
    public async Task DesignerPrefixedHoverAndFormat_MatchTheirUnprefixedAliases()
    {
        // Studio speaks one route dialect on every host: /api/designer/*. Hover and format had no
        // designer-prefixed alias here, so Studio requested names only this host served and lost
        // both features entirely on the Portal.
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };

        using var hover = new HttpRequestMessage(HttpMethod.Post, "/api/designer/hover");
        hover.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        hover.Content = JsonContent.Create(new HoverRequest("SELECT", "SELECT 1;", 0, 0, "pipeline.etlsql"));
        var hoverResponse = await client.SendAsync(hover);
        hoverResponse.EnsureSuccessStatusCode();
        var hoverResult = await hoverResponse.Content.ReadFromJsonAsync<HoverResponse>();
        Assert.NotNull(hoverResult);
        Assert.Contains("SELECT", hoverResult!.Markdown, StringComparison.OrdinalIgnoreCase);

        using var format = new HttpRequestMessage(HttpMethod.Post, "/api/designer/format");
        format.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        format.Content = JsonContent.Create(new FormatRequest(
            "CREATE CONNECTION m AS MOCKDB(); SELECT * FROM m.Users WHERE UserID = 1;",
            "pipeline.etlsql"));
        var formatResponse = await client.SendAsync(format);
        formatResponse.EnsureSuccessStatusCode();
        var formatResult = await formatResponse.Content.ReadFromJsonAsync<FormatResponse>();
        Assert.NotNull(formatResult);
        Assert.Empty(formatResult!.Diagnostics);
        Assert.Contains("CREATE CONNECTION", formatResult.Script);
    }

    [Fact]
    public async Task Complete_OffersSnippetTemplatesFromTheSharedLibrary()
    {
        // End-to-end wiring check: the snippet library is embedded in Core and shared with the TUI
        // and VS Code, but neither GUI editor surfaced it until now.
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/designer/complete");
        request.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        request.Content = JsonContent.Create(new CompleteRequest("$kpi", 0, 4, "pipeline.etlsql"));

        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CompleteResponse>();
        Assert.NotNull(result);
        var snippet = result!.Items.FirstOrDefault(item => item.Kind == "snippet" && item.Label == "$kpi");
        Assert.NotNull(snippet);
        Assert.Contains("CREATE VISUAL", snippet!.InsertText, StringComparison.Ordinal);
    }

    private const string MockDbScript = "CREATE CONNECTION m AS MOCKDB(); SELECT * FROM m.Users;";

    [Fact]
    public async Task DataSample_ReturnsRowsForAnAuthorizedTable()
    {
        // Studio keeps its entire visual palette disabled until a sample exists, so without this
        // route the desktop canvas could never be used at all.
        using var temp = new TempWorkspace();
        var scriptPath = Path.Combine(temp.Root, "pipeline.etlsql");
        await File.WriteAllTextAsync(scriptPath, "CREATE CONNECTION m AS MOCKDB(); SELECT * FROM m.Users;");

        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, scriptPath, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/designer/data-sample");
        request.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        request.Content = JsonContent.Create(new DataSampleRequest("connection", "m", "Users", "pipeline.etlsql", MockDbScript));

        var response = await client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<DataSampleResponse>();
        Assert.NotNull(result);
        Assert.Equal("connection", result!.SourceKind);
        Assert.NotEmpty(result.Columns);
        Assert.NotEmpty(result.Rows);
    }

    [Fact]
    public async Task DataSample_RejectsATableOutsideTheConnectionSchema()
    {
        using var temp = new TempWorkspace();
        var scriptPath = Path.Combine(temp.Root, "pipeline.etlsql");
        await File.WriteAllTextAsync(scriptPath, "CREATE CONNECTION m AS MOCKDB(); SELECT * FROM m.Users;");

        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, scriptPath, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/designer/data-sample");
        request.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        request.Content = JsonContent.Create(new DataSampleRequest("connection", "m", "NotARealTable", "pipeline.etlsql", MockDbScript));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DataSample_RefreshesAParsedDatasetQuery()
    {
        using var temp = new TempWorkspace();
        var scriptPath = Path.Combine(temp.Root, "report.rptsql");
        const string script = """
            CREATE CONNECTION m AS MOCKDB();
            CREATE DATASET &users AS (SELECT UserId, Name FROM m.Users);
            """;
        await File.WriteAllTextAsync(scriptPath, script);

        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, scriptPath, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/designer/data-sample");
        request.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        request.Content = JsonContent.Create(new DataSampleRequest(
            "dataset", null, null, "report.rptsql", script, "&users"));

        var response = await client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<DataSampleResponse>();
        Assert.NotNull(result);
        Assert.Equal("dataset", result!.SourceKind);
        Assert.Equal(["UserId", "Name"], result.Columns);
        Assert.NotEmpty(result.Rows);
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

    [Fact]
    public async Task Shell_RequiresSessionToken_AndDoesNotLeakIt()
    {
        // The shell embeds the session token so the page can call the API. Serving it
        // unauthenticated would hand that token to anything able to reach the loopback port,
        // making the /api gate decorative.
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "secret-token-value"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };

        var anonymous = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.DoesNotContain("secret-token-value", await anonymous.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var authorised = await client.GetAsync("/?token=secret-token-value");
        Assert.Equal(HttpStatusCode.OK, authorised.StatusCode);
    }

    [Fact]
    public async Task Api_SchemaEndpoint_RequiresSessionToken()
    {
        // The schema endpoint exposes cached table/column metadata, so it must sit behind the
        // same token gate as every other /api route.
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };

        var unauthenticated = await client.GetAsync("/api/designer/schema?connection=m&documentUri=pipeline.etlsql");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        var unauthenticatedSession = await client.GetAsync("/api/session/metadata?documentUri=pipeline.etlsql");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedSession.StatusCode);
    }

    [Fact]
    public async Task Run_HonoursClientCancellation()
    {
        // A run the caller abandons must not keep the host busy after the request is gone.
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var run = new HttpRequestMessage(HttpMethod.Post, "/api/run");
        run.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        run.Content = JsonContent.Create(new RunRequest(
            "CREATE CONNECTION m AS MOCKDB();\nSELECT * FROM m.Users;", null, "pipeline.etlsql", 100));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(run, cts.Token));

        // The host stays healthy and serves the next request.
        using var followUp = new HttpRequestMessage(HttpMethod.Get, "/api/workspace");
        followUp.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        var followUpResponse = await client.SendAsync(followUp);
        Assert.Equal(HttpStatusCode.OK, followUpResponse.StatusCode);
    }

    [Fact]
    public async Task Preview_BuildsReportManifest()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var preview = new HttpRequestMessage(HttpMethod.Post, "/api/preview");
        preview.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        preview.Content = JsonContent.Create(new PreviewRequest("""
            CREATE CONNECTION m AS MOCKDB();
            CREATE DATASET &users AS (SELECT UserID, UserName FROM m.Users);
            CREATE VISUAL userTable AS TABLE (SOURCE = &users);
            CREATE PAGE Overview AS DASHBOARD( STRUCTURE = 'A', MAP ( 'A' = userTable ) );
            """));

        var response = await client.SendAsync(preview);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("userTable", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_EmptyScript_ReturnsRedactedError()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var preview = new HttpRequestMessage(HttpMethod.Post, "/api/preview");
        preview.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        preview.Content = JsonContent.Create(new PreviewRequest("   "));

        var response = await client.SendAsync(preview);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Run_DoesNotReturnScriptSecretsInResponse()
    {
        // Workspace security model: no resolved secret, ENC: value or password may appear in a
        // browser response. Lineage carries script text verbatim, so it is the likeliest leak.
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var run = new HttpRequestMessage(HttpMethod.Post, "/api/run");
        run.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        // A computed column is the path that carries script text: the lineage entry for Tag gets
        // TransformationExpression = "(UPPER(UserName) + 'SECRET:db_password')". A pass-through
        // column would leave it null and the assertion would pass without proving anything.
        run.Content = JsonContent.Create(new RunRequest(
            """
            CREATE CONNECTION m AS MOCKDB();
            SELECT UserID, UPPER(UserName) + 'SECRET:db_password' AS Tag INTO #staged FROM m.Users;
            SELECT UserID FROM #staged;
            """,
            null,
            "pipeline.etlsql",
            100));

        var response = await client.SendAsync(run);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("SECRET:db_password", body, StringComparison.OrdinalIgnoreCase);
        // Assert the expression really did travel, so this stays a test of redaction rather than
        // a test that the field happened to be empty.
        Assert.Contains("UPPER(UserName)", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SECRET:********", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DROP TABLE m.Users;")]
    [InlineData("TRUNCATE TABLE m.Users;")]
    [InlineData("DELETE FROM m.Users;")]
    [InlineData("DELETE FROM m.Users WHERE UserID = 1;")]
    [InlineData("MERGE INTO m.Users AS t USING #updates AS s ON t.UserID = s.UserID WHEN MATCHED THEN UPDATE SET t.UserName = s.UserName;")]
    public void Guard_FlagsDestructiveStatements(string sql) =>
        Assert.NotEmpty(WorkstationRunGuard.FindDestructiveStatements(sql));

    [Theory]
    [InlineData("SELECT 1;")]
    [InlineData("DROP TABLE #staging;")]                     // session-local, dies with the session
    [InlineData("SELECT UserID INTO #t FROM m.Users;")]
    public void Guard_AllowsNonDestructiveStatements(string sql) =>
        Assert.Empty(WorkstationRunGuard.FindDestructiveStatements(sql));

    [Fact]
    public void Guard_FindsDestructiveStatementsNestedInControlFlow()
    {
        // Hiding a DROP inside an IF still destroys data.
        var found = WorkstationRunGuard.FindDestructiveStatements(
            "IF 1 = 1 BEGIN DROP TABLE m.Users; END");

        Assert.Single(found);
        Assert.Contains("DROP TABLE", found[0]);
    }

    [Fact]
    public void Guard_IgnoresUnparseableText()
    {
        // The run itself surfaces the parse error; the guard must not throw on the way there.
        Assert.Empty(WorkstationRunGuard.FindDestructiveStatements("this is not a script ("));
    }

    [Fact]
    public async Task Run_RefusesDestructiveScriptUntilConfirmed()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };

        async Task<RunResponse?> PostRun(bool confirm)
        {
            using var run = new HttpRequestMessage(HttpMethod.Post, "/api/run");
            run.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
            run.Content = JsonContent.Create(new RunRequest(
                "CREATE CONNECTION m AS MOCKDB();\nDROP TABLE m.Users;",
                null,
                "pipeline.etlsql",
                100,
                confirm));
            var response = await client.SendAsync(run);
            return await response.Content.ReadFromJsonAsync<RunResponse>();
        }

        var refused = await PostRun(confirm: false);
        Assert.NotNull(refused);
        Assert.False(refused!.Success);
        Assert.Contains(refused.Diagnostics, d => d.Code == "RUN_DESTRUCTIVE");
        Assert.Contains("DROP TABLE m.Users", refused.Message);

        // Confirming gets past the guard — the guard gates, it does not forbid.
        var confirmed = await PostRun(confirm: true);
        Assert.NotNull(confirmed);
        Assert.DoesNotContain(confirmed!.Diagnostics, d => d.Code == "RUN_DESTRUCTIVE");
    }

    [Fact]
    public async Task FormatterConfig_GetAndPost_SavesToEtlsqlFormatterJson()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };

        // 1. GET initial options
        using var getReq = new HttpRequestMessage(HttpMethod.Get, "/api/formatter/config");
        getReq.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        var getRes = await client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var defaultOpts = await getRes.Content.ReadFromJsonAsync<ETL_SQL.Core.Formatting.FormatterOptions>();
        Assert.NotNull(defaultOpts);
        Assert.Equal("upper", defaultOpts!.KeywordCasing);

        // 2. POST updated options (lower casing, 2 spaces)
        var updatedOpts = new ETL_SQL.Core.Formatting.FormatterOptions
        {
            KeywordCasing = "lower",
            IndentSize = 2,
            CommaPlacement = "trailing",
            IndentJoins = true,
        };

        using var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/formatter/config");
        postReq.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        postReq.Content = JsonContent.Create(updatedOpts);
        var postRes = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.OK, postRes.StatusCode);

        // 3. Verify .etlsql-formatter.json was created on disk
        string configFile = Path.Combine(temp.Root, ".etlsql-formatter.json");
        Assert.True(File.Exists(configFile));
        string json = await File.ReadAllTextAsync(configFile);
        Assert.Contains("\"KeywordCasing\": \"lower\"", json);

        // 4. Verify /api/format uses the saved options
        using var formatReq = new HttpRequestMessage(HttpMethod.Post, "/api/format");
        formatReq.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        formatReq.Content = JsonContent.Create(new FormatRequest(
            "SELECT id, name FROM customers;",
            "pipeline.etlsql"));
        var formatRes = await client.SendAsync(formatReq);
        Assert.Equal(HttpStatusCode.OK, formatRes.StatusCode);
        var formatBody = await formatRes.Content.ReadFromJsonAsync<FormatResponse>();
        Assert.NotNull(formatBody);
        Assert.Contains("select", formatBody!.Script);
        Assert.Contains("from", formatBody.Script);
    }

    [Fact]
    public async Task GitStatus_GetAndPost_ReturnsStatusAndCommitResponse()
    {
        using var temp = new TempWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };

        // 1. GET /api/git/status on non-git directory returns empty gracefully
        using var getReq = new HttpRequestMessage(HttpMethod.Get, "/api/git/status");
        getReq.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        var getRes = await client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var status = await getRes.Content.ReadFromJsonAsync<GitStatusResponse>();
        Assert.NotNull(status);
        Assert.False(status!.IsGitRepository);

        // 2. POST /api/git/commit without message returns error
        using var commitReq = new HttpRequestMessage(HttpMethod.Post, "/api/git/commit");
        commitReq.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        commitReq.Content = JsonContent.Create(new GitCommitRequest("  "));
        var commitRes = await client.SendAsync(commitReq);
        Assert.Equal(HttpStatusCode.OK, commitRes.StatusCode);
        var commitBody = await commitRes.Content.ReadFromJsonAsync<GitCommitResponse>();
        Assert.NotNull(commitBody);
        Assert.False(commitBody!.Committed);
        Assert.Equal("Commit message cannot be empty.", commitBody.Message);

        // 3. POST /api/git/commit in non-git directory returns failure rather than false success
        using var commitFailReq = new HttpRequestMessage(HttpMethod.Post, "/api/git/commit");
        commitFailReq.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        commitFailReq.Content = JsonContent.Create(new GitCommitRequest("Initial commit"));
        var commitFailRes = await client.SendAsync(commitFailReq);
        Assert.Equal(HttpStatusCode.OK, commitFailRes.StatusCode);
        var commitFailBody = await commitFailRes.Content.ReadFromJsonAsync<GitCommitResponse>();
        Assert.NotNull(commitFailBody);
        Assert.False(commitFailBody!.Committed);
        Assert.NotNull(commitFailBody.Message);
    }

    [Fact]
    public async Task GitHistoryAndDiff_CompareUnsavedContentWithLocalRevision()
    {
        using var temp = new TempWorkspace();
        RunGit(temp.Root, "init");
        RunGit(temp.Root, "config", "user.email", "workstation-tests@example.invalid");
        RunGit(temp.Root, "config", "user.name", "Workstation Tests");
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "pipeline.etlsql"), "SELECT 1 AS Value;\n");
        RunGit(temp.Root, "add", "pipeline.etlsql");
        RunGit(temp.Root, "commit", "-m", "Add pipeline");

        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };

        using var historyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/git/history?path=pipeline.etlsql");
        historyRequest.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        var historyResponse = await client.SendAsync(historyRequest);
        var history = await historyResponse.Content.ReadFromJsonAsync<GitHistoryResponse>();

        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.NotNull(history);
        Assert.True(history!.IsGitRepository);
        var entry = Assert.Single(history.Entries);
        Assert.Equal("Add pipeline", entry.Subject);

        using var diffRequest = new HttpRequestMessage(HttpMethod.Post, "/api/git/diff");
        diffRequest.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        diffRequest.Content = JsonContent.Create(new GitDiffRequest(
            "pipeline.etlsql",
            "SELECT 2 AS Value;\n-- unsaved\n",
            entry.Revision));
        var diffResponse = await client.SendAsync(diffRequest);
        var diff = await diffResponse.Content.ReadFromJsonAsync<GitDiffResponse>();

        Assert.Equal(HttpStatusCode.OK, diffResponse.StatusCode);
        Assert.NotNull(diff);
        Assert.Equal("pipeline.etlsql", diff!.Path);
        Assert.Equal("SELECT 1 AS Value;\n", diff.BaselineContent);
        Assert.Contains("-- unsaved", diff.WorkingContent, StringComparison.Ordinal);

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/api/git/diff");
        invalidRequest.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        invalidRequest.Content = JsonContent.Create(new GitDiffRequest("pipeline.etlsql", "SELECT 2;", "HEAD~1"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(invalidRequest)).StatusCode);
    }

    [Fact]
    public async Task GitCommit_WithTrailingBackslashMessage_CommitsSuccessfully()
    {
        using var temp = new TempWorkspace();
        RunGit(temp.Root, "init");
        RunGit(temp.Root, "config", "user.email", "workstation-tests@example.invalid");
        RunGit(temp.Root, "config", "user.name", "Workstation Tests");
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "pipeline.etlsql"), "SELECT 1;");

        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var commitReq = new HttpRequestMessage(HttpMethod.Post, "/api/git/commit");
        commitReq.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        commitReq.Content = JsonContent.Create(new GitCommitRequest(@"Path C:\"));

        var commitRes = await client.SendAsync(commitReq);
        Assert.Equal(HttpStatusCode.OK, commitRes.StatusCode);
        var commitBody = await commitRes.Content.ReadFromJsonAsync<GitCommitResponse>();

        Assert.NotNull(commitBody);
        Assert.True(commitBody!.Committed, commitBody.Message);
        Assert.False(string.IsNullOrWhiteSpace(commitBody.SourceRevision));
    }

    [Fact]
    public async Task GitCommit_StagesOnlyEditableScriptFiles()
    {
        using var temp = new TempWorkspace();
        RunGit(temp.Root, "init");
        RunGit(temp.Root, "config", "user.email", "workstation-tests@example.invalid");
        RunGit(temp.Root, "config", "user.name", "Workstation Tests");

        await File.WriteAllTextAsync(Path.Combine(temp.Root, "pipeline.etlsql"), "SELECT 1;");
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "local-secret.txt"), "PASSWORD=not-for-commit");
        RunGit(temp.Root, "add", "local-secret.txt");

        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token"));
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var commitReq = new HttpRequestMessage(HttpMethod.Post, "/api/git/commit");
        commitReq.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        commitReq.Content = JsonContent.Create(new GitCommitRequest("Commit pipeline script"));

        var commitRes = await client.SendAsync(commitReq);
        Assert.Equal(HttpStatusCode.OK, commitRes.StatusCode);
        var commitBody = await commitRes.Content.ReadFromJsonAsync<GitCommitResponse>();

        Assert.NotNull(commitBody);
        Assert.True(commitBody!.Committed, commitBody.Message);

        var committedFiles = RunGitCapture(temp.Root, "show", "--name-only", "--format=", "HEAD");
        Assert.Contains("pipeline.etlsql", committedFiles, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("local-secret.txt", committedFiles, StringComparison.OrdinalIgnoreCase);
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        _ = RunGitCapture(workingDirectory, arguments);
    }

    private static string RunGitCapture(string workingDirectory, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
        if (!process.WaitForExit(5000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} timed out.");
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {output}{error}");
        }

        return output;
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
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
                }

                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public bool StopRequested { get; private set; }
        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication()
        {
            StopRequested = true;
            _stopping.Cancel();
        }
    }

    private sealed class AlwaysHealthyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
