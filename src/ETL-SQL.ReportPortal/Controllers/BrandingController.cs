using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/branding")]
[AllowAnonymous]
public class BrandingController(PortalBrandingSettingsService branding) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(branding.ToDto());
}
