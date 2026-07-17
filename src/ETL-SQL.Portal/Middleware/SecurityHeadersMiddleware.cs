using System.Security.Cryptography;
using System.Text;

namespace ETL_SQL.Portal.Middleware;

public sealed class SecurityHeadersMiddleware(
    RequestDelegate next,
    PortalConfig config,
    ILogger<SecurityHeadersMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var frameAncestors = BuildFrameAncestors(config.Security.FrameAncestors);

        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            $"script-src 'self' 'nonce-{nonce}'; " +
            "script-src-attr 'none'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: blob:; " +
            "font-src 'self' data:; " +
            "connect-src 'self'; " +
            "frame-src 'self' blob:; " +
            $"frame-ancestors {frameAncestors}; " +
            "object-src 'none'; base-uri 'self'; form-action 'self'; " +
            "manifest-src 'self'; media-src 'self' blob:; worker-src 'self' blob:";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

        if (frameAncestors == "'self'")
            context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";

        if (!IsHtmlRequest(context.Request.Path))
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
            buffer.Position = 0;

            if (context.Response.ContentType?.StartsWith(
                    "text/html", StringComparison.OrdinalIgnoreCase) == true)
            {
                using var reader = new StreamReader(
                    buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var html = await reader.ReadToEndAsync(context.RequestAborted);
                html = html
                    .Replace("<script", $"<script nonce=\"{nonce}\"", StringComparison.OrdinalIgnoreCase)
                    .Replace("<style", $"<style nonce=\"{nonce}\"", StringComparison.OrdinalIgnoreCase);

                var bytes = Encoding.UTF8.GetBytes(html);
                context.Response.ContentLength = bytes.Length;
                await originalBody.WriteAsync(bytes, context.RequestAborted);
            }
            else
            {
                buffer.Position = 0;
                await buffer.CopyToAsync(originalBody, context.RequestAborted);
            }
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool IsHtmlRequest(PathString path) =>
        path.Value?.EndsWith(".html", StringComparison.OrdinalIgnoreCase) == true;

    private string BuildFrameAncestors(IEnumerable<string>? configuredOrigins)
    {
        var origins = new List<string> { "'self'" };
        foreach (var configuredOrigin in configuredOrigins ?? [])
        {
            var value = configuredOrigin.Trim().TrimEnd('/');
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                && string.IsNullOrEmpty(uri.PathAndQuery.Trim('/'))
                && string.IsNullOrEmpty(uri.Fragment)
                && string.IsNullOrEmpty(uri.UserInfo))
            {
                origins.Add(uri.GetLeftPart(UriPartial.Authority));
            }
            else
            {
                logger.LogWarning(
                    "Ignoring invalid Portal:Security:FrameAncestors origin '{Origin}'. " +
                    "Only exact HTTP(S) origins are accepted.",
                    configuredOrigin);
            }
        }

        return string.Join(' ', origins.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
