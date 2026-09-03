namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// A throwaway directory that stands in for a project folder on disk, for journeys driven against
/// the desktop host.
///
/// <para>Shared rather than nested in one test class because more than one desktop journey needs
/// one, and two copies would drift — in particular over the cleanup rule below, which is the part
/// that is easy to get wrong.</para>
/// </summary>
internal sealed class StudioTempWorkspace : IDisposable
{
    public StudioTempWorkspace()
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
