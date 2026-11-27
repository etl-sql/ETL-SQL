using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ETL_SQL.Core.Hosted;

public sealed record LoopbackPkceResult(
    string Code,
    string CodeVerifier,
    string RedirectUri);

/// <summary>
/// RFC 8252 loopback PKCE S256 browser handoff for native .NET clients (TUI and Desktop Studio).
/// Opens an ephemeral 127.0.0.1 callback listener, launches the system browser, validates state,
/// and closes the listener immediately on success, denial, cancellation, or timeout.
/// </summary>
public sealed class LoopbackPkceAuthenticator
{
    private readonly Func<Uri, CancellationToken, Task<bool>> _browserLauncher;

    public LoopbackPkceAuthenticator(Func<Uri, CancellationToken, Task<bool>>? browserLauncher = null)
    {
        _browserLauncher = browserLauncher ?? LaunchSystemBrowserAsync;
    }

    public async Task<LoopbackPkceResult> AuthorizeAsync(
        string portalOrigin,
        string clientId,
        string scope,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        if (!HostedOriginValidator.TryNormalizeTrustedOrigin(portalOrigin, out var origin, out _, out var originError))
            throw new InvalidOperationException(originError ?? "Invalid Portal origin.");

        var codeVerifier = GenerateBase64UrlRandom(32);
        var codeChallenge = ComputeS256Challenge(codeVerifier);
        var state = GenerateBase64UrlRandom(32);
        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(3);

        var port = FindFreeLoopbackPort();
        var redirectUri = $"http://127.0.0.1:{port}/callback";
        var prefix = $"http://127.0.0.1:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        try
        {
            var authorizeUrl = BuildAuthorizeUri(origin, clientId, redirectUri, codeChallenge, state, scope);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(effectiveTimeout);

            var callbackTask = WaitForCallbackAsync(listener, state, timeoutCts.Token);

            var launched = await _browserLauncher(authorizeUrl, timeoutCts.Token);
            if (!launched)
                throw new InvalidOperationException("Could not open the system browser for Portal sign-in.");

            var code = await callbackTask;
            return new LoopbackPkceResult(code, codeVerifier, redirectUri);
        }
        finally
        {
            try { listener.Stop(); } catch { }
            try { listener.Close(); } catch { }
        }
    }

    internal static Uri BuildAuthorizeUri(
        string canonicalOrigin,
        string clientId,
        string redirectUri,
        string codeChallenge,
        string state,
        string scope)
    {
        var query = string.Join("&",
            $"client_id={Uri.EscapeDataString(clientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"code_challenge={Uri.EscapeDataString(codeChallenge)}",
            "code_challenge_method=S256",
            $"state={Uri.EscapeDataString(state)}",
            $"scope={Uri.EscapeDataString(scope)}");
        return new Uri($"{canonicalOrigin}/api/auth/client/authorize?{query}", UriKind.Absolute);
    }

    internal static string ComputeS256Challenge(string codeVerifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    internal static string GenerateBase64UrlRandom(int byteCount) =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static int FindFreeLoopbackPort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    private static async Task<string> WaitForCallbackAsync(
        HttpListener listener,
        string expectedState,
        CancellationToken ct)
    {
        using var reg = ct.Register(() =>
        {
            try { listener.Stop(); } catch { }
        });

        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync();
            }
            catch (Exception ex) when (ct.IsCancellationRequested || ex is HttpListenerException or ObjectDisposedException)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException("Sign-in timed out or was cancelled.", ct);
                throw new InvalidOperationException("Loopback callback listener closed before receiving authorization response.", ex);
            }

            if (!string.Equals(ctx.Request.Url?.AbsolutePath, "/callback", StringComparison.Ordinal))
            {
                await WriteHtmlAsync(ctx.Response, 404, "Not found.");
                continue;
            }

            var query = ParseQuery(ctx.Request.Url?.Query);
            query.TryGetValue("state", out var returnedState);
            query.TryGetValue("code", out var code);
            query.TryGetValue("error", out var error);
            query.TryGetValue("error_description", out var errorDescription);

            if (!FixedTimeEquals(expectedState, returnedState))
            {
                await WriteHtmlAsync(ctx.Response, 400, "Sign-in state mismatch. You may close this window.");
                throw new InvalidOperationException("Portal sign-in callback state validation failed.");
            }

            if (!string.IsNullOrEmpty(error))
            {
                await WriteHtmlAsync(ctx.Response, 200, "Sign-in was denied or cancelled. You may close this window.");
                var detail = string.IsNullOrWhiteSpace(errorDescription) ? error : $"{error}: {errorDescription}";
                throw new InvalidOperationException($"Portal sign-in was not completed ({detail}).");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                await WriteHtmlAsync(ctx.Response, 400, "Missing authorization code. You may close this window.");
                throw new InvalidOperationException("Portal sign-in callback did not include an authorization code.");
            }

            await WriteHtmlAsync(ctx.Response, 200, "Sign-in complete. You may close this window and return to ETL-SQL.");
            return code;
        }

        throw new OperationCanceledException("Sign-in timed out or was cancelled.", ct);
    }

    private static Dictionary<string, string> ParseQuery(string? rawQuery)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(rawQuery))
            return result;

        var trimmed = rawQuery.StartsWith('?') ? rawQuery[1..] : rawQuery;
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
            }
            else
            {
                var key = Uri.UnescapeDataString(pair[..idx]);
                var val = Uri.UnescapeDataString(pair[(idx + 1)..].Replace('+', ' '));
                result[key] = val;
            }
        }

        return result;
    }

    private static bool FixedTimeEquals(string expected, string? actual)
    {
        if (string.IsNullOrEmpty(actual))
            return false;
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(actual);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response, int statusCode, string message)
    {
        var html = $"<!doctype html><html><body style=\"font-family:sans-serif;padding:24px\"><h3>ETL-SQL</h3><p>{WebUtility.HtmlEncode(message)}</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.StatusCode = statusCode;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static Task<bool> LaunchSystemBrowserAsync(Uri uri, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
