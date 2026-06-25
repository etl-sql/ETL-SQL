using System.Threading.Tasks;

namespace ETL_SQL.Core;
public interface IStatementHandler
{
    System.Type SupportedStatementType { get; }
    Task Execute(Statement statement, IExecutionContext context);
}
