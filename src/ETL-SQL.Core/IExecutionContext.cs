using ETL_SQL.Data;
using ETL_SQL.Common;
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
        string GetSqlTableName(TableReference t, string dialect = "MSSQL");
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
        ETL_SQL.Common.ILogger Logger { get; }
        bool IsVerbose { get; set; }
        bool ShowPassword { get; set; }
        bool RedirectOutput { get; set; }
        List<string> Messages { get; }
        int MaxMessages { get; set; }
        void Log(string message, ConsoleColor color = ConsoleColor.White);
        /// <summary>
        /// Optional interactive prompt callback. Returns true to proceed, false to abort.
        /// Null means non-interactive (auto-proceed).
        /// </summary>
        Func<string, Task<bool>>? OnPrompt { get; set; }
    }

    public interface IEvaluationContext
    {
        Task<object?> EvaluateValue(Expression? expr, Row context);
        IAsyncEnumerable<Row> EvaluateStream(Expression? expr, Row context);
        Task<bool> EvaluateCondition(Expression? expr, Row context);
        Task<object?> EvaluateUserDefinedFunction(FunctionCallExpression f, List<object?> args, Row context);
        object? ResolveIdentifier(string name, Row? row);
        int CompareConstants(object? a, object? b);
        bool IsSoftEqual(object? a, object? b);
        object? CastToType(object? value, string dataType);
    }

    public interface IDataContext
    {
        IDictionary<string, IDataSource> Connections { get; }
        /// <summary>Statement-local data source overrides (used for CTEs).</summary>
        IDictionary<string, IDataSource> LocalSources { get; }
        string? MasterPassword { get; }
        string? ScriptPassword { get; set; }
        DataTable? LastResult { get; set; }
        List<DataTable> LastResultSets { get; }
        long RowsProcessed { get; set; }
        long TotalSpilledBytes { get; set; }
        int PartitionsCount { get; set; }
        long AggregateGroupsCount { get; set; }
        double AggregateExpansionRatio { get; set; }
        long LastExecutionTimeMs { get; set; }
        long SubqueryCacheHits { get; set; }
        long SubqueryCacheMisses { get; set; }
        int SortSpillCount { get; set; }
        Action<DataTable>? OnResultSet { get; set; }
        bool IsSqlPushdown(string connName);
        /// <summary>Named environment sets created by CREATE SETS.</summary>
        IDictionary<string, NamedSet> NamedSets { get; }
        
        // Security override flags (granted via ### flags in script)
        bool AllowUnknownFileTypes { get; set; }
        bool AllowLargeFileOperationCount { get; set; }
        bool AllowDeepRecursion { get; set; }
        
        /// <summary>Metadata about the last caught exception in this session.</summary>
        ErrorInfo? LastError { get; set; }
        /// <summary>The error number of the most recently COMPLETED statement (for @@ERROR).</summary>
        int PreviousErrorNumber { get; set; }

        /// <summary>Number of rows before in-memory joins spill to disk (CFG-6).</summary>
        int JoinSpillThreshold { get; set; }
        /// <summary>Number of partitions used for external disk-spilling operations (CFG-5).</summary>
        int ExternalHashPartitions { get; set; }
        /// <summary>Number of rows per sort chunk before spilling to disk (CFG-4).</summary>
        int ExternalSortChunkSize { get; set; }
        /// <summary>Number of rows before window functions spill to disk (CFG-7).</summary>
        int WindowSpillThreshold { get; set; }
        /// <summary>Maximum number of batches held in RAM for #temp tables before spilling.</summary>
        int MaxInMemoryBatches { get; set; }
    }



    /// <summary>Stores metadata about an execution error for use with ERROR_* functions.</summary>
    public record ErrorInfo(int Number, string Message, int Severity, int State, int Line, string? Procedure);

    /// <summary>A stored collection of variable assignments created by CREATE SETS.</summary>
    public class NamedSet
    {
        public List<SetsAssignment> Assignments { get; }
        public bool WithPrompt { get; }
        public NamedSet(List<SetsAssignment> assignments, bool withPrompt) { Assignments = assignments; WithPrompt = withPrompt; }
    }

    public interface IEngineContext
    {
        Functions.IFunctionRegistry FunctionRegistry { get; }
        Task EvaluateStatement(Statement statement);
        Task Evaluate(Script script, System.Threading.CancellationToken cancellationToken = default);
        Task EvaluateProcedure(string name, List<object?> args);
        string ResolvePath(string path);
        int MaxRecursiveDepth { get; set; }
        int CurrentRecursiveDepth { get; set; }
        int BatchSize { get; set; }
        int ForeachPageSize { get; set; }
        int? PreviewLimit { get; set; }
        bool FunctionExists(string name);
        bool ProcedureExists(string name);
    }

    /// <summary>Stores Report-SQL visual, page, and dataset definitions registered during script execution.</summary>
    public interface IReportContext
    {
        /// <summary>Named visual definitions registered by CREATE VISUAL.</summary>
        IDictionary<string, CreateVisualStatement> VisualDefinitions { get; }
        /// <summary>Named page definitions registered by CREATE PAGE.</summary>
        IDictionary<string, CreatePageStatement> PageDefinitions { get; }
        /// <summary>Named dataset definitions registered by CREATE DATASET (includes refresh metadata).</summary>
        IDictionary<string, CreateDatasetStatement> DatasetDefinitions { get; }
        /// <summary>Named container definitions registered by CREATE CONTAINER.</summary>
        IDictionary<string, CreateContainerStatement> ContainerDefinitions { get; }
        /// <summary>Named navigation definitions registered by CREATE NAVIGATION.</summary>
        IDictionary<string, CreateNavigationStatement> NavigationDefinitions { get; }
        /// <summary>Report-level title set by SET REPORT TITLE = '...'</summary>
        string? ReportTitle { get; set; }
        /// <summary>Report-level description set by SET REPORT DESCRIPTION = '...'</summary>
        string? ReportDescription { get; set; }
    }

    /// <summary>
    /// The primary interface for script execution state, providing access to variables, connections,
    /// expression evaluation, and system services (Docker, Lineage, Transactions).
    /// </summary>
    public interface IExecutionContext : IVariableContext, IQueryContext, ISqlCompilerContext,
                                        ITransactionContext, ILineageContext, IDockerContext,
                                        ILoggingContext, IEvaluationContext, IDataContext, IEngineContext,
                                        IReportContext
    {
        Stack<Row> OuterRowStack { get; }
        Dictionary<Statement, object?> SubqueryCache { get; }
        System.Threading.CancellationToken CancellationToken { get; }
        IServiceProvider ServiceProvider { get; }
        
        bool IsProfiling { get; set; }
        bool IsWhatIf { get; set; }
        bool DisplayExecuteTree { get; set; }
        List<ExecutionMetrics> ProfileMetrics { get; }
        Common.ExecutionTree ExecutionTree { get; }
        /// <summary>The ID of the currently executing node in this task/context.</summary>
        Guid? CurrentNodeId { get; set; }

        /// <summary>Standardizer for file/path security and runaway protection.</summary>
        ETL_SQL.Services.SecurityService SecurityService { get; }
        void IncrementOperationCount(string? path = null);

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
        Task EvaluateClearSession(ClearSessionStatement stmt);

        /// <summary>Creates a thread-safe shallow clone of the context for parallel execution branches.</summary>
        IExecutionContext Fork();
        /// <summary>Merges results and metrics from a spawned context back into the parent.</summary>
        void Merge(IExecutionContext spawned);
    }

    public interface ILineageTracker
    {
        Dictionary<string, string> GlobalMetadata { get; }
        void Record(string target, IEnumerable<string> sources, string operation, string? targetColumn = null, IEnumerable<string>? sourceColumns = null, Dictionary<string, string>? metadata = null, string? derivedFromDescriptions = null, int line = 0, int column = 0, int endLine = 0, int endColumn = 0, string? sourceFile = null);
        IEnumerable<LineageEntry> GetLineage(string tableName);
        IEnumerable<LineageEntry> GetColumnLineage(string tableName, string columnName);
        Dictionary<string, string> GetTableMetadata(string tableName);
        Dictionary<string, string> GetColumnMetadata(string tableName, string columnName);
        IEnumerable<LineageEntry> GetAncestors(string tableName, string? columnName = null);
        Dictionary<string, string> InheritMetadata(IEnumerable<string> sourceTables, IEnumerable<string> sourceColumns, out string? derivedFromDescriptions);
        IEnumerable<LineageEntry> GetFullLineage();
        void LoadState(IEnumerable<LineageEntry> entries);
        void Clear();
    }

    public interface IDockerManager : IAsyncDisposable
    {
        bool HasActiveContainers { get; }
        string? LastConnectionString { get; }
        Task<string> StartContainer(string imageName, string? alias = null);
        Task StopContainer(string alias);
        Task PauseContainer(string alias);
        Task ResumeContainer(string alias);
        Task CloseContainers(string? nameOrAlias = null);
        string? GetConnectionString(string alias);
        Dictionary<string, string> GetState();
        void LoadState(Dictionary<string, string> connectionStrings, string? lastConnectionString);
    }
}
