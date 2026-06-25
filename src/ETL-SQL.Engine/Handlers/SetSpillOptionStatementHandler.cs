using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the SET SPILL_ENCRYPTION/COMPRESSION ON/OFF statements.
/// Works in conjunction with the secure SpillStore.
/// </summary>
public class SetSpillOptionStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    public Type SupportedStatementType => typeof(SetSpillOptionStatement);

    /// <summary>Executes the SET SPILL_... statement, updating the context and notifying the user.</summary>
    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (SetSpillOptionStatement)statement;

        if (stmt.Option == SpillOptionType.Encryption)
        {
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
}
