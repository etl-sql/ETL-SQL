using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/branding")]
[AllowAnonymous]
public class BrandingController(PortalBrandingSettingsService branding) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(branding.ToDto());
}
