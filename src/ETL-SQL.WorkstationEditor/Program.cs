using ETL_SQL.WorkstationEditor;

var options = WorkstationEditorOptions.Parse(args, Directory.GetCurrentDirectory());
var app = WorkstationEditorApp.Create(args, options);

await app.StartAsync();

var url = WorkstationEditorApp.GetListeningUrl(app);
Console.WriteLine("ETL-SQL Local Script Editor");
Console.WriteLine($"Workspace: {options.WorkspaceRoot}");
Console.WriteLine($"URL: {url}/?token={Uri.EscapeDataString(options.SessionToken)}");
Console.WriteLine("Press Ctrl+C to stop.");

await app.WaitForShutdownAsync();
