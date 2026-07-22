using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Security;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Reporting;

namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationPreviewService(IServiceProvider services, ETL_SQL.Common.ILogger logger)
{
    private const int TimeoutSeconds = 30;
    private const int OperatorGrantMb = 128;
    private const long SessionCeilingBytes = 256L * 1024 * 1024;

    public async Task<ReportManifest> BuildPreviewAsync(string scriptText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scriptText))
            throw new ArgumentException("Nothing to preview — the script is empty.");

        var script =
            $"SET OPERATOR_MEMORY_GRANT = {OperatorGrantMb};\n" +
            $"SET MAX_SESSION_SIZE = {SessionCeilingBytes};\n" +
            scriptText;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        var sessionContext = new CliContext
        {
            Command = "build",
            IsSilentMode = true,
            SessionId = Guid.NewGuid().ToString("N")
        };

        var session = new ExecutionSession(services, sessionContext, logger);
        var result = await session.ExecuteAsync(script, timeout.Token, "workstation-preview");

        if (!result.Success)
        {
            var message = result.Diagnostics.Count > 0
                ? string.Join("; ", result.Diagnostics.Select(d => d.Message))
                : "Preview build failed.";
            throw new InvalidOperationException(SecretRedactor.Redact(message));
        }

        var evaluator = session.LastEvaluator
            ?? throw new InvalidOperationException("Preview produced no report context.");

        return await new ManifestBuilder(evaluator).BuildAsync(scriptText);
    }
}
