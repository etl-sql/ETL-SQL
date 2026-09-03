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
[Collection(PortalBrowserCollection.Name)]
public sealed class StudioSsisJourneyTests(PortalBrowserFixture fixture)
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
