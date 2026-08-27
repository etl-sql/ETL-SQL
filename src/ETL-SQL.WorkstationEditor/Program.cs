using System.Diagnostics;
using ETL_SQL.WorkstationEditor;
using Microsoft.AspNetCore.Builder;

WorkstationEditorOptions options;
try
{
    options = WorkstationEditorOptions.Parse(args, Directory.GetCurrentDirectory());
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

WebApplication app;
try
{
    app = WorkstationEditorApp.Create(args, options);
    await app.StartAsync();
}
catch (IOException ex)
{
    // Most commonly the requested port is already in use.
    Console.Error.WriteLine($"Could not start the script editor: {ex.Message}");
    return 1;
}

var pathPrefix = options.StudioMode ? "/studio" : "";
var url = $"{WorkstationEditorApp.GetListeningUrl(app)}{pathPrefix}/?token={Uri.EscapeDataString(options.SessionToken)}";
Console.WriteLine(options.StudioMode ? "ETL-SQL Studio (Local Workbench)" : "ETL-SQL Local Script Editor");
Console.WriteLine($"Workspace: {options.WorkspaceRoot}");
if (options.ReadOnly) Console.WriteLine("Mode:      read-only");
Console.WriteLine($"URL:       {url}");
Console.WriteLine("Press Ctrl+C to stop.");

if (options.OpenBrowser) TryOpenBrowser(url);

await app.WaitForShutdownAsync();
return 0;

static void TryOpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        // Headless machines and locked-down shells have no browser handler; the URL is
        // already printed, so this is not worth failing the command over.
        Console.Error.WriteLine($"Could not open a browser automatically ({ex.Message}). Open the URL above.");
    }
}
