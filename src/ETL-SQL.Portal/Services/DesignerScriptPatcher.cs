using ETL_SQL.Portal.Models;

namespace ETL_SQL.Portal.Services;

/// <summary>Portal adapter for the shared, host-neutral surgical script patcher.</summary>
public sealed class DesignerScriptPatcher
{
    private readonly ETL_SQL.Reporting.Authoring.DesignerScriptPatcher _inner = new();

    public DesignerScriptPatcher(DesignerScriptGenerationService? generator = null)
    {
        _ = generator;
    }

    public string Patch(string? script, DesignerStateDto state) =>
        _inner.Patch(script, state.ToAuthoringState());
}
