using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Scheduling;

/// <summary>
/// Central notification delivery path for scheduled jobs and Portal-evaluated alerts.
/// The Orchestrator resolves the notification's shared connection alias locally, so credentials
/// and SECRET: references stay on the Orchestrator host instead of crossing API boundaries.
/// </summary>
public sealed class NotificationDispatchService(
    IServiceProvider serviceProvider,
    IJobCatalogStore catalog,
    ILogger<NotificationDispatchService> logger)
{
    public async Task DispatchJobNotificationsAsync(
        JobDefinition job,
        string finalStatus,
        long historyId,
        ScriptExecutionResult? result,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<JobNotificationLink> links;
        try
        {
            links = await catalog.GetJobNotificationsAsync(job.Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Job {JobName}: failed to read notification links.", job.Name);
            return;
        }

        if (links.Count == 0) return;

        var trigger = finalStatus.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)
            ? NotificationTrigger.Success
            : NotificationTrigger.Failure;
        var dueLinks = links
            .Where(link => link.Trigger == trigger || link.Trigger == NotificationTrigger.Completion)
            .ToList();
        if (dueLinks.Count == 0) return;

        foreach (var link in dueLinks)
        {
            var title = finalStatus.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)
                ? $"Job succeeded: {job.Name}"
                : $"Job failed: {job.Name}";
            
            string text;
            if (finalStatus.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                text = $"Job '{job.Name}' completed successfully.";
            }
            else
            {
                var errorMsg = SecretRedactor.Redact(result?.ErrorMessage);
                text = $"Job '{job.Name}' failed. {errorMsg}".Trim();
                if (!string.IsNullOrEmpty(result?.SessionId))
                {
                    text += $" Resume ID: {result.SessionId}";
                }
            }

            await DispatchNotificationAsync(
                new NotificationDispatchPayload(
                    NotificationName: link.NotificationName,
                    SourceKind: "JOB",
                    Title: title,
                    Text: text,
                    Trigger: link.Trigger.ToString().ToUpperInvariant(),
                    Status: finalStatus,
                    JobName: job.Name,
                    HistoryId: historyId,
                    RowsProcessed: result?.RowsProcessed ?? 0,
                    ErrorMessage: SecretRedactor.Redact(result?.ErrorMessage)),
                cancellationToken);
        }
    }

    public async Task<NotificationDispatchResult> DispatchNotificationAsync(
        NotificationDispatchPayload payload,
        CancellationToken cancellationToken = default)
    {
        NotificationDefinition? notification;
        try
        {
            notification = await catalog.GetNotificationAsync(payload.NotificationName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Notification '{Notification}': failed to load definition.",
                payload.NotificationName);
            return NotificationDispatchResult.Failed(payload.NotificationName, "Failed to load notification.");
        }

        if (notification is null)
        {
            logger.LogWarning(
                "Notification '{Notification}' does not exist; skipping.",
                payload.NotificationName);
            return NotificationDispatchResult.Skip(payload.NotificationName, "Notification does not exist.");
        }

        var explicitSubscriptionDispatch = payload.SourceKind.Equals(
            "SUBSCRIPTION",
            StringComparison.OrdinalIgnoreCase);
        if (!notification.IsEnabled && !explicitSubscriptionDispatch)
        {
            logger.LogInformation(
                "Notification '{Notification}' is disabled; skipping.",
                notification.Name);
            return NotificationDispatchResult.Skip(notification.Name, "Notification is disabled.");
        }

        using var scope = serviceProvider.CreateScope();
        var connectionCatalog = scope.ServiceProvider.GetService<IConnectionCatalogProvider>();
        if (connectionCatalog is null)
        {
            logger.LogWarning(
                "Notification '{Notification}' is due, but no connection catalog provider is configured.",
                notification.Name);
            return NotificationDispatchResult.Skip(notification.Name, "No connection catalog provider is configured.");
        }

        SharedConnectionDefinition connection;
        try
        {
            connection = await connectionCatalog.ResolveAsync(
                notification.ConnectionName,
                identity: null,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(
                "Notification '{Notification}' references unusable connection '{Connection}': {Message}",
                notification.Name, notification.ConnectionName, SecretRedactor.Redact(ex.Message));
            return NotificationDispatchResult.Failed(notification.Name, "Notification connection is unavailable.");
        }

        var executor = scope.ServiceProvider.GetRequiredService<IScriptExecutor>();
        var script = BuildNotificationScript(payload, notification, connection);
        try
        {
            var delivery = await executor.ExecuteTextAsync(
                script,
                sessionId: null,
                cancellationToken: cancellationToken,
                jobName: $"{payload.SourceKind.ToLowerInvariant()}:{payload.JobName ?? payload.AlertName ?? "notification"}:{notification.Name}");
            if (delivery.Success)
            {
                logger.LogInformation(
                    "{SourceKind} notification '{Notification}' delivered.",
                    payload.SourceKind, notification.Name);
                return NotificationDispatchResult.Deliver(notification.Name);
            }

            logger.LogWarning(
                "{SourceKind} notification '{Notification}' delivery failed: {Message}",
                payload.SourceKind, notification.Name, SecretRedactor.Redact(delivery.ErrorMessage));
            return NotificationDispatchResult.Failed(notification.Name, SecretRedactor.Redact(delivery.ErrorMessage) ?? "Delivery failed.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "{SourceKind} notification '{Notification}' delivery failed.",
                payload.SourceKind, notification.Name);
            return NotificationDispatchResult.Failed(notification.Name, "Delivery failed.");
        }
    }

    private static string BuildNotificationScript(
        NotificationDispatchPayload payload,
        NotificationDefinition notification,
        SharedConnectionDefinition connection)
    {
        return connection.ConnectorType.Equals("SMTP", StringComparison.OrdinalIgnoreCase)
            ? BuildSmtpNotificationScript(payload, notification)
            : BuildEventNotificationScript(payload, notification, connection);
    }

    private static string BuildEventNotificationScript(
        NotificationDispatchPayload payload,
        NotificationDefinition notification,
        SharedConnectionDefinition connection)
    {
        const string alias = "__job_notification_sink";
        var recipient = payload.RecipientOverride ?? notification.Recipient ?? string.Empty;
        var attachmentColumns = payload.AttachmentPaths.Count > 0 ? ", Attachments" : string.Empty;
        var attachmentValues = payload.AttachmentPaths.Count > 0
            ? $",{Environment.NewLine}                '{SqlString(string.Join(";", payload.AttachmentPaths))}'"
            : string.Empty;
        return $"""
            CREATE CONNECTION {alias} AS {connection.ConnectorType}('SHARED:{SqlString(notification.ConnectionName)}');
            INSERT INTO {alias} (
                Title, Text, SourceKind, JobName, AlertName, ReportId, NotificationName, Trigger, Status, HistoryId, RowsProcessed, Recipient, ErrorMessage, Actor{attachmentColumns}
            )
            VALUES (
                '{SqlString(payload.Title)}',
                '{SqlString(payload.Text)}',
                '{SqlString(payload.SourceKind)}',
                '{SqlString(payload.JobName)}',
                '{SqlString(payload.AlertName)}',
                '{SqlString(payload.ReportId)}',
                '{SqlString(notification.Name)}',
                '{SqlString(payload.Trigger)}',
                '{SqlString(payload.Status)}',
                {payload.HistoryId ?? 0},
                {payload.RowsProcessed},
                '{SqlString(recipient)}',
                '{SqlString(SecretRedactor.Redact(payload.ErrorMessage))}',
                '{SqlString(payload.Actor)}'{attachmentValues}
            );
            """;
    }

    private static string BuildSmtpNotificationScript(
        NotificationDispatchPayload payload,
        NotificationDefinition notification)
    {
        const string alias = "__job_notification_sink";
        var recipient = payload.RecipientOverride ?? notification.Recipient ?? string.Empty;
        var attachmentColumns = payload.AttachmentPaths.Count > 0 ? ", Attachments" : string.Empty;
        var attachmentValues = payload.AttachmentPaths.Count > 0
            ? $",{Environment.NewLine}                '{SqlString(string.Join(";", payload.AttachmentPaths))}'"
            : string.Empty;
        return $"""
            CREATE CONNECTION {alias} AS SMTP('SHARED:{SqlString(notification.ConnectionName)}');
            INSERT INTO {alias} (
                To, Subject, Body{attachmentColumns}
            )
            VALUES (
                '{SqlString(recipient)}',
                '{SqlString(payload.Title)}',
                '{SqlString(payload.Text)}'{attachmentValues}
            );
            """;
    }

    private static string SqlString(string? value) => (value ?? string.Empty).Replace("'", "''");
}

