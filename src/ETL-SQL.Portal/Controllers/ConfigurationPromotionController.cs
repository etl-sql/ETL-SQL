using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/admin/configuration")]
[Authorize(Roles = "Admin")]
public sealed class ConfigurationPromotionController(ConfigurationPromotionValidationService validator) : ControllerBase
{
    public sealed record ValidateRequest(string Script, Dictionary<string, string>? Bindings = null);

    [HttpPost("validate")]
    public async Task<ActionResult<ConfigurationPromotionValidationService.Result>> Validate(
        [FromBody] ValidateRequest request, CancellationToken ct) =>
        Ok(await validator.ValidateAsync(request.Script, request.Bindings, ct));
}
