namespace ETL_SQL.Portal.Services;

/// <summary>
/// Share links and embed tokens are bearer credentials for a report, so the catalog keeps only a
/// SHA-256 of each. The token itself is shown once, in the response that creates it.
/// </summary>
public static class AnonymousReportTokens
{
    public const string HashPrefix = "sha256:";

    public static string Hash(string token) => HashPrefix + TokenService.HashRefreshToken(token);
}
