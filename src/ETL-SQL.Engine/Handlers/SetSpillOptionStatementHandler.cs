using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Engine.Services;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the SET SPILL_ENCRYPTION/COMPRESSION ON/OFF statements.
/// Works in conjunction with the secure SpillStore.
/// </summary>
public class SetSpillOptionStatementHandler(ILogger logger, IConfiguration? config = null) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    private readonly IConfiguration? _config = config;
    public Type SupportedStatementType => typeof(SetSpillOptionStatement);

    /// <summary>Executes the SET SPILL_... statement, updating the context and notifying the user.</summary>
    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (SetSpillOptionStatement)statement;

        if (stmt.Option == SpillOptionType.Encryption)
        {
            if (!stmt.Enabled)
                EnforceSpillEncryptionPolicy(context);

            context.SpillEncryptionEnabled = stmt.Enabled;
            _logger.Info("Disk spill encryption is now {Status}.", stmt.Enabled ? "ENABLED" : "DISABLED");

            if (!stmt.Enabled && context.SpillCompressionEnabled)
            {
                context.SpillCompressionEnabled = false;
                _logger.Info("Disk spill compression was automatically DISABLED because encryption is required for compressed storage.");
            }
        }
        else if (stmt.Option == SpillOptionType.Compression)
        {
            context.SpillCompressionEnabled = stmt.Enabled;
            _logger.Info("Disk spill compression is now {Status}.", stmt.Enabled ? "ENABLED" : "DISABLED");

            if (stmt.Enabled && !context.SpillEncryptionEnabled)
            {
                context.SpillEncryptionEnabled = true;
                _logger.Info("Disk spill encryption was automatically ENABLED because it is required for compressed storage.");
            }
        }

        return Task.CompletedTask;
    }

    private void EnforceSpillEncryptionPolicy(IExecutionContext context)
    {
        var snapshot = OperationPolicyBoundary.Refresh(context, "SET SPILL_ENCRYPTION OFF");
        if (snapshot.IsEnrolled
            && snapshot.GovernedValues.TryGetValue("Security:RequireSpillEncryption", out var governed)
            && bool.TryParse(governed, out var required)
            && required)
        {
            throw new OperationPolicyDeniedException(OperationPolicyDecision.Deny(
                snapshot,
                "Security:RequireSpillEncryption",
                "SET SPILL_ENCRYPTION OFF",
                "true",
                "Enterprise policy Security:RequireSpillEncryption prohibits disabling spill encryption."));
        }

        if (DefaultThresholds.RequireSpillEncryption(_config))
        {
            throw new ExecutionException(
                "SET SPILL_ENCRYPTION OFF is prohibited by deployment security policy (Security:RequireSpillEncryption).");
        }
    }
}
