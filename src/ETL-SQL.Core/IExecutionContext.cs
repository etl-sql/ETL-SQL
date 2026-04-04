using ETL_SQL.Data;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Core
{
    public interface IVariableContext
    {
        IDictionary<string, object?> Variables { get; }
        IDictionary<string, object?> CurrentVariables { get; }
        IDictionary<string, VariableMetadata> VariableMetadata { get; }
        IDictionary<string, VariableMetadata> CurrentMetadata { get; }
        void SetVariable(string name, object? value);
        object? GetVariable(string name);
        void PushScope(Dictionary<string, object?> vars, Dictionary<string, VariableMetadata>? metadata = null);
        void PopScope();
        bool ContainsVariable(string name);
        /// <summary>Checks if a variable was declared in the current local scope only.</summary>
        bool ContainsVariableInCurrentScope(string name);
        void DeclareVariable(string name, object? value, VariableMetadata? metadata = null);
        Dictionary<string, object?> GetVariablesWithMetadata(Func<VariableMetadata, bool> predicate);
    }

    public interface IQueryContext
    {
        IAsyncEnumerable<DataTable> ExecuteQuery(Statement query);
        Task<IDataSource> ResolveDataSourceAsync(TableReference table);
        IAsyncEnumerable<DataTable> ResolveAndApplyOperators(TableReference table);
        IAsyncEnumerable<DataTable> EvaluateForClause(IAsyncEnumerable<DataTable> batches, ForClause forClause);
        IAsyncEnumerable<DataTable> InterceptProgress(IAsyncEnumerable<DataTable> chunks);
        IAsyncEnumerable<DataTable> AlignColumns(IAsyncEnumerable<DataTable> batches, List<string> targetCols);
        ForClause? GetForClause(Statement stmt);
        TableReference? GetIntoTable(Statement stmt);
    }

    public interface ILineageContext
    {
        ILineageTracker LineageTracker { get; }
    }

    public interface ISqlCompilerContext
    {
        string CompileExpression(Expression e, string dialect = "MSSQL");
        string CompileQuery(Statement s, string dialect = "MSSQL");
        string GetSqlTableName(TableReference t);
    }

    public interface ITransactionContext
    {
        Task BeginTransaction();
        Task CommitTransaction();
        Task RollbackTransaction(string? name = null);
        int TranCount { get; }
    }

    public interface IDockerContext
    {
        IDockerManager DockerManager { get; }
    }

    public interface ILoggingContext
    {
        bool IsVerbose { get; set; }
        bool RedirectOutput { get; set; }
        List<string> Messages { get; }
        int MaxMessages { get; set; }
        void Log(string message, ConsoleColor color = ConsoleColor.White);
    }

    public interface IEvaluationContext
    {
        Task<object?> EvaluateValue(Expression? expr, Row context);
        Task<bool> EvaluateCondition(Expression? expr, Row context);
        Task<object?> EvaluateUserDefinedFunction(FunctionCallExpression f, List<object?> args, Row context);
        object? ResolveIdentifier(string name, Row? row);
        int CompareConstants(object? a, object? b);
        object? CastToType(object? value, string dataType);
    }

    public interface IDataContext
    {
        IDictionary<string, IDataSource> Connections { get; }
        string? MasterPassword { get; }
        DataTable? LastResult { get; set; }
        List<DataTable> LastResultSets { get; }
        long RowsProcessed { get; set; }
        long TotalSpilledBytes { get; set; }
        int PartitionsCount { get; set; }
        Action<DataTable>? OnResultSet { get; set; }
        bool IsSqlPushdown(string connName);
    }

    public interface IEngineContext
    {
        Functions.IFunctionRegistry FunctionRegistry { get; }
        Task EvaluateStatement(Statement statement);
        Task Evaluate(Script script);
        Task EvaluateProcedure(string name, List<object?> args);
        string ResolvePath(string path);
        int MaxRecursiveDepth { get; set; }
        int BatchSize { get; set; }
        int? PreviewLimit { get; set; }
        bool FunctionExists(string name);
        bool ProcedureExists(string name);
    }

    /// <summary>
    /// The primary interface for script execution state, providing access to variables, connections, 
    /// expression evaluation, and system services (Docker, Lineage, Transactions).
    /// </summary>
    public interface IExecutionContext : IVariableContext, IQueryContext, ISqlCompilerContext, 
                                        ITransactionContext, ILineageContext, IDockerContext,
                                        ILoggingContext, IEvaluationContext, IDataContext, IEngineContext
    {
        Stack<Row> OuterRowStack { get; }
        Dictionary<Statement, object?> SubqueryCache { get; }
        
        bool IsProfiling { get; set; }
        List<ExecutionMetrics> ProfileMetrics { get; }

        List<string> GetIndexedColumns(Expression? cond, string alias);

        Task EvaluateCreateTable(CreateTableStatement stmt);
        Task EvaluateCreateIndex(CreateIndexStatement stmt);
        void EvaluateCreateFunction(CreateFunctionStatement stmt);
        void EvaluateCreateProcedure(CreateProcedureStatement stmt);
        Task EvaluateDropConnection(DropConnectionStatement stmt);
        void EvaluateDropFunction(DropFunctionStatement stmt);
        Task EvaluateDropIndex(DropIndexStatement stmt);
        void EvaluateDropProcedure(DropProcedureStatement stmt);
        Task EvaluateDropTable(DropTableStatement stmt);

        /// <summary>Creates a thread-safe shallow clone of the context for parallel execution branches.</summary>
        IExecutionContext Fork();
        /// <summary>Merges results and metrics from a spawned context back into the parent.</summary>
        void Merge(IExecutionContext spawned);
    }

    public interface ILineageTracker
    {
        void Record(string target, IEnumerable<string> sources, string operation, string? targetColumn = null, IEnumerable<string>? sourceColumns = null, Dictionary<string, string>? metadata = null, string? derivedFromDescriptions = null, int line = 0, int column = 0, int endLine = 0, int endColumn = 0, string? sourceFile = null);
        IEnumerable<LineageEntry> GetLineage(string tableName);
        IEnumerable<LineageEntry> GetColumnLineage(string tableName, string columnName);
        Dictionary<string, string> GetColumnMetadata(string tableName, string columnName);
        IEnumerable<LineageEntry> GetAncestors(string tableName, string? columnName = null);
        Dictionary<string, string> InheritMetadata(IEnumerable<string> sourceTables, IEnumerable<string> sourceColumns, out string? derivedFromDescriptions);
        IEnumerable<LineageEntry> GetFullLineage();
        void Clear();
    }

    public interface IDockerManager : IAsyncDisposable
    {
        string? LastConnectionString { get; }
        Task<string> StartContainer(string imageName, string? alias = null);
        Task StopContainer(string alias);
        Task PauseContainer(string alias);
        Task ResumeContainer(string alias);
        Task CloseContainers(string? nameOrAlias = null);
        string? GetConnectionString(string alias);
    }
}
