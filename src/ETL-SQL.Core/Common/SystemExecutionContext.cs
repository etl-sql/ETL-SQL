using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core.Functions;
using ETL_SQL.Services;
using ETL_SQL.Core.Spill;

namespace ETL_SQL.Core.Common
{
    /// <summary>
    /// A minimal, stateless implementation of IExecutionContext for use in background tasks
    /// where a full script session (Evaluator) is not available.
    /// Used primarily by the Language Server for metadata discovery.
    /// </summary>
    public class SystemExecutionContext : IExecutionContext
    {
        private static readonly Lazy<SystemExecutionContext> _instance = new(() => new SystemExecutionContext());
        public static SystemExecutionContext Instance => _instance.Value;

        private class ConsoleLogger : ILogger
        {
            public bool IsVerbose { get; set; }
            public bool IsDebugEnabled => false;
            public bool IsVerboseEnabled => false;
            public bool SuppressConsole { get; set; }
            public bool IsJsonMode { get; set; }
            public string? SessionId { get; set; }
            public event Action<string, ConsoleColor>? OnMessage;

            public void Log(LogLevel level, string message, Exception? ex = null) { }
            public void Debug(string message) { }
            public void Info(string message) { }
            public void Warning(string message) { }
            public void Error(string message, Exception? ex = null) { }
            public void WriteLine(string message, ConsoleColor color = ConsoleColor.White) { }
            public void Debug(string template, params object?[] args) { }
            public void Info(string template, params object?[] args) { }
            public void Warning(string template, params object?[] args) { }
            public void Error(string template, Exception? ex, params object?[] args) { }
        }

        public ILogger Logger { get; } = new ConsoleLogger();
        public SecurityService SecurityService { get; }
        public ILineageTracker LineageTracker => throw new NotSupportedException();
        public IDockerManager DockerManager => throw new NotSupportedException();
        public IFunctionRegistry FunctionRegistry => throw new NotSupportedException();
        
        public IDictionary<string, object?> Variables => new Dictionary<string, object?>();
        public IDictionary<string, object?> CurrentVariables => new Dictionary<string, object?>();
        public IDictionary<string, VariableMetadata> VariableMetadata => new Dictionary<string, VariableMetadata>();
        public IDictionary<string, VariableMetadata> CurrentMetadata => new Dictionary<string, VariableMetadata>();
        
        public IDictionary<string, IDataSource> Connections => new Dictionary<string, IDataSource>();
        public IDictionary<string, IDataSource> LocalSources => new Dictionary<string, IDataSource>();
        public IDictionary<string, NamedSet> NamedSets => new Dictionary<string, NamedSet>();
        
        public string? MasterPassword => null;
        public string? ScriptPassword { get; set; }
        public DataTable? LastResult { get; set; }
        public List<DataTable> LastResultSets { get; } = new();
        public long RowsProcessed { get; set; }
        public long TotalSpilledBytes { get; set; }
        public int PartitionsCount { get; set; }
        public long AggregateGroupsCount { get; set; }
        public double AggregateExpansionRatio { get; set; }
        public long LastExecutionTimeMs { get; set; }
        public long SubqueryCacheHits { get; set; }
        public long SubqueryCacheMisses { get; set; }
        public int SortSpillCount { get; set; }
        public Action<DataTable>? OnResultSet { get; set; }
        
        public bool IsProfiling { get; set; }
        public bool IsWhatIf { get; set; }
        public bool DisplayExecuteTree { get; set; }
        public bool IsVerbose { get; set; }
        public bool ShowPassword { get; set; }
        public bool RedirectOutput { get; set; }
        public List<string> Messages { get; } = new();
        public int MaxMessages { get; set; } = 1000;
        public Func<string, Task<bool>>? OnPrompt { get; set; }

        public Stack<Row> OuterRowStack { get; } = new();
        public Dictionary<Statement, object?> SubqueryCache { get; } = new();
        public CancellationToken CancellationToken => CancellationToken.None;
        public IServiceProvider ServiceProvider => null!;
        public List<ExecutionMetrics> ProfileMetrics { get; } = new();
        public ExecutionTree ExecutionTree => new ExecutionTree();
        public Guid? CurrentNodeId { get; set; }

