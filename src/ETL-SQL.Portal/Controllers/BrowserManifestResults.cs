using ETL_SQL.Reporting;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// Portal-side entry to the one browser-delivery boundary. Returning <c>Ok(manifest)</c> would let
/// MVC serialize the server's working object, semantic contracts and all; every endpoint that hands
/// a manifest to a browser goes through here instead.
/// </summary>
public static class BrowserManifestResults
{
    /// <summary>Serializes a manifest through the browser-delivery projection.</summary>
    public static ContentResult BrowserManifest(this ControllerBase controller, ReportManifest manifest) =>
        controller.Content(BrowserDeliveryProjection.Serialize(manifest), "application/json");

    /// <summary>Re-projects a stored manifest payload before it reaches a browser.</summary>
    public static ContentResult StoredBrowserManifest(this ControllerBase controller, string manifestJson) =>
        controller.Content(BrowserDeliveryProjection.ProjectStoredJson(manifestJson), "application/json");
}
