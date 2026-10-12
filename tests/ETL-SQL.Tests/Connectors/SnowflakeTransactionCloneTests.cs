using System.Data;
using System.Data.Common;
using System.Reflection;
using ETL_SQL.Connectors.Snowflake;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("CompatBreak", "0.21.0")]
public sealed class SnowflakeTransactionCloneTests
{
    [Fact]
    public async Task TableViewsFollowLaterTransactionChangesAndDoNotDisposeTheOwner()
    {
        await using var source = new SnowflakeDataSource(SystemExecutionContext.Instance, "account=test", null, null);
        await using var clone = source.WithTable("target");
        var state = typeof(SnowflakeDataSource).GetField("_transactionState", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(source)!;
        var field = state.GetType().GetField("Transaction")!;
        var first = new RecordingTransaction();
        field.SetValue(state, first);
        await source.WithTable("another").DisposeAsync();
        Assert.Equal(0, first.Rollbacks);
        await ((ITransactionalDataSource)clone).RollbackAsync();
        Assert.Equal(1, first.Rollbacks);
        Assert.Null(field.GetValue(state));
        var second = new RecordingTransaction();
        field.SetValue(state, second);
        await ((ITransactionalDataSource)clone).CommitAsync();
        Assert.Equal(1, second.Commits);
        await source.RollbackAsync();
        Assert.Equal(0, second.Rollbacks);
    }

    private sealed class RecordingTransaction : DbTransaction
    {
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection? DbConnection => null;
        public override void Commit() => Commits++;
        public override void Rollback() => Rollbacks++;
    }
}
