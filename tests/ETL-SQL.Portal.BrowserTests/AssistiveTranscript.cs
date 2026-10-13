using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Records what assistive technology is given at each step of a keyboard journey.
///
/// <para>For the focused control: the role, name, description, value and states Chromium computed
/// for its accessibility tree, which is what a screen reader reads from. For everything else: the
/// text that arrived in a live region since the last step, which is what a screen reader speaks
/// unprompted.</para>
///
/// <para><b>This is not a screen reader's speech.</b> It shows that a name, a state and an
/// announcement exist and what they say. How a given screen reader phrases, orders or drops them
/// is only learned by running one; see the pass sheet in
/// <c>docs/architecture/decisions/studio-keyboard-assistive-evidence.md</c>.</para>
/// </summary>
internal sealed class AssistiveTranscript
{
    private readonly IPage page;
    private readonly ICDPSession cdp;
    private readonly List<Entry> entries = [];

    private AssistiveTranscript(IPage page, ICDPSession cdp)
    {
        this.page = page;
        this.cdp = cdp;
    }

    public IReadOnlyList<Entry> Entries => entries;

    public sealed record Entry(string Step, string Role, string Name, string Detail, string Dialog, IReadOnlyList<string> Announced);

    /// <summary>Starts listening for live-region text. Call once the page under test has loaded.</summary>
    public static async Task<AssistiveTranscript> StartAsync(IPage page)
    {
        var transcript = new AssistiveTranscript(page, await page.Context.NewCDPSessionAsync(page));
        await transcript.ListenAsync();
        return transcript;
    }

    /// <summary>Re-attaches the live-region listener after a navigation replaced the document.</summary>
    public Task ListenAsync() => page.EvaluateAsync(
        """
        () => {
            if (window.__assistiveAnnouncements) return;
            window.__assistiveAnnouncements = [];
            const live = '[aria-live]:not([aria-live="off"]), [role="status"], [role="alert"], [role="log"]';
            const said = new WeakMap();
            // The words in a region, piece by piece: a title and a message are separate elements and
            // are read with a pause between them, and a button inside the region is a control to
            // reach, not part of what is announced.
            const spoken = region => {
                const pieces = [];
                const walker = document.createTreeWalker(region, NodeFilter.SHOW_TEXT);
                for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                    if (node.parentElement.closest('button, [aria-hidden="true"]')) continue;
                    const piece = node.textContent.replace(/\s+/g, ' ').trim();
                    if (piece) pieces.push(piece);
                }
                return pieces.join('. ').replace(/\.\. /g, '. ');
            };
            const report = node => {
                const element = node.nodeType === Node.ELEMENT_NODE ? node : node.parentElement;
                const region = element?.closest(live);
                if (!region) return;
                const text = spoken(region);
                if (!text || said.get(region) === text) return;
                said.set(region, text);
                // A toast sits inside the notification region and both are live. Whether that is
                // spoken once or twice is the screen reader's choice; here it is recorded once.
                const queue = window.__assistiveAnnouncements;
                if (queue[queue.length - 1] !== text) queue.push(text);
            };
            new MutationObserver(mutations => {
                for (const mutation of mutations) {
                    report(mutation.target);
                    mutation.addedNodes.forEach(report);
                }
            }).observe(document.documentElement, { subtree: true, childList: true, characterData: true });
        }
        """);