public sealed record NotificationDispatchPayload
{
    public NotificationDispatchPayload(
        string NotificationName,
        string SourceKind,
        string Title,
        string Text,
        string? Trigger = null,
        string? Status = null,
        string? JobName = null,
        string? AlertName = null,
        string? ReportId = null,
        long? HistoryId = null,
        long RowsProcessed = 0,
        string? RecipientOverride = null,
        string? ErrorMessage = null,
        string? Actor = null,
        IReadOnlyList<string>? AttachmentPaths = null)
    {
        this.NotificationName = NotificationName;
        this.SourceKind = SourceKind;
        this.Title = Title;
        this.Text = Text;
        this.Trigger = Trigger;
        this.Status = Status;
        this.JobName = JobName;
        this.AlertName = AlertName;
        this.ReportId = ReportId;
        this.HistoryId = HistoryId;
        this.RowsProcessed = RowsProcessed;
        this.RecipientOverride = RecipientOverride;
        this.ErrorMessage = ErrorMessage;
        this.Actor = Actor;
        this.AttachmentPaths = ValidateAttachmentPaths(AttachmentPaths);
    }

    public string NotificationName { get; init; }
    public string SourceKind { get; init; }
    public string Title { get; init; }
    public string Text { get; init; }
    public string? Trigger { get; init; }
    public string? Status { get; init; }
    public string? JobName { get; init; }
    public string? AlertName { get; init; }
    public string? ReportId { get; init; }
    public long? HistoryId { get; init; }
    public long RowsProcessed { get; init; }
    public string? RecipientOverride { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Actor { get; init; }
    public IReadOnlyList<string> AttachmentPaths { get; init; }

    private static IReadOnlyList<string> ValidateAttachmentPaths(IReadOnlyList<string>? paths)
    {
        if (paths is null || paths.Count == 0)
            return [];

        var normalized = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Notification attachment paths cannot be blank.", nameof(paths));

            if (!Path.IsPathFullyQualified(path))
                throw new ArgumentException(
                    "Notification attachment paths must be absolute paths.", nameof(paths));
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException(
                    $"Notification attachment does not exist: {fullPath}", fullPath);
            normalized.Add(fullPath.Replace("\\", "/"));
        }

        return normalized;
    }
}

public sealed record NotificationDispatchResult(
    string NotificationName,
    bool Delivered,
    bool Skipped,
    string? Message)
{
    public static NotificationDispatchResult Deliver(string notificationName) =>
        new(notificationName, Delivered: true, Skipped: false, Message: null);

    public static NotificationDispatchResult Skip(string notificationName, string message) =>
        new(notificationName, Delivered: false, Skipped: true, Message: message);

    public static NotificationDispatchResult Failed(string notificationName, string message) =>
        new(notificationName, Delivered: false, Skipped: false, Message: message);
}
