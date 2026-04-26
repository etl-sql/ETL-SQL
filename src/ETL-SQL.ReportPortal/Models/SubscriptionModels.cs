namespace ETL_SQL.ReportPortal.Models;

public record SubscriptionDto(
    int       Id,
    int       ReportId,
    string    ReportName,
    string?   Schedule,
    bool      DeliverOnRefresh,
    string    Format,
    string    SmtpAlias,
    string    Recipients,
    DateTime? LastSentAt,
    DateTime? NextRunAt,
    int       FailCount,
    bool      IsActive);

public record CreateSubscriptionRequest(
    int     ReportId,
    string? Schedule,
    string  Format,
    string? SmtpAlias,
    string? RecipientEmail,
    string? AtTime);

public record UpdateSubscriptionRequest(
    string? Schedule,
    bool?   DeliverOnRefresh,
    string? Format,
    string? SmtpAlias,
    string? Recipients,
    bool?   IsActive);

public record SmtpConnectionDto(
    int    Id,
    string Alias,
    string Host,
    int    Port,
    string? Username,
    string? FromAddress,
    bool   UseSsl);

public record CreateSmtpRequest(
    string  Alias,
    string  Host,
    int     Port,
    string? Username,
    string? Password,
    string? FromAddress,
    bool    UseSsl);

public record UpdateSmtpRequest(
    string? Host,
    int?    Port,
    string? Username,
    string? Password,
    string? FromAddress,
    bool?   UseSsl);
