namespace ETL_SQL.Portal.Models;

public record ReportParameterDto(
    string Name,
    string Type,
    string? Default,
    bool Required,
    string? Description);

public record SubscriptionDto(
    int Id,
    int ReportId,
    string ReportName,
    string? Name,
    string? Schedule,
    string? AtTime,
    bool DeliverOnRefresh,
    string Format,
    string SmtpAlias,
    string Recipients,
    DateTime? LastSentAt,
    DateTime? NextRunAt,
    int FailCount,
    bool IsActive,
    Dictionary<string, string>? Parameters,
    string? ParameterSummary,
    long Version = 1);

public record CreateSubscriptionRequest(
    int ReportId,
    string? Name,
    string? Schedule,
    string Format,
    string? SmtpAlias,
    string? RecipientEmail,
    string? AtTime,
    Dictionary<string, string>? Parameters);

public record UpdateSubscriptionRequest(
    string? Name,
    string? Schedule,
    string? AtTime,
    bool? DeliverOnRefresh,
    string? Format,
    string? SmtpAlias,
    string? Recipients,
    bool? IsActive,
    Dictionary<string, string>? Parameters);

public record BulkSubscriptionStatusRequest(IList<VersionedResourceRequest>? Subscriptions, bool IsActive)
{
    public IList<int> SubscriptionIds => (Subscriptions ?? []).Select(x => x.Id).ToList();
}

public record SmtpConnectionDto(
    int Id,
    string Alias,
    string Host,
    int Port,
    string? Username,
    string? FromAddress,
    bool UseSsl,
    long Version = 1);

public record CreateSmtpRequest(
    string Alias,
    string Host,
    int Port,
    string? Username,
    string? Password,
    string? FromAddress,
    bool UseSsl);

public record UpdateSmtpRequest(
    string? Host,
    int? Port,
    string? Username,
    string? Password,
    string? FromAddress,
    bool? UseSsl);

public record CreateRefreshJobRequest(
    string ReportName,
    string Schedule,
    string OrchestratorAlias);