    /// <summary>Records the focused control and whatever was announced since the last step.</summary>
    public async Task<Entry> RecordAsync(string step)
    {
        var focused = await cdp.SendAsync("Runtime.evaluate", new Dictionary<string, object>
        {
            ["expression"] = "document.activeElement"
        });
        var objectId = focused!.Value.GetProperty("result").TryGetProperty("objectId", out var id) ? id.GetString() : null;

        string role = "", name = "", detail = "";
        if (objectId is not null)
        {
            var tree = await cdp.SendAsync("Accessibility.getPartialAXTree", new Dictionary<string, object>
            {
                ["objectId"] = objectId,
                ["fetchRelatives"] = false
            });
            var node = tree!.Value.GetProperty("nodes").EnumerateArray().FirstOrDefault();
            if (node.ValueKind == JsonValueKind.Object)
            {
                role = Text(node, "role");
                name = Text(node, "name");
                var details = new List<string>();
                if (Text(node, "description") is { Length: > 0 } description) details.Add($"described: {description}");
                if (Text(node, "value") is { Length: > 0 } value && role != "textbox") details.Add($"value: {value}");
                if (node.TryGetProperty("properties", out var properties))
                {
                    foreach (var property in properties.EnumerateArray())
                    {
                        var key = property.GetProperty("name").GetString();
                        if (key is not ("selected" or "pressed" or "expanded" or "checked" or "invalid" or "required" or "disabled" or "modal")) continue;
                        var state = property.GetProperty("value").TryGetProperty("value", out var raw) ? raw.ToString() : "";
                        if (state is "" or "False" or "false") continue;
                        details.Add(state is "True" or "true" ? key! : $"{key}: {state}");
                    }
                }
                detail = string.Join("; ", details);
            }
        }

        var surroundings = await page.EvaluateAsync<string[]>(
            """
            () => {
                const dialog = document.activeElement?.closest('[role="dialog"], dialog');
                const labelledBy = dialog?.getAttribute('aria-labelledby');
                const dialogName = dialog
                    ? (dialog.getAttribute('aria-label')
                        || (labelledBy && document.getElementById(labelledBy)?.textContent.trim())
                        || '(unnamed dialog)')
                    : '';
                const announced = (window.__assistiveAnnouncements || []).splice(0);
                return [dialogName, ...announced];
            }
            """);

        var entry = new Entry(step, role, name, detail, surroundings[0], surroundings[1..]);
        entries.Add(entry);
        return entry;
    }

    /// <summary>
    /// Waits for an announcement containing <paramref name="text"/> and returns it. The announcement
    /// stays queued, so the next <see cref="RecordAsync"/> still shows it against its step.
    /// </summary>
    public async Task<string> WaitForAnnouncementAsync(string text)
    {
        try
        {
            var handle = await page.WaitForFunctionAsync(
                "text => (window.__assistiveAnnouncements || []).find(item => item.includes(text))",
                text, new PageWaitForFunctionOptions { Timeout = 10_000 });
            return await handle.JsonValueAsync<string>();
        }
        catch (TimeoutException exception)
        {
            var heard = await page.EvaluateAsync<string[]>("() => window.__assistiveAnnouncements || []");
            throw new Xunit.Sdk.XunitException(
                $"Nothing announced '{text}'. Announced since the last step: "
                + (heard.Length == 0 ? "(nothing)" : string.Join(" | ", heard)), exception);
        }
    }

    /// <summary>Every stop the keyboard made must have a role a screen reader names and a name to say.</summary>
    public void AssertEveryStopIsNamed()
    {
        var unnamed = entries
            .Where(entry => string.IsNullOrWhiteSpace(entry.Name)
                || entry.Role is "" or "generic" or "none" or "RootWebArea"
                || entry.Dialog == "(unnamed dialog)")
            .Select(entry => $"{entry.Step}: role '{entry.Role}', name '{entry.Name}', dialog '{entry.Dialog}'")
            .ToList();
        Assert.True(unnamed.Count == 0,
            "These keyboard stops give a screen reader nothing useful to say:" + Environment.NewLine
            + string.Join(Environment.NewLine, unnamed));
    }

    /// <summary>The transcript as a Markdown table, one row per step.</summary>
    public string ToMarkdown()
    {
        var table = new StringBuilder();
        table.AppendLine("| # | Step | Focus lands on | State | Announced |");
        table.AppendLine("| ---: | :--- | :--- | :--- | :--- |");
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var focus = $"{entry.Role} \"{entry.Name}\"" + (entry.Dialog.Length > 0 ? $" in dialog \"{entry.Dialog}\"" : "");
            table.AppendLine(
                $"| {index + 1} | {Cell(entry.Step)} | {Cell(focus)} | {Cell(entry.Detail)} | {Cell(string.Join(" / ", entry.Announced))} |");
        }
        return table.ToString();
    }

    /// <summary>
    /// Writes the transcript where <c>ETLSQL_ASSISTIVE_TRANSCRIPT_DIR</c> points, when it is set.
    /// A run that is only checking the assertions leaves nothing behind.
    /// </summary>
    public async Task SaveAsync(string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("ETLSQL_ASSISTIVE_TRANSCRIPT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, fileName), ToMarkdown());
    }

    private static string Text(JsonElement node, string property) =>
        node.TryGetProperty(property, out var holder) && holder.TryGetProperty("value", out var value)
            ? (value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString())
            : "";

    private static string Cell(string text) =>
        text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
