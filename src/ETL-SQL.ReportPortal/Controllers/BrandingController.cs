using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ETL_SQL.ReportPortal.Services;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/branding")]
[AllowAnonymous]
public class BrandingController(PortalBrandingSettingsService branding) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(branding.ToDto());
}
