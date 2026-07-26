using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Adaptive;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Planning;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;
using ETL_SQL.Services;

namespace ETL_SQL.Core.Common;
/// <summary>
/// A minimal, stateless implementation of IExecutionContext for use in background tasks
/// where a full script session (Evaluator) is not available.
/// Used primarily by the Language Server for metadata discovery.
/// </summary>
public class SystemExecutionContext : IExecutionContext, IVariableContext, IReportContext, ITelemetryContext
{
    public Governance.ExecutionPolicySnapshot? ExecutionPolicy { get; set; }
    public Governance.ExecutionIdentity? ExecutionIdentity { get; set; }
    public IVariableContext VarContext => this;
    public IReportContext ReportContext => this;
    public ITelemetryContext Telemetry => this;

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
        public event Action<string, string?, ConsoleColor>? OnMessage;

        public void Log(LogLevel level, string message, Exception? ex = null)
        {
            OnMessage?.Invoke(message, SessionId, ConsoleColor.Gray);
        }
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
    public IDatasetRegistry? DatasetRegistry { get; set; }
    public Interfaces.ILanguageHelpRegistry LanguageHelp { get; } = new Metadata.LanguageHelpRegistry();

    public IDictionary<string, object?> Variables => new Dictionary<string, object?>();
    public IDictionary<string, object?> CurrentVariables => new Dictionary<string, object?>();
    public IDictionary<string, VariableMetadata> VariableMetadata => new Dictionary<string, VariableMetadata>();
    public IDictionary<string, VariableMetadata> CurrentMetadata => new Dictionary<string, VariableMetadata>();

    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETL-SQL", "Sessions", SessionId);
    public IDictionary<string, IDataSource> Connections { get; } = new Dictionary<string, IDataSource>(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, IDataSource> LocalSources => new Dictionary<string, IDataSource>();
    public IDictionary<string, NamedSet> NamedSets => new Dictionary<string, NamedSet>();

    public string? MasterPassword => null;
    public string? ScriptPassword { get; set; }
    public DataTable? LastResult { get; set; }
    public List<DataTable> LastResultSets { get; } = new();
    public long RowsProcessed { get; set; }
    public long LastStatementRowsProcessed { get; set; }
    public long TotalSpilledBytes { get; set; }
    public long SpillReadBytes { get; set; }
    public int SpillExtentCount { get; set; }
    public string? LastIndexUsedName { get; set; }
    public bool TelemetryEnabled { get; set; } = true;
    public int PartitionsCount { get; set; }
    public int PartitionPassCount { get; set; }
    public long AggregateGroupsCount { get; set; }
    public double AggregateExpansionRatio { get; set; }
    public long LastExecutionTimeMs { get; set; }
    public long SubqueryCacheHits { get; set; }
    public long SubqueryCacheMisses { get; set; }
    public int SubquerySpillCount { get; set; }
    public long SubquerySpilledBytes { get; set; }
    public int SortSpillCount { get; set; }
    public int FetchStatus { get; set; }
    public Action<DataTable>? OnResultSet { get; set; }

    public bool IsProfiling { get; set; }
    public long QueueWaitMs { get; set; }
    public long LockWaitMs { get; set; }
    public bool IsWhatIf { get; set; }
    public bool LineageEnabled { get; set; } = true;
    public string? LineageNamespace { get; set; } = "etl-sql";
    public string? JobName { get; set; }
    public Dictionary<string, string> PendingJobStateUpdates { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool LineageImportCatalog { get; set; }
    public bool TruncateString { get; set; }
    public bool SkipError { get; set; }
    public bool DisplayExecuteTree { get; set; }
    public bool IsVerbose { get; set; }
    public bool ShowPassword { get; set; }
    public bool AllowPlaintextSecrets { get; set; }
    public bool NoSaveSensitive { get; set; }
    public bool NoSaveConnection { get; set; }
    public bool ConnectionEncryption { get; set; }
    public bool RedirectOutput { get; set; }
    public List<LogEntry> Messages { get; } = new();
    public int MaxMessages { get; set; } = 1000;
    public Func<string, Task<bool>>? OnPrompt { get; set; }
    public Action<Diagnostic>? OnMessage { get; set; }
    public bool IsPersistentSession { get; set; }
    public bool IsResuming { get; set; }
    public bool InteractiveMode { get; set; }
    public bool IsMockMode { get; set; }
    public List<object?>? Parameters { get; set; }
    public DayOfWeek WeekStartDay { get; set; } = DayOfWeek.Monday;
    public string ScriptHashPolicy { get; set; } = "Warn";
    public bool CaseSensitiveComparison { get; set; }
    public bool DataQualityDryRun { get; set; }
    public Quality.DataQualityReport DataQuality { get; } = new();

    public Stack<Row> OuterRowStack { get; } = new();
    public LruCache<SubqueryCacheKey, Data.SubqueryResult> SubqueryCache { get; } = new(5000);
    public CancellationToken CancellationToken => CancellationToken.None;
    public IServiceProvider ServiceProvider => null!;
    public List<ExecutionMetrics> ProfileMetrics { get; } = new();
    public ExecutionTree ExecutionTree => new ExecutionTree();
    public IReadOnlyList<PlanDecision> PlanDecisions { get; } = Array.Empty<PlanDecision>();
    public int MaxPlanDecisions { get; set; } = 0;
    public void RecordPlanDecision(PlanDecision decision) { }
    public void Clear() { }
    /// <summary>No-op for stateless context.</summary>
    void IReportContext.Clear() { }
    public Guid? CurrentNodeId { get; set; }

    public int TranCount => 0;
    public bool AutoRollbackOnFinish { get; set; } = true;
    public int MaxRecursiveDepth { get; set; } = 100;
    public int CurrentRecursiveDepth { get; set; }
    public int BatchSize { get; set; } = 10000;
    public int MaxLastResultRows { get; set; } = LanguageMetadata.DefaultMaxLastResultRows;
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
    public int OperatorMemoryGrantMB { get; set; } = 256;
    public int MaxInMemoryBatches { get; set; } = LanguageMetadata.DefaultMaxInMemoryBatches;
    public long SubquerySpillThresholdRows { get; set; } = LanguageMetadata.DefaultSubquerySpillThresholdRows;
    public long TempTableSpillThresholdRows { get; set; } = LanguageMetadata.DefaultTempTableSpillThresholdRows;
    public int MaxParallelDegree { get; set; } = LanguageMetadata.DefaultMaxParallelDegree;
    public bool AdaptiveExecutionEnabled { get; set; }
    public AdaptiveAdvisor? AdaptiveAdvisor { get; set; }
    public AdaptiveRuntimeMetrics AdaptiveMetrics { get; } = new();
    public long MaxStringResultSize { get; set; } = LanguageMetadata.DefaultMaxStringResultSize;
    public int RegexMatchTimeoutMs { get; set; } = (int)SecurityService.DefaultRegexMatchTimeout.TotalMilliseconds;
    public string? CurrentScriptPath { get; set; }
    public string? CurrentSectionLabel { get; set; }
    public int MaxFileOperations { get; set; } = SecurityService.DefaultMaxFileOperations;
    public int MaxGroupingSets { get; set; } = LanguageMetadata.DefaultMaxGroupingSets;
    public long MaxSessionSize { get; set; } = LanguageMetadata.DefaultMaxSessionSize;
    public bool SpillEncryptionEnabled { get; set; } = true;
    public bool SpillCompressionEnabled { get; set; } = true;
    public string SpillFormat { get; set; } = "Arrow";
    public ISpillStore SpillStore => null!;
    public string? DecryptValue(string? value) => value;
    public ISessionStateManager SessionStateManager { get; set; } = new NullSessionStateManager();

    public bool AllowUnknownFileTypes { get; set; }
    public bool AllowLargeFileOperationCount { get; set; }
    public bool AllowDeepRecursion { get; set; }
    public bool AllowLargeStringResults { get; set; }
    public HashSet<string> AllowedFileTypeOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int MaxGenerateRows { get => SecurityService.DefaultMaxGenerateRows; set { } }
    public int MaxSmtpEmailsPerScript { get; set; } = SecurityService.DefaultMaxSmtpEmailsPerScript;
    public int SmtpEmailsSentThisScript { get; private set; }
    public void RecordSmtpEmailSend()
    {
        SmtpEmailsSentThisScript++;
        if (MaxSmtpEmailsPerScript >= 0 && SmtpEmailsSentThisScript > MaxSmtpEmailsPerScript)
            throw new ETL_SQL.Services.SecurityException($"SMTP send limit exceeded: this script attempted to send {SmtpEmailsSentThisScript} emails, but MAX_SMTP_EMAILS_PER_SCRIPT is {MaxSmtpEmailsPerScript}.");
    }
    public int MaxInternalOperations { get => SecurityService.MaxInternalOperations; set => SecurityService.MaxInternalOperations = value; }
    public int MaxConnectionsPerScript { get; set; } = 100;
    public int MaxTempTablesPerScript { get; set; } = 100;
    public int MaxVariablesPerScript { get; set; } = 100;
    public int MaxVisualsPerScript { get; set; } = 100;

    public IDictionary<string, CreateVisualStatement> VisualDefinitions { get; } = new Dictionary<string, CreateVisualStatement>();
    public IDictionary<string, CreatePageStatement> PageDefinitions { get; } = new Dictionary<string, CreatePageStatement>();
    public IDictionary<string, CreateDatasetStatement> DatasetDefinitions { get; } = new Dictionary<string, CreateDatasetStatement>();
    public IDictionary<string, CreateContainerStatement> ContainerDefinitions { get; } = new Dictionary<string, CreateContainerStatement>(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, CreateNavigationStatement> NavigationDefinitions { get; } = new Dictionary<string, CreateNavigationStatement>(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, CreateStyleStatement> StyleDefinitions { get; } = new Dictionary<string, CreateStyleStatement>(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, CreateButtonStatement> ButtonDefinitions { get; } = new Dictionary<string, CreateButtonStatement>(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, CreateTemplateStatement> TemplateDefinitions { get; } = new Dictionary<string, CreateTemplateStatement>(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, CreateThemeStatement> ThemeDefinitions { get; } = new Dictionary<string, CreateThemeStatement>(StringComparer.OrdinalIgnoreCase);
    public string TemplatePath { get; set; } = "./Templates";
    public string? ReportTitle { get; set; }
    public IDictionary<string, string> BaselineParameters { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool ReportTitleIsMarkdown { get; set; }
    public string? ReportDescription { get; set; }
    public string? ReportCss { get; set; }
    public string? ReportJs { get; set; }
    public string? ReportHtmlHead { get; set; }
    public string? ReportHtmlBody { get; set; }
    public string? ReportHtmlFooter { get; set; }
    public string? ReportFavicon { get; set; }
    public string? ReportLogo { get; set; }
    public string? ReportBackground { get; set; }
    public string? ReportTheme { get; set; }
    public string? ReportNavigation { get; set; }

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
    public bool RemoveProcedure(string name) => false;
    public void SetProcedure(string name, CreateProcedureStatement stmt) => throw new NotSupportedException();
    public bool TryGetProcedure(string name, out CreateProcedureStatement? stmt) { stmt = null; return false; }
    public void SetFunction(string name, CreateFunctionStatement stmt) => throw new NotSupportedException();
    public bool RemoveFunction(string name) => false;
    public bool TryGetFunction(string name, out CreateFunctionStatement? stmt) { stmt = null; return false; }
    public void SetView(string name, CreateViewStatement stmt) => throw new NotSupportedException();
    public bool RemoveView(string name) => false;
    public bool TryGetView(string name, out CreateViewStatement? stmt) { stmt = null; return false; }
    public IReadOnlyDictionary<string, CreateViewStatement> GetViews() => new Dictionary<string, CreateViewStatement>(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, (object? Value, VariableMetadata Metadata)> GetVariablesWithMetadata(Func<VariableMetadata, bool>? predicate = null) => new Dictionary<string, (object? Value, VariableMetadata Metadata)>();
    public void Reset() { }

    public IAsyncEnumerable<DataTable> ExecuteQuery(Statement query) => throw new NotSupportedException();
    public Task<IDataSource> ResolveDataSourceAsync(TableReference table) => throw new NotSupportedException();
    public IAsyncEnumerable<DataTable> ResolveAndApplyOperators(TableReference table) => throw new NotSupportedException();
    public IAsyncEnumerable<DataTable> EvaluateForClause(IAsyncEnumerable<DataTable> batches, ForClause forClause) => throw new NotSupportedException();
    public IAsyncEnumerable<DataTable> InterceptProgress(IAsyncEnumerable<DataTable> chunks) => chunks;
    public ForClause? GetForClause(Statement stmt) => null;
    public TableReference? GetIntoTable(Statement stmt) => null;

    public CompiledSql CompileExpression(Expression e, string dialect = "MSSQL") => CompiledSql.Empty;
    public CompiledSql CompileQuery(Statement s, string dialect = "MSSQL") => CompiledSql.Empty;
    public string GetSqlTableName(TableReference t, string dialect = "MSSQL") => throw new NotSupportedException();

    public Task BeginTransaction() => Task.CompletedTask;
    public Task CommitTransaction() => Task.CompletedTask;
    public Task RollbackTransaction(string? name = null) => Task.CompletedTask;
    public Task RollbackAllTransactions() => Task.CompletedTask;

    public void Log(string message, ConsoleColor color = ConsoleColor.White, bool forwardToLogger = true)
    {
        if (Messages.Count >= MaxMessages && MaxMessages > 0)
            Messages.RemoveAt(0);

        Messages.Add(new LogEntry(message, color, DateTime.UtcNow));
    }

    public virtual ValueTask<object?> EvaluateValue(Expression? expr, Row context, bool decryptSensitive = false) => new ValueTask<object?>(null as object);
    public IAsyncEnumerable<Row> EvaluateStream(Expression? expr, Row context) => AsyncEnumerable.Empty<Row>();
    public ValueTask<bool> EvaluateCondition(Expression? expr, Row context) => new ValueTask<bool>(false);
    public ValueTask<object?> EvaluateUserDefinedFunction(FunctionCallExpression f, List<object?> args, Row context) => new ValueTask<object?>(null as object);
    public object? ResolveIdentifier(string name, Row? row) => null;
    public int CompareConstants(object? a, object? b) => 0;
    public bool IsSoftEqual(object? a, object? b) => object.Equals(a, b);
    public object? CastToType(object? value, string dataType) => value;

    public bool IsSqlPushdown(string connName) => false;

    public Task EvaluateStatement(Statement statement, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task Evaluate(Script script, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public IAsyncEnumerable<DataTable> EvaluateSelect(SelectStatement stmt) => throw new NotSupportedException();
    public Task EvaluateProcedure(string name, List<(string? Name, object? Value)> args) => Task.CompletedTask;
    public string ResolvePath(string path) => path;
    public bool FunctionExists(string name) => false;
    public bool ProcedureExists(string name) => false;

    public void IncrementOperationCount(OperationType type = OperationType.FileSystem, string? path = null, int count = 1) { }
    public IDisposable EnterRecursiveScope() => new DummyDisposable();
    private class DummyDisposable : IDisposable { public void Dispose() { } }
    public List<string> GetIndexedColumns(Expression? cond, string alias) => new();

    public IExecutionContext Fork() => this;
    public void Merge(IExecutionContext spawned) { }
    public Task ResetSessionAsync() => Task.CompletedTask;

    // IQueryContext.AlignColumns fix:
    IAsyncEnumerable<DataTable> IQueryContext.AlignColumns(IAsyncEnumerable<DataTable> batches, List<string> targetCols) => batches;
}