        public int TranCount => 0;
        public int MaxRecursiveDepth { get; set; } = 100;
        public int CurrentRecursiveDepth { get; set; }
        public int BatchSize { get; set; } = 10000;
        public int ForeachPageSize { get; set; } = 1000;
        public int? PreviewLimit { get; set; }

        public ErrorInfo? LastError { get; set; }
        public ErrorInfo? ActiveException { get; set; }
        public int PreviousErrorNumber { get; set; }

        // Hyper-scale thresholds (defaulting to engine presets)
        public int JoinSpillThreshold { get; set; } = LanguageMetadata.DefaultJoinSpillThreshold;
        public int ExternalHashPartitions { get; set; } = LanguageMetadata.DefaultExternalHashPartitions;
        public int ExternalSortChunkSize { get; set; } = LanguageMetadata.DefaultExternalSortChunkSize;
        public int WindowSpillThreshold { get; set; } = LanguageMetadata.DefaultWindowSpillThreshold;
        public int MaxInMemoryBatches { get; set; } = LanguageMetadata.DefaultMaxInMemoryBatches;
        public int MaxParallelDegree { get; set; } = LanguageMetadata.DefaultMaxParallelDegree;
        public long MaxStringResultSize { get; set; } = LanguageMetadata.DefaultMaxStringResultSize;
        public int RegexMatchTimeoutMs { get; set; } = (int)SecurityService.DefaultRegexMatchTimeout.TotalMilliseconds;
        public string? CurrentScriptPath { get; set; }
        public int MaxFileOperations { get; set; } = SecurityService.DefaultMaxFileOperations;
        public bool SpillEncryptionEnabled { get; set; } = true;
        public bool SpillCompressionEnabled { get; set; } = true;
        public ISpillStore SpillStore => null!;

        public bool AllowUnknownFileTypes { get; set; }
        public bool AllowLargeFileOperationCount { get; set; }
        public bool AllowDeepRecursion { get; set; }
        public bool AllowLargeStringResults { get; set; }

        public IDictionary<string, CreateVisualStatement> VisualDefinitions { get; } = new Dictionary<string, CreateVisualStatement>();
        public IDictionary<string, CreatePageStatement> PageDefinitions { get; } = new Dictionary<string, CreatePageStatement>();
        public IDictionary<string, CreateDatasetStatement> DatasetDefinitions { get; } = new Dictionary<string, CreateDatasetStatement>();
        public IDictionary<string, CreateContainerStatement> ContainerDefinitions { get; } = new Dictionary<string, CreateContainerStatement>(StringComparer.OrdinalIgnoreCase);
        public IDictionary<string, CreateNavigationStatement> NavigationDefinitions { get; } = new Dictionary<string, CreateNavigationStatement>(StringComparer.OrdinalIgnoreCase);
        public IDictionary<string, CreateStyleStatement> StyleDefinitions { get; } = new Dictionary<string, CreateStyleStatement>(StringComparer.OrdinalIgnoreCase);
        public string? ReportTitle { get; set; }
        public string? ReportDescription { get; set; }

        public SystemExecutionContext()
        {
            SecurityService = new SecurityService(Logger);
            SecurityService.IsTestMode = true; // Authorized for BaseDir access (bin/obj) in background/test scenarios
        }

        public void SetVariable(string name, object? value) => throw new NotSupportedException();
        public object? GetVariable(string name) => null;
        public void PushScope(Dictionary<string, object?> vars, Dictionary<string, VariableMetadata>? metadata = null) => throw new NotSupportedException();
        public void PopScope() => throw new NotSupportedException();
        public bool ContainsVariable(string name) => false;
        public bool ContainsVariableInCurrentScope(string name) => false;
        public void DeclareVariable(string name, object? value, VariableMetadata? metadata = null) => throw new NotSupportedException();
        public Dictionary<string, object?> GetVariablesWithMetadata(Func<VariableMetadata, bool> predicate) => new();

