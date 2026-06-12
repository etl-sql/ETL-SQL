using ETL_SQL.ReportPortal.Data;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.ReportPortal.Services;

public static class OptimisticConcurrency
{
    public static long? ReadExpectedVersion(HttpRequest request)
    {
        var value = request.Headers.IfMatch.ToString().Trim();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            value = value[2..].Trim();

        value = value.Trim('"');
        return long.TryParse(value, out var version) && version > 0 ? version : null;
    }

    public static string ToETag(long version) => $"\"{version}\"";

    public static void SetETag(HttpResponse response, long version) =>
        response.Headers.ETag = ToETag(version);

    public static IActionResult MissingVersion(ControllerBase controller) =>
        controller.StatusCode(StatusCodes.Status428PreconditionRequired, new
        {
            error = "This mutation requires the current resource version in the If-Match header."
        });

    public static IActionResult Conflict(ControllerBase controller, object current) =>
        controller.Conflict(new
        {
            error = "The resource changed after it was read. Refresh it and retry.",
            current
        });

    public static bool Prepare(PortalDbContext db, IVersionedEntity entity, long expectedVersion)
    {
        if (entity.Version != expectedVersion)
            return false;

        // COMPAT_BREAK: 0.12
        db.Entry(entity).Property(x => x.Version).OriginalValue = expectedVersion;
        entity.Version = checked(expectedVersion + 1);
        return true;
    }
}
