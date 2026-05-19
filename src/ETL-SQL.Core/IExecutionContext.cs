using ETL_SQL.Data;
using ETL_SQL.Core.Data;
using ETL_SQL.Common;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core.Spill;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Core
{
    public enum OperationType
    {
        /// <summary>Standard file system operations (Delete, Copy, Move, Rename, Zip, Encrypt).</summary>
        FileSystem,
        /// <summary>Internal engine operations (Procedure/Function calls, Template generation).</summary>
        EngineInternal,
        /// <summary>Mock data generation operations.</summary>
        MockData
    }

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
        bool RemoveProcedure(string name);
        void SetProcedure(string name, CreateProcedureStatement stmt);
        bool TryGetProcedure(string name, out CreateProcedureStatement? stmt);
        void SetFunction(string name, CreateFunctionStatement stmt);
        bool RemoveFunction(string name);
        bool TryGetFunction(string name, out CreateFunctionStatement? stmt);
        IDictionary<string, (object? Value, VariableMetadata Metadata)> GetVariablesWithMetadata(Func<VariableMetadata, bool>? predicate = null);
        /// <summary>Purges all variables, procedures, and functions from the context.</summary>
        void Reset();
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
        CompiledSql CompileExpression(Expression e, string dialect = "MSSQL");
        CompiledSql CompileQuery(Statement s, string dialect = "MSSQL");
        string GetSqlTableName(TableReference t, string dialect = "MSSQL");
    }

    public interface ITransactionContext
    {
        Task BeginTransaction();
        Task CommitTransaction();
        Task RollbackTransaction(string? name = null);
        int TranCount { get; }
        /// <summary>Whether to automatically rollback open transactions when a script finishes (Zero-Trust safety).</summary>
        bool AutoRollbackOnFinish { get; set; }
        Task RollbackAllTransactions();
    }

    public interface IDockerContext
    {
        IDockerManager DockerManager { get; }
    }

    public record LogEntry(string Message, ConsoleColor Color, DateTime Timestamp);

    public interface ILoggingContext
    {
        ETL_SQL.Common.ILogger Logger { get; }
        bool IsVerbose { get; set; }
        bool ShowPassword { get; set; }
        bool RedirectOutput { get; set; }
        List<LogEntry> Messages { get; }
        int MaxMessages { get; set; }
        void Log(string message, ConsoleColor color = ConsoleColor.White, bool forwardToLogger = true);
        /// <summary>
        /// Optional interactive prompt callback. Returns true to proceed, false to abort.
        /// Null means non-interactive (auto-proceed).
        /// </summary>
        Func<string, Task<bool>>? OnPrompt { get; set; }
    }

    public interface IEvaluationContext
    {
        ValueTask<object?> EvaluateValue(Expression? expr, Row context, bool decryptSensitive = false);
        IAsyncEnumerable<Row> EvaluateStream(Expression? expr, Row context);
        ValueTask<bool> EvaluateCondition(Expression? expr, Row context);
        ValueTask<object?> EvaluateUserDefinedFunction(FunctionCallExpression f, List<object?> args, Row context);
        object? ResolveIdentifier(string name, Row? row);
        int CompareConstants(object? a, object? b);
        bool IsSoftEqual(object? a, object? b);
        object? CastToType(object? value, string dataType);
    }

    public interface ITelemetryContext
    {
        long RowsProcessed { get; set; }
        long LastStatementRowsProcessed { get; set; }
        long TotalSpilledBytes { get; set; }
        bool TelemetryEnabled { get; set; }
        int PartitionsCount { get; set; }
        long AggregateGroupsCount { get; set; }
        double AggregateExpansionRatio { get; set; }
        long LastExecutionTimeMs { get; set; }
        long SubqueryCacheHits { get; set; }
        long SubqueryCacheMisses { get; set; }
        int SubquerySpillCount { get; set; }
        long SubquerySpilledBytes { get; set; }
        int SortSpillCount { get; set; }
        int FetchStatus { get; set; }
        bool IsProfiling { get; set; }
        List<ExecutionMetrics> ProfileMetrics { get; }
        Common.ExecutionTree ExecutionTree { get; }
        void Clear();
    }

    public interface IDataContext
    {
        string? SessionId { get; }
        string SessionRoot { get; }
        IDictionary<string, IDataSource> Connections { get; }
        /// <summary>Statement-local data source overrides (used for CTEs).</summary>
        IDictionary<string, IDataSource> LocalSources { get; }
        string? MasterPassword { get; }
        string? ScriptPassword { get; set; }
        DataTable? LastResult { get; set; }
        List<DataTable> LastResultSets { get; }
        string? LastIndexUsedName { get; set; }
        Action<DataTable>? OnResultSet { get; set; }
        bool IsSqlPushdown(string connName);
        /// <summary>Named environment sets created by CREATE SETS.</summary>
        IDictionary<string, NamedSet> NamedSets { get; }
        
        // Security override flags (granted via ### flags in script)
        bool AllowUnknownFileTypes { get; set; }
        bool AllowLargeFileOperationCount { get; set; }
        bool AllowDeepRecursion { get; set; }
        bool AllowLargeStringResults { get; set; }
        HashSet<string> AllowedFileTypeOverrides { get; }
        int MaxGenerateRows { get; set; }
        int MaxSmtpEmailsPerScript { get; set; }
        int SmtpEmailsSentThisScript { get; }
        void RecordSmtpEmailSend();
        int MaxInternalOperations { get; set; }

        
        /// <summary>Metadata about the last caught exception in this session.</summary>
        ErrorInfo? LastError { get; set; }
        /// <summary>The error that caused the current CATCH block to run (persists for the duration of the block).</summary>
        ErrorInfo? ActiveException { get; set; }
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
        /// <summary>Per-operator memory budget in MB. When the estimated working set exceeds this limit the engine switches to an external (disk-spilling) operator.</summary>
        int OperatorMemoryGrantMB { get; set; }
        /// <summary>Maximum number of batches held in RAM for #temp tables before spilling.</summary>
        int MaxInMemoryBatches { get; set; }
        /// <summary>Number of rows before subquery results spill to disk.</summary>
        long SubquerySpillThresholdRows { get; set; }
        /// <summary>The maximum number of concurrent tasks allowed in a PARALLEL block.</summary>
        int MaxParallelDegree { get; set; }
        /// <summary>The maximum size in bytes allowed for a single string function result.</summary>
        long MaxStringResultSize { get; set; }
        /// <summary>Milliseconds to wait before timing out a regular expression match.</summary>
        int RegexMatchTimeoutMs { get; set; }
        /// <summary>The absolute path to the script file currently being executed (if any).</summary>
        string? CurrentScriptPath { get; set; }
        /// <summary>Maximum number of file operations allowed in a single script.</summary>
        int MaxFileOperations { get; set; }
        /// <summary>Maximum number of grouping sets allowed in a CUBE/ROLLUP operation.</summary>
        int MaxGroupingSets { get; set; }
        /// <summary>Maximum size in bytes for a persisted session payload (CFG-G4).</summary>
        long MaxSessionSize { get; set; }
        /// <summary>Whether this session is marked for persistence across process runs.</summary>
        bool IsPersistentSession { get; set; }
        /// <summary>The start-of-week day used by RELDATE W/WS/WE anchors. Defaults to Monday (ISO 8601).</summary>
        DayOfWeek WeekStartDay { get; set; }
        /// <summary>Hash-mismatch policy for script integrity checks. "Warn" logs and continues; "Block" throws. Defaults to "Warn".</summary>
        string ScriptHashPolicy { get; set; }
        /// <summary>When true, string comparisons are case-sensitive. Defaults to false (SQL Server-style case-insensitive). Settable at runtime via SET CASE_SENSITIVE.</summary>
        bool CaseSensitiveComparison { get; set; }
        
        /// <summary>Positional parameters provided for the current execution (for ? and ?n placeholders).</summary>
        List<object?>? Parameters { get; set; }
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
        Interfaces.ILanguageHelpRegistry LanguageHelp { get; }
        Task EvaluateStatement(Statement statement, System.Threading.CancellationToken cancellationToken = default);
        Task Evaluate(Script script, System.Threading.CancellationToken cancellationToken = default);
        IAsyncEnumerable<DataTable> EvaluateSelect(SelectStatement stmt);
        Task EvaluateProcedure(string name, List<(string? Name, object? Value)> args);
        string ResolvePath(string path);
        int MaxRecursiveDepth { get; set; }
        int CurrentRecursiveDepth { get; set; }
        int BatchSize { get; set; }
        long TempTableSpillThresholdRows { get; set; }
        int MaxLastResultRows { get; set; }
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
        /// <summary>Named style definitions registered by CREATE STYLE.</summary>
        IDictionary<string, CreateStyleStatement> StyleDefinitions { get; }
        /// <summary>Named button definitions registered by CREATE BUTTON.</summary>
        IDictionary<string, CreateButtonStatement> ButtonDefinitions { get; }
        /// <summary>Named template definitions registered by CREATE TEMPLATE.</summary>
        IDictionary<string, CreateTemplateStatement> TemplateDefinitions { get; }
        /// <summary>Named theme definitions registered by CREATE THEME.</summary>
        IDictionary<string, CreateThemeStatement> ThemeDefinitions { get; }
        /// <summary>The directory where .json style templates are discovered.</summary>
        string TemplatePath { get; set; }
        /// <summary>Report-level title set by SET REPORT TITLE = '...'</summary>
        string? ReportTitle { get; set; }
        /// <summary>Baseline parameter values used for ghosting in cross-highlighting.</summary>
        IDictionary<string, string> BaselineParameters { get; }
        /// <summary>Whether the report title is markdown.</summary>
        bool ReportTitleIsMarkdown { get; set; }
        string? ReportDescription { get; set; }
        string? ReportCss { get; set; }
        string? ReportJs { get; set; }
        string? ReportHtmlHead { get; set; }
        string? ReportHtmlBody { get; set; }
        string? ReportHtmlFooter { get; set; }
        string? ReportFavicon { get; set; }
        string? ReportLogo { get; set; }
        string? ReportBackground { get; set; }
        string? ReportTheme { get; set; }
        string? ReportNavigation { get; set; }
        /// <summary>Clears all visual and report definitions.</summary>
        void Clear();
    }

    /// <summary>
    /// The primary interface for script execution state, providing access to variables, connections,
    /// expression evaluation, and system services (Docker, Lineage, Transactions).
    /// </summary>
    public interface IExecutionContext : IQueryContext, ISqlCompilerContext,
                                        ITransactionContext, ILineageContext, IDockerContext,
                                        ILoggingContext, IEvaluationContext, IDataContext, IEngineContext, IVariableContext
    {
        // Property-based access to sub-contexts for better interface segregation (TODO-91)
        IVariableContext VarContext { get; }
        IReportContext ReportContext { get; }
        ITelemetryContext Telemetry { get; }
        IDatasetRegistry? DatasetRegistry { get; }
        
        /// <summary>Event raised when a diagnostic message is emitted (Interactive Mode).</summary>
        Action<Diagnostic>? OnMessage { get; set; }
        
        /// <summary>
        /// Whether the engine is in interactive mode (e.g. Notebooks/REPL).
        /// Enables global idempotency for object creation and immediate visual emission.
        /// </summary>
        bool InteractiveMode { get; set; }
        
        IEvaluationContext EvaluationContext => this;
        IDataContext DataContext => this;
        IQueryContext QueryContext => this;
        IEngineContext EngineContext => this;
        ILoggingContext LoggingContext => this;
        ITransactionContext TransactionContext => this;
        ILineageContext LineageContext => this;

        bool SpillEncryptionEnabled { get; set; }
        bool SpillCompressionEnabled { get; set; }
        string SpillFormat { get; set; }
        ISpillStore SpillStore { get; }
        
        /// <summary>Decrypts an 'ENC:...' value using the current session context passwords.</summary>
        string? DecryptValue(string? value);

        Stack<Row> OuterRowStack { get; }
        Common.LruCache<Data.SubqueryCacheKey, Data.SubqueryResult> SubqueryCache { get; }
        System.Threading.CancellationToken CancellationToken { get; }
        IServiceProvider ServiceProvider { get; }
        
        bool IsWhatIf { get; set; }
        bool LineageEnabled { get; set; }
        bool TelemetryEnabled { get; set; }
        bool DisplayExecuteTree { get; set; }
        /// <summary>The ID of the currently executing node in this task/context.</summary>
        Guid? CurrentNodeId { get; set; }

        /// <summary>Standardizer for file/path security and runaway protection.</summary>
        ETL_SQL.Services.SecurityService SecurityService { get; }
        
        /// <summary>Manager for session persistence and key derivation.</summary>
        ETL_SQL.Core.Execution.ISessionStateManager SessionStateManager { get; }
        
        void IncrementOperationCount(OperationType type = OperationType.FileSystem, string? path = null, int count = 1);
        IDisposable EnterRecursiveScope();

        List<string> GetIndexedColumns(Expression? cond, string alias);




        /// <summary>Creates a thread-safe shallow clone of the context for parallel execution branches.</summary>
        IExecutionContext Fork();
        /// <summary>Merges results and metrics from a spawned context back into the parent.</summary>
        void Merge(IExecutionContext spawned);
        /// <summary>Resets the entire session (variables, temp tables, results, transactions, lineage, report definitions) to a clean state.</summary>
        Task ResetSessionAsync();
    }

    public interface ILineageTracker
    {
        Dictionary<string, string> GlobalMetadata { get; }
        void Record(string target, IEnumerable<string> sources, string operation, string? targetColumn = null, IEnumerable<string>? sourceColumns = null, Dictionary<string, string>? metadata = null, string? derivedFromDescriptions = null, int line = 0, int column = 0, int endLine = 0, int endColumn = 0, string? sourceFile = null, TransformationKind transformationKind = TransformationKind.Unknown, string? transformationExpression = null, IReadOnlyList<string>? functionsApplied = null);
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
        string? LastAlias { get; }
        Task<string> StartContainer(string imageName, string? alias = null);
        Task StopContainer(string? alias);
        Task PauseContainer(string? alias);
        Task ResumeContainer(string? alias);
        Task CloseContainers(string? nameOrAlias = null);
        string? GetConnectionString(string alias);
        Dictionary<string, string> GetState();
        void LoadState(Dictionary<string, string> connectionStrings, string? lastConnectionString);
    }
}
