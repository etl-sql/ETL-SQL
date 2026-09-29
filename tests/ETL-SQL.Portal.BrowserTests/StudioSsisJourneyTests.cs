using ETL_SQL.WorkstationEditor;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The SSIS-like ETL journey, driven from the GUI on a production host.
///
/// <para>Extract, stage into <c>#temp</c>, validate, transform, branch into explicit parallel work,
/// load, and inspect intermediate state — built the way an author builds it and then handed to
/// <see cref="StudioCertification"/>, so the verdict is the same contract the other certified
/// journeys are held to.</para>
///
/// <para><b>Why the desktop host.</b> A pipeline is a <c>.etlsql</c> file in a workspace. The Portal
/// catalog stores reports, and its interactive-run policy refuses the statements a pipeline is made
/// of, so the desktop host is not a convenience here — it is the host that owns this artifact.</para>
///
/// <para><b>Why two surfaces.</b> Staging into <c>#temp</c> is a top-level ETL-SQL statement and the
/// palette has no chip for it; an execution task is an <c>EXECUTE conn BEGIN … END</c> block, which
/// runs SQL on the remote engine and is the wrong shape for staging. So extract and transform are
/// authored in the code pane and validation, the parallel branch, and the loads on the canvas — which
/// is the honest division, and exercises the code ↔ canvas round-trip the contract asks for rather
/// than pretending one surface does everything.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioSsisJourneyTests(StudioAuthoringFixture fixture)
{
    /// <summary>Only the connection. Everything else in the file is authored by the journey.</summary>
    private const string Seed = "CREATE CONNECTION sample_data AS MOCKDB();\n";

    [Fact]
    public async Task Certifies_TheSsisLikeEtlJourney()
    {
        using var workspace = new StudioTempWorkspace();
        var file = Path.Combine(workspace.Root, "nightly_load.etlsql");
        await File.WriteAllTextAsync(file, Seed);

        await using var host = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            workspace.Root, file, 0, false, "ssis-token",
            StudioMode: true, InstanceId: Guid.NewGuid().ToString("D")));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token=ssis-token");
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await page.Locator("[data-projection='split']").ClickAsync();

        // ── Extract and stage ────────────────────────────────────────────────
        // A top-level statement, because that is what staging into #temp is. It shows up on the map
        // as a stage the canvas reports but does not own.
        await AppendToScriptAsync(page,
            "SELECT UserID, UserName INTO #staged FROM sample_data.Users;");

        // ── Transform ────────────────────────────────────────────────────────
        await AppendToScriptAsync(page,
            "SELECT UserID, UPPER(UserName) AS UserNameUpper INTO #transformed FROM #staged;");

        // ── Validate ─────────────────────────────────────────────────────────
        await AddTaskAsync(page, "validation", "staged_rows_arrived", new Dictionary<string, string>
        {
            ["condition"] = "(SELECT COUNT(*) FROM #staged) > 0",
            ["message"] = "No rows were staged.",
        });

        // ── Branch into explicit parallel work ───────────────────────────────
        // A PARALLEL block is the only thing in ETL-SQL that means concurrency, and it is created
        // empty and filled by dragging tasks in.
        await AddTaskAsync(page, "parallel", "load_fanout", []);

        await AddExecutionTaskAsync(page, "load_primary",
            "CREATE TABLE loaded_users (UserID INT, UserNameUpper VARCHAR);");
        await NestAsync(page, "load_primary", "load_fanout");

        await AddExecutionTaskAsync(page, "load_audit",
            "CREATE TABLE load_audit (LoadedAt VARCHAR);");
        await NestAsync(page, "load_audit", "load_fanout");

        // ── Inspect intermediate state ───────────────────────────────────────
        await SelectTaskAsync(page, "staged_rows_arrived");
        var scope = page.Locator("[data-task-scope]");
        await scope.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        // The panel reads the script asynchronously and says so while it does. Reading it before it
        // has resolved asserts nothing, so wait for it to stop saying "Reading the script".
        await page.WaitForFunctionAsync(
            "() => { const host = document.querySelector('[data-task-scope]');"
            + " return host && !host.textContent.includes('Reading the script'); }",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });
        var scopeText = await scope.InnerTextAsync();
        Assert.Contains("#staged", scopeText, StringComparison.Ordinal);

        // ── Save, reload, certify ────────────────────────────────────────────
        await page.Locator("[data-action='save']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === false");

        var saved = await File.ReadAllTextAsync(file);
        Assert.Contains("#staged", saved, StringComparison.Ordinal);
        Assert.Contains("#transformed", saved, StringComparison.Ordinal);
        Assert.Contains("PARALLEL", saved, StringComparison.OrdinalIgnoreCase);

        StudioCertification.Certify(
            new CertifiedArtifact("SSIS-like ETL", StudioHost.Desktop, "nightly_load.etlsql", saved),
            saved);
        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// An IF built on the canvas is given an ELSE there, and a task is put in each branch — the SSIS
    /// "precedence on true / on false" shape, written as the ETL-SQL an author would type.
    ///
    /// <para>On the real host, because the sandbox draws a canned map: the first defect this found was
    /// that a labelled IF reached the real map without its key, so its card was not a canvas task at
    /// all and nothing could be dropped into it.</para>
    /// </summary>
    [Fact]
    public async Task AnIfIsGivenAnElseAndEachBranchATask()
    {
        using var workspace = new StudioTempWorkspace();
        var file = Path.Combine(workspace.Root, "branching.etlsql");
        await File.WriteAllTextAsync(file, Seed);

        await using var host = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            workspace.Root, file, 0, false, "branch-token",
            StudioMode: true, InstanceId: Guid.NewGuid().ToString("D")));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token=branch-token");
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await page.Locator("[data-projection='split']").ClickAsync();

        await AddTaskAsync(page, "if", "orders_ready", new Dictionary<string, string>
        {
            ["condition"] = "(SELECT COUNT(*) FROM sample_data.Users) > 0",
        });

        // Selecting the IF is only possible when the map keyed its card.
        await SelectTaskAsync(page, "orders_ready");
        await page.Locator("[data-task-add-else]").ClickAsync();
        var elseCard = page.Locator("[data-task-key='orders_ready:else']");
        await elseCard.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        await AddExecutionTaskAsync(page, "load_primary", "SELECT 1 AS loaded;");
        await NestAsync(page, "load_primary", "orders_ready");

        await AddExecutionTaskAsync(page, "load_fallback", "SELECT 0 AS loaded;");
        await page.Locator("[data-task-key='load_fallback']").DragToAsync(elseCard);
        await WaitForBranchesAsync(page, "orders_ready", ["load_primary"], ["load_fallback"]);

        // An ELSE that holds work is not removed out from under it: the refusal is said, not swallowed.
        await SelectTaskAsync(page, "orders_ready");
        var before = await ScriptAsync(page);
        await page.Locator("[data-task-remove-else]").ClickAsync();
        await page.Locator(".etlsql-feedback-toast", new PageLocatorOptions { HasTextString = "load_fallback" })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        Assert.Equal(before, await ScriptAsync(page));

        // Saved and reopened, the branches are still two cards with a task each.
        await page.Locator("[data-action='save']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === false");
        var saved = await File.ReadAllTextAsync(file);
        AssertBranches(saved, "orders_ready", ["load_primary"], ["load_fallback"]);

        await page.ReloadAsync();
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await page.Locator("[data-projection='split']").ClickAsync();
        await page.Locator("[data-task-key='orders_ready:else']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// Editing an existing task changes its fields, not only its label, and changes nothing else.
    ///
    /// <para>The editor used to offer the label alone for every kind but execution and FOREACH: a
    /// validation's condition and message could only be changed by finding them in the script.</para>
    /// </summary>
    [Fact]
    public async Task ATasksFieldsAreEditedInPlace()
    {
        using var workspace = new StudioTempWorkspace();
        var file = Path.Combine(workspace.Root, "editing.etlsql");
        await File.WriteAllTextAsync(file, Seed);

        await using var host = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            workspace.Root, file, 0, false, "edit-token",
            StudioMode: true, InstanceId: Guid.NewGuid().ToString("D")));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token=edit-token");
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await page.Locator("[data-projection='split']").ClickAsync();

        await AddTaskAsync(page, "validation", "users_arrived", new Dictionary<string, string>
        {
            ["condition"] = "(SELECT COUNT(*) FROM sample_data.Users) > 0",
            ["message"] = "No users.",
        });
        var before = await ScriptAsync(page);

        await SelectTaskAsync(page, "users_arrived");
        await page.Locator("[data-task-edit]").ClickAsync();
        var message = page.Locator("[data-task-field='message']");
        await message.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        Assert.Equal("No users.", await message.InputValueAsync());
        Assert.Equal("(SELECT COUNT(*) FROM sample_data.Users) > 0",
            await page.Locator("[data-task-field='condition']").InputValueAsync());

        await message.FillAsync("The user feed arrived empty.");
        await page.Locator("[data-dialog-action='save']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.editorInstance.getValue().includes('The user feed arrived empty.')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        Assert.Equal(
            before.Replace("'No users.'", "'The user feed arrived empty.'", StringComparison.Ordinal),
            await ScriptAsync(page));
        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// Each branch of a PARALLEL is its own row on the map, inside a band that says it runs in
    /// parallel, and the step after the block comes after all of them.
    ///
    /// <para>Before, stages were placed by depth alone: a two-step branch and a one-step branch shared
    /// rows, and nothing on the map said which steps ran side by side.</para>
    /// </summary>
    [Fact]
    public async Task ParallelBranchesAreDrawnAsLanes()
    {
        using var workspace = new StudioTempWorkspace();
        var file = Path.Combine(workspace.Root, "lanes.etlsql");
        await File.WriteAllTextAsync(file, Seed + """

            load_fanout:
            PARALLEL BEGIN
                load_primary:
                EXECUTE sample_data BEGIN
                    SELECT 1 AS loaded;
                END;

                BEGIN
                    stage_audit:
                    EXECUTE sample_data BEGIN
                        SELECT 2 AS staged;
                    END;

                    load_audit:
                    EXECUTE sample_data BEGIN
                        SELECT 3 AS loaded;
                    END;
                END;
            END;

            after_load:
            EXECUTE sample_data BEGIN
                SELECT 4 AS done;
            END;
            """);

        await using var host = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            workspace.Root, file, 0, false, "lane-token",
            StudioMode: true, InstanceId: Guid.NewGuid().ToString("D")));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token=lane-token");
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await page.Locator("[data-projection='split']").ClickAsync();
        await page.Locator("[data-task-key='after_load']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await page.WaitForFunctionAsync("() => document.querySelectorAll('[data-dag-lane]').length === 2", null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

        var measured = await page.EvaluateAsync<System.Text.Json.JsonElement>("""
            () => {
              const box = key => document.querySelector(`[data-task-key="${key}"]`).getBoundingClientRect();
              const block = document.querySelector('[data-task-key="load_fanout"]').dataset.dagNode;
              const band = lane => document.querySelector(`[data-dag-lane="${block}:${lane}"]`);
              const plain = r => ({ left: r.left, right: r.right, top: r.top, bottom: r.bottom });
              return {
                primary: plain(box('load_primary')),
                stage: plain(box('stage_audit')),
                audit: plain(box('load_audit')),
                after: plain(box('after_load')),
                band0: plain(band(0).getBoundingClientRect()),
                band1: plain(band(1).getBoundingClientRect()),
                label0: band(0).textContent,
                label1: band(1).textContent,
              };
            }
            """);

        double Get(string box, string edge) => measured.GetProperty(box).GetProperty(edge).GetDouble();
        double Centre(string box) => (Get(box, "top") + Get(box, "bottom")) / 2;
        bool Inside(string inner, string outer) =>
            Get(inner, "left") >= Get(outer, "left") && Get(inner, "right") <= Get(outer, "right")
            && Get(inner, "top") >= Get(outer, "top") && Get(inner, "bottom") <= Get(outer, "bottom");

        // The two-step branch is one row, and it is not the one-step branch's row.
        Assert.InRange(Centre("audit") - Centre("stage"), -1, 1);
        Assert.True(Get("band0", "bottom") <= Get("band1", "top") || Get("band1", "bottom") <= Get("band0", "top"),
            "The two branch bands overlap.");

        // Each band wraps its own branch and not the other one.
        Assert.True(Inside("primary", "band0"), "Branch 1's step is outside its band.");
        Assert.True(Inside("stage", "band1") && Inside("audit", "band1"), "Branch 2's steps are outside their band.");
        Assert.False(Inside("primary", "band1"));
        Assert.Contains("Branch 1", measured.GetProperty("label0").GetString(), StringComparison.Ordinal);
        Assert.Contains("Branch 2", measured.GetProperty("label1").GetString(), StringComparison.Ordinal);

        // The join comes after both branches.
        Assert.True(Get("after", "left") > Math.Max(Get("band0", "right"), Get("band1", "right")),
            "The step after the PARALLEL is not after its branches.");
        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// An extract and a load, built entirely from the palette: pick a table from the connection, tick
    /// its columns, name the #temp, and watch the statement being written before it is added.
    ///
    /// <para>This is the part of SSIS the canvas could not do at all. Staging needed typing, and the
    /// only data chip ran vendor SQL on the remote database.</para>
    /// </summary>
    [Fact]
    public async Task AnExtractAndALoadAreBuiltFromThePalette()
    {
        using var workspace = new StudioTempWorkspace();
        var file = Path.Combine(workspace.Root, "extract_load.etlsql");
        await File.WriteAllTextAsync(file, Seed);

        await using var host = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            workspace.Root, file, 0, false, "data-token",
            StudioMode: true, InstanceId: Guid.NewGuid().ToString("D")));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token=data-token");
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await page.Locator("[data-projection='split']").ClickAsync();

        // ── Read a table ─────────────────────────────────────────────────────
        await page.Locator("[data-task-kind='extract']").ClickAsync();
        await page.Locator("[data-task-id]").FillAsync("read_users");

        // The table list is the connection's own, read from its schema.
        try
        {
            await page.Locator("datalist[id^='etlsql-task-tables'] option[value='Users']").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        }
        catch (TimeoutException exception)
        {
            var note = await page.Locator("[data-schema-note]").InnerTextAsync();
            var connection = await page.Locator("[data-task-connection]").InputValueAsync();
            var options = await page.Locator("datalist[id^='etlsql-task-tables'] option").CountAsync();
            throw new Xunit.Sdk.XunitException(
                $"The table suggestions never arrived. Connection: '{connection}'; options: {options}; "
                + $"schema note: '{note}'.", exception);
        }
        var table = page.Locator("[data-task-field='table']");
        await table.FillAsync("Users");
        await table.DispatchEventAsync("change");

        // Its columns are ticked rather than typed.
        await page.Locator("[data-column-pick='columns'][value='UserID']").CheckAsync();
        await page.Locator("[data-column-pick='columns'][value='UserName']").CheckAsync();
        Assert.Equal("UserID, UserName", await page.Locator("[data-task-field='columns']").InputValueAsync());
        await page.Locator("[data-task-field='target']").FillAsync("#staged_users");

        // The statement is on screen before anything is written.
        const string extract = "read_users:\nSELECT UserID, UserName\nINTO #staged_users\nFROM sample_data.Users;";
        await page.WaitForFunctionAsync(
            "text => document.querySelector('[data-task-preview]')?.textContent === text", extract,
            new PageWaitForFunctionOptions { Timeout = 15_000 });
        Assert.DoesNotContain("read_users", await ScriptAsync(page), StringComparison.Ordinal);
        await CommitTaskAsync(page, "read_users");

        // ── Insert rows ──────────────────────────────────────────────────────
        await page.Locator("[data-task-kind='load']").ClickAsync();
        await page.Locator("[data-task-id]").FillAsync("load_users");
        await page.Locator("[data-task-field='source']").FillAsync("#staged_users");
        await page.Locator("[data-task-field='table']").FillAsync("Users");
        await page.Locator("[data-task-field='columns']").FillAsync("UserID, UserName");
        await CommitTaskAsync(page, "load_users");

        var script = (await ScriptAsync(page)).Replace("\r\n", "\n", StringComparison.Ordinal);
        const string load = "load_users:\nINSERT INTO sample_data.Users (UserID, UserName)\nSELECT UserID, UserName\nFROM #staged_users;";
        Assert.Contains(extract, script, StringComparison.Ordinal);
        Assert.Contains(load, script, StringComparison.Ordinal);
        Assert.True(script.IndexOf(extract, StringComparison.Ordinal) < script.IndexOf(load, StringComparison.Ordinal),
            "The load was written before the extract it reads from.");

        // Both are tasks on the map, not read-only stages.
        await page.Locator("[data-task-key='read_users']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await page.Locator("[data-task-key='load_users']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// Transforms built from the palette, each reading the #temp an earlier step staged: the #temp
    /// tables are offered by name, and their columns are ticked rather than typed.
    /// </summary>
    [Fact]
    public async Task TransformsAreBuiltFromThePaletteOnStagedTables()
    {
        using var workspace = new StudioTempWorkspace();
        var file = Path.Combine(workspace.Root, "transforms.etlsql");
        await File.WriteAllTextAsync(file, Seed + """

            read_users:
            SELECT UserID, UserName
            INTO #staged_users
            FROM sample_data.Users;
            """);

        await using var host = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            workspace.Root, file, 0, false, "transform-token",
            StudioMode: true, InstanceId: Guid.NewGuid().ToString("D")));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token=transform-token");
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await page.Locator("[data-projection='split']").ClickAsync();

        // ── Filter & pick columns ────────────────────────────────────────────
        await page.Locator("[data-task-kind='reshape']").ClickAsync();
        await page.Locator("[data-task-id]").FillAsync("clean_users");
        await page.Locator("datalist[id^='etlsql-task-temps'] option[value='#staged_users']").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        var source = page.Locator("[data-task-field='source']");
        await source.FillAsync("#staged_users");
        await source.DispatchEventAsync("change");
        await page.Locator("[data-column-pick='columns'][value='UserID']").CheckAsync();
        await page.Locator("[data-task-field='derived']").FillAsync("UPPER(UserName) AS UserNameUpper");
        await page.Locator("[data-task-field='target']").FillAsync("#clean_users");
        const string reshape = "clean_users:\nSELECT UserID, UPPER(UserName) AS UserNameUpper\nINTO #clean_users\nFROM #staged_users;";
        await page.WaitForFunctionAsync(
            "text => document.querySelector('[data-task-preview]')?.textContent === text", reshape,
            new PageWaitForFunctionOptions { Timeout = 15_000 });
        await CommitTaskAsync(page, "clean_users");

        // ── Summarise ────────────────────────────────────────────────────────
        await page.Locator("[data-task-kind='summarise']").ClickAsync();
        await page.Locator("[data-task-id]").FillAsync("count_names");
        var summarySource = page.Locator("[data-task-field='source']");
        await summarySource.FillAsync("#clean_users");
        await summarySource.DispatchEventAsync("change");
        // The column the reshape calculated is offered by the name it was given.
        await page.Locator("[data-column-pick='columns'][value='UserNameUpper']").CheckAsync(
            new LocatorCheckOptions { Timeout = 15_000 });
        await page.Locator("[data-task-field='measures']").FillAsync("COUNT(*) AS Users");
        await page.Locator("[data-task-field='target']").FillAsync("#name_counts");
        await CommitTaskAsync(page, "count_names");

        // ── Join ─────────────────────────────────────────────────────────────
        await page.Locator("[data-task-kind='join']").ClickAsync();
        await page.Locator("[data-task-id]").FillAsync("with_counts");
        var left = page.Locator("[data-task-field='source']");
        await left.FillAsync("#clean_users");
        await left.DispatchEventAsync("change");
        var right = page.Locator("[data-task-field='right']");
        await right.FillAsync("#name_counts");
        await right.DispatchEventAsync("change");
        await page.Locator("[data-task-field='jointype']").SelectOptionAsync("LEFT");
        await page.Locator("[data-column-pick='keys'][value='UserNameUpper']").CheckAsync(new LocatorCheckOptions { Timeout = 15_000 });
        await page.Locator("[data-column-pick='columns'][value='Users']").CheckAsync(new LocatorCheckOptions { Timeout = 15_000 });
        await page.Locator("[data-task-field='target']").FillAsync("#with_counts");
        await CommitTaskAsync(page, "with_counts");

        var script = (await ScriptAsync(page)).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(reshape, script, StringComparison.Ordinal);
        Assert.Contains("count_names:\nSELECT UserNameUpper, COUNT(*) AS Users\nINTO #name_counts\nFROM #clean_users\nGROUP BY UserNameUpper;",
            script, StringComparison.Ordinal);
        Assert.Contains("with_counts:\nSELECT l.*, r.Users\nINTO #with_counts\nFROM #clean_users AS l\nLEFT JOIN #name_counts AS r\n    ON l.UserNameUpper = r.UserNameUpper;",
            script, StringComparison.Ordinal);

        // ── Check and tidy ───────────────────────────────────────────────────
        await page.Locator("[data-task-kind='expectschema']").ClickAsync();
        await page.Locator("[data-task-id]").FillAsync("counts_have_shape");
        await page.Locator("[data-task-field='source']").FillAsync("#name_counts");
        await page.Locator("[data-task-field='schema']").FillAsync("UserNameUpper VARCHAR, Users INT");
        await page.Locator("[data-task-field='warnonly']").CheckAsync();
        await CommitTaskAsync(page, "counts_have_shape");

        await page.Locator("[data-task-kind='droptemp']").ClickAsync();
        await page.Locator("[data-task-id]").FillAsync("free_staging");
        await page.Locator("[data-task-field='source']").FillAsync("#staged_users");
        await CommitTaskAsync(page, "free_staging");

        script = (await ScriptAsync(page)).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("counts_have_shape:\nEXPECT SCHEMA #name_counts (\n    UserNameUpper VARCHAR,\n    Users INT\n) ON DRIFT WARN;",
            script, StringComparison.Ordinal);
        Assert.Contains("free_staging:\nDROP TABLE #staged_users;", script, StringComparison.Ordinal);

        // ── The map reads as a pipeline ──────────────────────────────────────
        // A step is called what its author named it, with the statement underneath.
        var card = page.Locator("[data-task-key='clean_users']");
        await card.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        Assert.Equal("clean_users", (await card.Locator(".etlsql-dag-card-title").InnerTextAsync()).Trim());
        Assert.Contains("#clean_users", await card.Locator(".etlsql-dag-card-statement").InnerTextAsync(), StringComparison.Ordinal);

        // The staged table is drawn where it flows: into the step that reads it, and into the drop.
        var readId = await page.Locator("[data-task-key='read_users']").GetAttributeAsync("data-dag-node");
        var cleanId = await card.GetAttributeAsync("data-dag-node");
        var dropId = await page.Locator("[data-task-key='free_staging']").GetAttributeAsync("data-dag-node");
        await AssertEdgeDrawnAsync(page.Locator(
            $"[data-dag-source='{readId}'][data-dag-target='{cleanId}'][data-dag-label='#staged_users'][data-dag-edge-kind='data']"),
            "#staged_users into clean_users");
        var longLine = page.Locator(
            $"[data-dag-source='{readId}'][data-dag-target='{dropId}'][data-dag-label='#staged_users'][data-dag-edge-kind='data']");
        await AssertEdgeDrawnAsync(longLine, "#staged_users into free_staging");

        // A hand-over to the next step is a straight line; one that skips steps arcs under the row,
        // rather than running behind every card between and sitting on their lines.
        Assert.Equal("0", await page.Locator($"[data-dag-source='{readId}'][data-dag-target='{cleanId}'][data-dag-edge-kind='data']")
            .GetAttributeAsync("data-dag-bow"));
        Assert.True(int.Parse(await longLine.GetAttributeAsync("data-dag-bow") ?? "0", System.Globalization.CultureInfo.InvariantCulture) > 0,
            "The long data line is drawn straight through the steps it skips.");
        Assert.Contains("DROP TABLE #staged_users",
            await page.Locator("[data-task-key='free_staging'] .etlsql-dag-card-statement").InnerTextAsync(), StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
    }

    /// <summary>Waits for an edge to be in the document with path data, without waiting for visibility.</summary>
    private static async Task AssertEdgeDrawnAsync(ILocator edge, string label)
    {
        try
        {
            await edge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        }
        catch (TimeoutException exception)
        {
            throw new Xunit.Sdk.XunitException($"The {label} line was never drawn.", exception);
        }

        Assert.False(string.IsNullOrWhiteSpace(await edge.GetAttributeAsync("d")), $"The {label} line has no path data.");
    }

    private static Task<string> ScriptAsync(IPage page) =>
        page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");

    /// <summary>
    /// Waits for each branch to hold exactly the named tasks, read by the canonical parser — the same
    /// thing the engine will run, rather than a guess from where the words fall in the text.
    /// </summary>
    private static async Task WaitForBranchesAsync(IPage page, string ifLabel, string[] inIf, string[] inElse)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var script = await ScriptAsync(page);
            try
            {
                AssertBranches(script, ifLabel, inIf, inElse);
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
            }
            catch (Exception exception)
            {
                var toasts = await page.Locator(".etlsql-feedback-toast").AllInnerTextsAsync();
                throw new Xunit.Sdk.XunitException(
                    $"The branches of '{ifLabel}' never held what was dropped on them. "
                    + $"Feedback said: {(toasts.Count == 0 ? "(nothing)" : string.Join(" | ", toasts))}"
                    + $"{Environment.NewLine}{exception.Message}", exception);
            }
        }
    }

    private static void AssertBranches(string script, string ifLabel, string[] inIf, string[] inElse)
    {
        var statements = new ETL_SQL.Core.Parser.Parser(new ETL_SQL.Core.Parser.Lexer(script).Tokenize(), script).Parse().Statements;
        var index = statements.ToList().FindIndex(statement =>
            statement is ETL_SQL.Core.SectionLabelStatement label && label.LabelName == ifLabel);
        Assert.True(index >= 0 && index + 1 < statements.Count, $"No '{ifLabel}' in:{Environment.NewLine}{script}");
        var branch = Assert.IsType<ETL_SQL.Core.IfStatement>(statements[index + 1]);

        static string[] Labels(ETL_SQL.Core.Statement? body) =>
            (body as ETL_SQL.Core.BlockStatement)?.Statements
                .OfType<ETL_SQL.Core.SectionLabelStatement>().Select(label => label.LabelName).ToArray() ?? [];

        Assert.Equal(inIf, Labels(branch.IfBody));
        Assert.Equal(inElse, Labels(branch.ElseBody));
    }

    // ── Journey helpers ──────────────────────────────────────────────────────

    /// <summary>Appends a statement in the code pane, the way an author types one.</summary>
    private static async Task AppendToScriptAsync(IPage page, string statement)
    {
        await page.EvaluateAsync(
            "text => { const editor = window.__STUDIO__.state.editorInstance;"
            + " editor.setValue(editor.getValue().replace(/\\s*$/, '') + '\\n\\n' + text + '\\n'); }",
            statement);
        await page.WaitForFunctionAsync(
            "text => window.__STUDIO__.state.editorInstance.getValue().includes(text)", statement,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    /// <summary>Adds a task from the palette and fills its editor.</summary>
    private static async Task AddTaskAsync(IPage page, string kind, string label, Dictionary<string, string> fields)
    {
        await page.Locator($"[data-task-kind='{kind}']").ClickAsync();
        var dialog = page.Locator("[data-task-id]");
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await dialog.FillAsync(label);
        foreach (var (name, value) in fields)
            await page.Locator($"[data-task-field='{name}']").FillAsync(value);
        await CommitTaskAsync(page, label);
    }

    /// <summary>Adds an execution task, whose body is typed into the query workbench.</summary>
    private static async Task AddExecutionTaskAsync(IPage page, string label, string body)
    {
        await page.Locator("[data-task-kind='execution']").ClickAsync();
        var id = page.Locator("[data-task-id]");
        await id.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await id.FillAsync(label);

        var editor = page.Locator("[data-task-workbench] .cm-content");
        await editor.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await editor.ClickAsync();
        // InsertText rather than typing: it raises one `insertText` input event, so CodeMirror's
        // bracket-closing key handlers never fire and the SQL that lands is the SQL that was asked
        // for rather than the SQL plus whatever the editor helpfully added.
        await page.Keyboard.InsertTextAsync(body);
        await CommitTaskAsync(page, label);
    }

    private static async Task CommitTaskAsync(IPage page, string label)
    {
        await page.Locator("[data-dialog-action='save']").ClickAsync();
        try
        {
            await page.WaitForFunctionAsync(
                "label => window.__STUDIO__.state.editorInstance.getValue().includes(label)", label,
                new PageWaitForFunctionOptions { Timeout = 15_000 });
        }
        catch (TimeoutException exception)
        {
            var reason = await page.Locator("[data-dialog-actions]").IsVisibleAsync()
                ? await page.Locator("[data-modal-box]").InnerTextAsync()
                : "(the dialog closed without writing the task)";
            throw new Xunit.Sdk.XunitException(
                $"Adding the task '{label}' wrote nothing into the script. Dialog said: {reason}", exception);
        }
    }

    private static async Task SelectTaskAsync(IPage page, string id)
    {
        var card = page.Locator($"[data-task-key='{id}']");
        await card.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await card.ClickAsync();
    }

    /// <summary>
    /// Drags a task card onto a container card, which is what puts it inside.
    ///
    /// <para>The drag starts anywhere on the card. It did not always: the map binds its own
    /// node-repositioning gesture to a card header that covers the whole card, and that gesture
    /// calls <c>preventDefault</c>, which cancelled the native drag before it began — so this
    /// gesture, and the reorder that shares it, could never fire from anywhere on any card.</para>
    /// </summary>
    private static async Task NestAsync(IPage page, string task, string container)
    {
        var card = page.Locator($"[data-task-key='{task}']");
        var target = page.Locator($"[data-task-key='{container}']");
        await card.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await target.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        try
        {
            await card.DragToAsync(target);

            // Asserted on the statement the drag was supposed to move, inside the block it was
            // supposed to move into — which means finding the block's own closing END. An earlier
            // version sliced the script from `PARALLEL` to the end of the file and asked whether the
            // task appeared anywhere in it, which every task after the block satisfies: it passed
            // while the block stayed empty and both loads sat outside it.
            await page.WaitForFunctionAsync(
                """
                names => {
                    const [task, container] = names;
                    const script = window.__STUDIO__.state.editorInstance.getValue();
                    const start = script.indexOf(container + ':');
                    if (start < 0) return false;
                    const words = script.slice(start).match(/[A-Za-z_#][A-Za-z0-9_]*|\S/g) || [];
                    let depth = 0;
                    for (let index = 0; index < words.length; index++) {
                        const word = words[index].toUpperCase();
                        if (word === 'BEGIN') depth++;
                        else if (word === 'END') { depth--; if (depth === 0) return false; }
                        else if (depth > 0 && words[index] === task) return true;
                    }
                    return false;
                }
                """,
                new[] { task, container },
                new PageWaitForFunctionOptions { Timeout = 15_000 });
        }
        catch (TimeoutException exception)
        {
            // A refused edit arrives as a toast and a nested one as a script change, so a failure
            // that shows neither is a gesture that never reached the canvas at all — which is a
            // different bug from one the host refused, and the message has to tell them apart.
            var toasts = await page.Locator(".etlsql-feedback-toast").AllInnerTextsAsync();
            var script = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");
            throw new Xunit.Sdk.XunitException(
                $"Dragging '{task}' onto the container '{container}' did not put it inside. "
                + $"Feedback said: {(toasts.Count == 0 ? "(nothing)" : string.Join(" | ", toasts))}"
                + $"{Environment.NewLine}Script:{Environment.NewLine}{script}",
                exception);
        }
    }
}
