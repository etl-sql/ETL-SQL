using ETL_SQL.Analysis.Services;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Interfaces;

namespace ETL_SQL.WorkstationEditor;

/// <summary>
/// Desktop HTTP adapter over the shared <see cref="LanguageHoverService"/>. The lookup itself is
/// host-neutral so the Portal serves identical hover text from the same corpus.
/// </summary>
public sealed class WorkstationHelpService(
    ILanguageHelpRegistry languageHelp,
    IFunctionRegistry functionRegistry)
{
    private readonly LanguageHoverService _hover = new(languageHelp, functionRegistry);

    public HoverResponse GetHover(HoverRequest request)
    {
        var (markdown, kind) = _hover.Lookup(request.Word);
        return markdown is null ? new HoverResponse(null) : new HoverResponse(markdown, kind);
    }
}

public sealed record HoverRequest(string? Word, string? Script = null, int Line = 0, int Column = 0, string? DocumentUri = null);

public sealed record HoverResponse(string? Markdown, string? Kind = null);