        public IAsyncEnumerable<DataTable> ExecuteQuery(Statement query) => throw new NotSupportedException();
        public Task<IDataSource> ResolveDataSourceAsync(TableReference table) => throw new NotSupportedException();
        public IAsyncEnumerable<DataTable> ResolveAndApplyOperators(TableReference table) => throw new NotSupportedException();
        public IAsyncEnumerable<DataTable> EvaluateForClause(IAsyncEnumerable<DataTable> batches, ForClause forClause) => throw new NotSupportedException();
        public IAsyncEnumerable<DataTable> InterceptProgress(IAsyncEnumerable<DataTable> chunks) => chunks;
        public ForClause? GetForClause(Statement stmt) => null;
        public TableReference? GetIntoTable(Statement stmt) => null;

        public string CompileExpression(Expression e, string dialect = "MSSQL") => throw new NotSupportedException();
        public string CompileQuery(Statement s, string dialect = "MSSQL") => throw new NotSupportedException();
        public string GetSqlTableName(TableReference t, string dialect = "MSSQL") => throw new NotSupportedException();

        public Task BeginTransaction() => Task.CompletedTask;
        public Task CommitTransaction() => Task.CompletedTask;
        public Task RollbackTransaction(string? name = null) => Task.CompletedTask;

        public void Log(string message, ConsoleColor color = ConsoleColor.White) { }

        public Task<object?> EvaluateValue(Expression? expr, Row context) => Task.FromResult<object?>(null);
        public IAsyncEnumerable<Row> EvaluateStream(Expression? expr, Row context) => AsyncEnumerable.Empty<Row>();
        public Task<bool> EvaluateCondition(Expression? expr, Row context) => Task.FromResult(false);
        public Task<object?> EvaluateUserDefinedFunction(FunctionCallExpression f, List<object?> args, Row context) => Task.FromResult<object?>(null);
        public object? ResolveIdentifier(string name, Row? row) => null;
        public int CompareConstants(object? a, object? b) => 0;
        public bool IsSoftEqual(object? a, object? b) => object.Equals(a, b);
        public object? CastToType(object? value, string dataType) => value;

        public bool IsSqlPushdown(string connName) => false;

        public Task EvaluateStatement(Statement statement) => Task.CompletedTask;
        public Task Evaluate(Script script, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EvaluateProcedure(string name, List<object?> args) => Task.CompletedTask;
        public string ResolvePath(string path) => path;
        public bool FunctionExists(string name) => false;
        public bool ProcedureExists(string name) => false;

        public void IncrementOperationCount(string? path = null) { }
        public List<string> GetIndexedColumns(Expression? cond, string alias) => new();

        public Task EvaluateCreateTable(CreateTableStatement stmt) => Task.CompletedTask;
        public Task EvaluateCreateIndex(CreateIndexStatement stmt) => Task.CompletedTask;
        public void EvaluateCreateFunction(CreateFunctionStatement stmt) { }
        public void EvaluateCreateProcedure(CreateProcedureStatement stmt) { }
        public Task EvaluateDropConnection(DropConnectionStatement stmt) => Task.CompletedTask;
        public void EvaluateDropFunction(DropFunctionStatement stmt) { }
        public Task EvaluateDropIndex(DropIndexStatement stmt) => Task.CompletedTask;
        public void EvaluateDropProcedure(DropProcedureStatement stmt) { }
        public Task EvaluateDropTable(DropTableStatement stmt) => Task.CompletedTask;
        public Task EvaluateClearSession(ClearSessionStatement stmt) => Task.CompletedTask;

        public IExecutionContext Fork() => this;
        public void Merge(IExecutionContext spawned) { }

        // IQueryContext.AlignColumns fix:
        IAsyncEnumerable<DataTable> IQueryContext.AlignColumns(IAsyncEnumerable<DataTable> batches, List<string> targetCols) => batches;
    }
}
