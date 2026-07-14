using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/docs")]
public sealed class DocumentationController(
    DocumentationLibraryService docs,
    PortalModuleRegistry modules) : ControllerBase
{
    [HttpGet("index")]
    public IActionResult Index()
    {
        if (!modules.IsEnabled("Documentation"))
            return NotFound(new { error = "documentation_disabled" });

        return Ok(docs.GetIndex());
    }

    [HttpGet("search")]
    public IActionResult Search([FromQuery] string? q, [FromQuery] int limit = 25)
    {
        if (!modules.IsEnabled("Documentation"))
            return NotFound(new { error = "documentation_disabled" });

        return Ok(docs.Search(q, limit));
    }

    [HttpGet("document")]
    public async Task<IActionResult> Document([FromQuery] string path, CancellationToken ct)
    {
        if (!modules.IsEnabled("Documentation"))
            return NotFound(new { error = "documentation_disabled" });

        var document = await docs.GetDocumentAsync(path, ct);
        return document is null ? NotFound(new { error = "document_not_found" }) : Ok(document);
    }
}
