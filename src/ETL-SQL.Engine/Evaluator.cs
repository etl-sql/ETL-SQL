using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Adaptive;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace ETL_SQL.Engine;
// Using ExecutionException from ETL_SQL.Core.Common.Exceptions

public class BreakException : Exception { }
public class ContinueException : Exception { }
public class GotoException(string labelName) : Exception
{
    public string LabelName { get; } = labelName;
}

public class ReturnException : Exception
{
    public object? Value { get; }
    public ReturnException(object? value) { Value = value; }
}

/// <summary>
/// The primary execution engine for ETL-SQL scripts.
/// Coordinates connections, variables, statement handlers, and expression evaluation.
/// </summary>
public partial class Evaluator : IExecutionContext, IAsyncDisposable, IDataValidator, ISpillable
{
    public ExecutionPolicySnapshot? ExecutionPolicy { get; set; }
    public ExecutionIdentity? ExecutionIdentity { get; set; }
    /// <summary>
    /// Process-wide by default; settable so tests (and future per-tenant hosting) can bound an
    /// evaluator with an isolated budget instead of mutating <see cref="MemoryGrantArbiter.Shared"/>.
    /// </summary>
    public IMemoryGrantArbiter MemoryArbiter { get; set; } = MemoryGrantArbiter.Shared;
    private readonly IEnumerable<IStatementHandler> _handlers;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly Core.Functions.IFunctionRegistry _functionRegistry;
    private readonly ILineageTracker _lineageTracker;
    private readonly IDockerManager _dockerManager;
    private readonly IConnectorRegistry _connectorRegistry;
    private readonly ISessionStateManager _sessionStateManager;
    public SecurityService SecurityService => _securityService;
    private readonly SecurityService _securityService;
    private readonly ETL_SQL.Common.ILogger _logger;
    private readonly Core.Interfaces.ILanguageHelpRegistry _languageHelp;
    private readonly ConcurrentDictionary<string, IDataSource> _connections;
    private readonly IBufferManager? _bufferManager;
    private readonly Dictionary<string, IDataSource> _localSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingJobStateUpdates = new();

    public IDictionary<string, IDataSource> Connections => _connections;
    public IDictionary<string, IDataSource> LocalSources => _localSources;
    public Dictionary<string, string> PendingJobStateUpdates => _pendingJobStateUpdates;

    private readonly VariableScopeManager _variableScopeManager;
    private readonly EvaluatorComponentRegistry _registry;
    private readonly QueryCompiler _queryCompiler;
    private readonly ExecutionMetricsReporter _metricsReporter;
    private readonly DataSourceManager _dataSourceManager;
    private readonly SchemaManager _schemaManager;
    private readonly ExpressionEvaluator _expressionEvaluator;
    private readonly ProcedureExecutor _procedureExecutor;
    private readonly DataConstraintValidator _constraintValidator;
    private readonly EvaluatorSpillCoordinator _spillCoordinator;
    private readonly BatchPipelineHelper _batchPipelineHelper = new();
    private readonly Dictionary<Type, IStatementHandler> _statementHandlers = new();

    private readonly Stack<Row> _outerRowStack = new();
    private readonly ETL_SQL.Core.Common.LruCache<SubqueryCacheKey, ETL_SQL.Core.Data.SubqueryResult> _subqueryCache;
    private readonly Dictionary<NodeReuseKey, ExecutionNode> _nodeReuseMap = new();
    private readonly TransactionManager _transactionManager = new();
    private readonly ISpillStore _spillStore;
    private AdaptiveExecutionController? _adaptiveController;
    private AdaptiveAdvisor? _adaptiveAdvisor;
    private bool _ownsAdaptiveAdvisor;
    private ResourceSignalSampler? _adaptiveSampler;
    private CancellationTokenSource? _adaptiveSamplerCts;
    private Task? _adaptiveSamplerTask;
    private Action<string, string?, ConsoleColor>? _onMessageHandler;

    public ISpillStore SpillStore => _spillStore;

    public QueryCompiler QueryCompiler => _registry.QueryCompiler;
    public ExpressionEvaluator ExpressionEvaluator => _registry.ExpressionEvaluator;
    public ProcedureExecutor ProcedureExecutor => _registry.ProcedureExecutor;
    public DataSourceManager DataSourceManager => _registry.DataSourceManager;
    public SchemaManager SchemaManager => _registry.SchemaManager;
    public ExecutionMetricsReporter MetricsReporter => _registry.MetricsReporter;

    /// <summary>Current transaction nesting level.</summary>
    public int TranCount => _transactionManager.TranCount;

    private readonly EvaluatorOptions _options;
    public EvaluatorOptions Options => _options;

    public bool AutoRollbackOnFinish { get => _options.AutoRollbackOnFinish; set => _options.AutoRollbackOnFinish = value; }

    [System.Obsolete("Use Telemetry.ExecutionTree")]
    public ExecutionTree ExecutionTree => Telemetry.ExecutionTree;

    public IReportContext ReportContext => _registry.ReportContext;

    public long TempTableSpillThresholdRows { get => _options.TempTableSpillThresholdRows; set => _options.TempTableSpillThresholdRows = value; }
    public int MaxRecursiveDepth
    {
        get => _options.MaxRecursiveDepth;
        set { _options.MaxRecursiveDepth = value; _securityService.MaxRecursiveDepth = value; }
    }
    public int CurrentRecursiveDepth { get; set; } = 0;
    public string? LastIndexUsedName { get; set; }

    public bool AllowUnknownFileTypes { get => _options.AllowUnknownFileTypes; set => _options.AllowUnknownFileTypes = value; }
    public bool AllowLargeFileOperationCount { get => _options.AllowLargeFileOperationCount; set => _options.AllowLargeFileOperationCount = value; }
    public bool AllowDeepRecursion { get => _options.AllowDeepRecursion; set => _options.AllowDeepRecursion = value; }
    public bool AllowLargeStringResults { get => _options.AllowLargeStringResults; set => _options.AllowLargeStringResults = value; }
    public HashSet<string> AllowedFileTypeOverrides => _options.AllowedFileTypeOverrides;

    public int MaxParallelDegree
    {
        get => _options.MaxParallelDegree;
        set { _options.MaxParallelDegree = value; _securityService.MaxParallelDegree = value; }
    }
    public long MaxStringResultSize
    {
        get => _options.MaxStringResultSize;
        set { _options.MaxStringResultSize = value; _securityService.MaxStringResultSize = value; }
    }
    public int RegexMatchTimeoutMs
    {
        get => _options.RegexMatchTimeoutMs;
        set { _options.RegexMatchTimeoutMs = value; _securityService.RegexMatchTimeout = TimeSpan.FromMilliseconds(value); }
    }
    public string? CurrentScriptPath { get; set; }
    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();
    public int MaxFileOperations
    {
        get => _options.MaxFileOperations;
        set { _options.MaxFileOperations = value; _securityService.MaxFileOperations = value; }
    }
    public int MaxGroupingSets { get => _options.MaxGroupingSets; set => _options.MaxGroupingSets = value; }
    public long MaxSessionSize { get => _options.MaxSessionSize; set => _options.MaxSessionSize = value; }
    public int MaxLastResultRows { get => _options.MaxLastResultRows; set => _options.MaxLastResultRows = value; }
    public int MaxGenerateRows { get => _options.MaxGenerateRows; set => _options.MaxGenerateRows = value; }
    public int MaxSmtpEmailsPerScript { get => _options.MaxSmtpEmailsPerScript; set => _options.MaxSmtpEmailsPerScript = value; }
    private int _smtpEmailsSentThisScript;
    public int SmtpEmailsSentThisScript => _smtpEmailsSentThisScript;
    public void RecordSmtpEmailSend()
    {
        var count = System.Threading.Interlocked.Increment(ref _smtpEmailsSentThisScript);
        if (MaxSmtpEmailsPerScript >= 0 && count > MaxSmtpEmailsPerScript)
        {
            throw new SecurityException($"SMTP send limit exceeded: this script attempted to send {count} emails, but MAX_SMTP_EMAILS_PER_SCRIPT is {MaxSmtpEmailsPerScript}.");
        }
        // Enterprise ceiling binds regardless of the local (SET-overridable) limit above.
        if (IsEnterpriseGoverned)
            ETL_SQL.Core.Governance.OperationPolicyBoundary.EnforceCeiling(this,
                "Security:MaxSmtpEmailsPerScript", count, "<smtp-send>");
    }
    public int MaxInternalOperations
    {
        get => _options.MaxInternalOperations;
        set { _options.MaxInternalOperations = value; _securityService.MaxInternalOperations = value; }
    }
    public int MaxConnectionsPerScript { get => _options.MaxConnectionsPerScript; set => _options.MaxConnectionsPerScript = value; }
    public int MaxTempTablesPerScript { get => _options.MaxTempTablesPerScript; set => _options.MaxTempTablesPerScript = value; }
    public int MaxVariablesPerScript { get => _options.MaxVariablesPerScript; set => _options.MaxVariablesPerScript = value; }
    public int MaxVisualsPerScript { get => _options.MaxVisualsPerScript; set => _options.MaxVisualsPerScript = value; }

    public bool IsPersistentSession { get; set; }
    public bool IsResuming { get; set; }
    public string? ResumeLabel { get; set; }
    public List<object?>? Parameters { get; set; }
    /// <summary>Start-of-week day for RELDATE W/WS/WE anchors. Settable at runtime via SET WEEK_START_DAY.</summary>
    public DayOfWeek WeekStartDay { get => _options.WeekStartDay; set => _options.WeekStartDay = value; }
    /// <summary>Hash-mismatch policy for script integrity checks. Settable at runtime via SET SCRIPT_HASH_POLICY.</summary>
    public string ScriptHashPolicy { get => _options.ScriptHashPolicy; set => _options.ScriptHashPolicy = value; }
    /// <summary>When true, string comparisons are case-sensitive. Defaults to false. Settable at runtime via SET CASE_SENSITIVE.</summary>
    public bool CaseSensitiveComparison { get => _options.CaseSensitiveComparison; set => _options.CaseSensitiveComparison = value; }
    public bool UseColumnarTempTables { get => _options.UseColumnarTempTables; set => _options.UseColumnarTempTables = value; }
    public bool LineageEnabled { get => _options.LineageEnabled; set => _options.LineageEnabled = value; }
    public string? LineageNamespace { get => _options.LineageNamespace; set => _options.LineageNamespace = value; }
    public string? JobName { get => _options.JobName; set => _options.JobName = value; }
    public bool LineageImportCatalog { get => _options.LineageImportCatalog; set => _options.LineageImportCatalog = value; }
    public bool TruncateString { get => _options.TruncateString; set => _options.TruncateString = value; }
    public bool SkipError { get => _options.SkipError; set => _options.SkipError = value; }

    /// <summary>Last script lexing duration in milliseconds.</summary>
    public long LastLexTimeMs { get; set; }
    /// <summary>Last script parsing duration in milliseconds.</summary>
    public long LastParseTimeMs { get; set; }
    /// <summary>Last script total execution duration in milliseconds.</summary>
    public long LastExecTimeMs { get; set; }


    /// <summary>Size of row batches used during streaming operations.</summary>
    public int BatchSize { get => _options.BatchSize; set => _options.BatchSize = value; }

    /// <summary>Number of batches held in memory before spilling to disk for #temp tables.</summary>
    public int MaxInMemoryBatches { get => _options.MaxInMemoryBatches; set => _options.MaxInMemoryBatches = value; }

    /// <summary>Maximum rows to fetch per page for remote FOREACH pushdown.</summary>
    public int ForeachPageSize { get => _options.ForeachPageSize; set => _options.ForeachPageSize = value; }

    /// <summary>
    /// Whether to output detailed execution logs for this evaluator instance.
    /// Reads the global Logger.IsVerbose as a fallback so that a process-wide verbose
    /// flag still takes effect, but setting this property does NOT mutate the global
    /// flag — avoiding race conditions when multiple Evaluators run concurrently.
    /// </summary>
    public bool IsVerbose
    {
        get => _options.IsVerbose || _logger.IsVerbose;
        set => _options.IsVerbose = value;
    }

    /// <summary>If true, Log messages are captured in the Messages list instead of direct console output.</summary>
    public bool RedirectOutput { get => _options.RedirectOutput; set => _options.RedirectOutput = value; }

    /// <summary>Limit the number of rows returned for previews.</summary>
    public int? PreviewLimit { get => _options.PreviewLimit; set => _options.PreviewLimit = value; }

    /// <summary>Preference for showing sensitive data in plain text in the UI.</summary>
    public bool ShowPassword { get => _options.ShowPassword; set => _options.ShowPassword = value; }

    /// <summary>Unsafe local-dev escape hatch allowing plaintext secrets to remain in saved source.</summary>
    public bool AllowPlaintextSecrets { get => _options.AllowPlaintextSecrets; set => _options.AllowPlaintextSecrets = value; }

    public bool NoSaveSensitive { get => _options.NoSaveSensitive; set => _options.NoSaveSensitive = value; }
    public bool NoSaveConnection { get => _options.NoSaveConnection; set => _options.NoSaveConnection = value; }
    public bool ConnectionEncryption { get => _options.ConnectionEncryption; set => _options.ConnectionEncryption = value; }

    /// <summary>Master password for decrypting connection strings.</summary>
    public string? MasterPassword { get; set; }


    /// <summary>Script-level password for encryption/decryption of sensitive data within the script.</summary>
    public string? ScriptPassword { get; set; }

    /// <summary>Event raised when a batch of rows is processed.</summary>
    public Action<long>? OnBatchProcessed { get; set; }

    /// <summary>Event raised when a new result set is produced.</summary>
    public Action<DataTable>? OnResultSet { get; set; }

    /// <summary>Event raised when a new visual is created (Interactive Mode).</summary>
    public Action<CreateVisualStatement>? OnVisualCreated { get; set; }

    /// <summary>Event raised when a diagnostic message is emitted (Interactive Mode).</summary>
    public Action<Diagnostic>? OnMessage { get; set; }

    private readonly object _lastResultSetsLock = new();
    private readonly object _messagesLock = new();

    /// <summary>The result set of the last executed query.</summary>
    public DataTable? LastResult { get; set; }

    /// <summary>Collection of all result sets produced during the last execution.</summary>
    public List<DataTable> LastResultSets { get; } = new();

    /// <summary>Named environment sets created by CREATE SETS.</summary>
    public IDictionary<string, NamedSet> NamedSets { get; } = new Dictionary<string, NamedSet>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Optional prompt callback for interactive USE SETS WITH_PROMPT. Null = non-interactive (auto-proceed).</summary>
    public Func<string, Task<bool>>? OnPrompt { get; set; }

    /// <summary>Whether to capture execution metrics for profiling.</summary>
    public bool IsJsonMode { get; set; }

    /// <summary>Whether to run in dry-run mode (no side effects).</summary>
    public bool IsWhatIf { get; set; }

    /// <summary>Whether to display a graphical execution tree during the script run.</summary>
    public bool DisplayExecuteTree { get; set; } = true;

    /// <summary>Whether to reuse execution nodes for identical statements in loops to keep the pipeline view clean.</summary>
    public bool ReuseLoopNodes { get; set; } = true;

    /// <summary>Whether the engine is in interactive mode (e.g. Notebooks/REPL).</summary>
    public bool InteractiveMode { get; set; } = false;

    public IServiceProvider ServiceProvider => _serviceProvider;

    public int JoinSpillThreshold { get => _options.JoinSpillThreshold; set => _options.JoinSpillThreshold = value; }
    public int ExternalHashPartitions { get => _options.ExternalHashPartitions; set => _options.ExternalHashPartitions = value; }
    public int ExternalSortChunkSize { get => _options.ExternalSortChunkSize; set => _options.ExternalSortChunkSize = value; }
    public int WindowSpillThreshold { get => _options.WindowSpillThreshold; set => _options.WindowSpillThreshold = value; }
    public int OperatorMemoryGrantMB { get => _options.OperatorMemoryGrantMB; set => _options.OperatorMemoryGrantMB = value; }
    public MemoryGovernorPolicy MemoryGovernorPolicy { get => _options.MemoryGovernorPolicy; set => _options.MemoryGovernorPolicy = value; }
    public bool AdaptiveExecutionEnabled
    {
        get => _options.AdaptiveExecutionEnabled;
        set
        {
            _options.AdaptiveExecutionEnabled = value;
            if (value) EnsureAdaptiveAdvisor();
            else
            {
                if (_ownsAdaptiveAdvisor)
                    _adaptiveAdvisor?.Dispose();
                _adaptiveAdvisor = null;
                _ownsAdaptiveAdvisor = false;
            }
        }
    }
    public AdaptiveAdvisor? AdaptiveAdvisor => _adaptiveAdvisor;
    public long SubquerySpillThresholdRows { get => _options.SubquerySpillThresholdRows; set => _options.SubquerySpillThresholdRows = value; }
    public bool SpillEncryptionEnabled { get => _options.SpillEncryptionEnabled; set => _options.SpillEncryptionEnabled = value; }
    public bool SpillCompressionEnabled { get => _options.SpillCompressionEnabled; set => _options.SpillCompressionEnabled = value; }
    public string SpillFormat { get => _options.SpillFormat; set => _options.SpillFormat = value; }

    public string? DecryptValue(string? val)
    {
        if (string.IsNullOrEmpty(val)) return val;
        if (!val.StartsWith("ENC:")) return val;

        // Priority: ScriptPassword > MasterPassword > MachineKey
        string? pwd = ScriptPassword ?? MasterPassword ?? ETL_SQL.Services.SecurityService.GetMachineKey();
        if (string.IsNullOrEmpty(pwd)) return val;

        return ETL_SQL.Common.CryptoUtils.Decrypt(val, pwd);
    }


    /// <summary>The ID of the currently executing node in this task/context.</summary>
    public Guid? CurrentNodeId
    {
        get => ExecutionNode.Current.Value?.Id;
        set => ExecutionNode.Current.Value = value.HasValue ? Telemetry.ExecutionTree.GetNode(value.Value) : null;
    }

    /// <summary>Interface for managing Docker database containers.</summary>
    public IDockerManager DockerManager => _dockerManager;

    /// <summary>Interface for tracking data lineage.</summary>
    public ILineageTracker LineageTracker => _lineageTracker;

    /// <summary>Manager for session persistence and cleanup.</summary>
    public ISessionStateManager SessionStateManager => _sessionStateManager;

    public ILogger Logger => _logger;
    public IDatasetRegistry? DatasetRegistry { get; set; }

    /// <summary>
    /// Registry caller-permission string (e.g. "UserId=7" or "IsAdmin=true") used to ACL-gate
    /// dataset access. Set by the portal host beside <see cref="DatasetRegistry"/>; null/empty
    /// means fail-closed (PRIVATE datasets denied, PUBLIC still allowed).
    /// </summary>
    public string? DatasetCallerContext { get; set; }

    /// <summary>
    /// Id of the report whose script is executing, used to link any datasets it CREATEs to their
    /// owning report (and, via the report, their folder for PUBLIC access checks). Set by the
    /// portal host beside <see cref="DatasetRegistry"/>; null in non-portal/standalone runs.
    /// </summary>
    public int? DatasetOwningReportId { get; set; }

    /// <summary>
    /// Portal-managed at-rest key (base64) used to encrypt/decrypt cached dataset parquet, so the
    /// cache is bound to the portal (not the host) and is portable. Set by the portal host beside
    /// <see cref="DatasetRegistry"/>; null/empty falls back to host-bound ENCRYPT=MACHINE.
    /// </summary>
    public string? DatasetAtRestKey { get; set; }

    /// <summary>
    /// Unique identifier for the current session.
    /// Setting this also stamps all subsequent log output from this Evaluator
    /// with the session ID for correlation across concurrent sessions.
    /// </summary>
    public string? SessionId
    {
        get => _sessionId;
        set
        {
            var val = value;
            if (_sessionId != null) _sessionStateManager.UnregisterActiveSession(_sessionId);
            _sessionId = val;
            if (_sessionId != null) _sessionStateManager.RegisterActiveSession(_sessionId);
            _logger.SessionId = val;
        }
    }
    private string? _sessionId = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The root directory for the current session (metadata, logs, and spills).
    /// Defaults to the standard AppData path if not explicitly provided.
    /// </summary>
    public string SessionRoot
    {
        get => _sessionRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETL-SQL", "Sessions", SessionId ?? "DEFAULT");
        set => _sessionRoot = value;
    }
    private string? _sessionRoot;


    /// <summary>Cache for scalar subquery results to avoid redundant execution.</summary>
    public ETL_SQL.Core.Common.LruCache<SubqueryCacheKey, ETL_SQL.Core.Data.SubqueryResult> SubqueryCache => _subqueryCache;

    /// <summary>Token used to cancel long-running operations in this context.</summary>
    public System.Threading.CancellationToken CancellationToken { get; private set; } = System.Threading.CancellationToken.None;

    /// <summary>Stack of row contexts for correlated subquery resolution.</summary>
    public Stack<Row> OuterRowStack => _outerRowStack;

    /// <summary>Registry of all scalar and aggregate functions available in the session.</summary>
    public Core.Functions.IFunctionRegistry FunctionRegistry => _functionRegistry;

    /// <summary>Registry for shared language help documentation.</summary>
    public Core.Interfaces.ILanguageHelpRegistry LanguageHelp => _languageHelp;



    // Consolidated Unified Constructor for DI and Sessions
    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public Evaluator(
        IEnumerable<IStatementHandler> handlers,
        IServiceProvider serviceProvider,
        Core.Functions.IFunctionRegistry functionRegistry,
        ILineageTracker lineageTracker,
        IDockerManager dockerManager,
        IConnectorRegistry connectorRegistry,
        ISessionStateManager sessionStateManager,
        SecurityService securityService,
        ILogger logger,
        Core.Interfaces.ILanguageHelpRegistry languageHelp,
        EvaluatorComponentRegistry? registry = null,
        ConcurrentDictionary<string, IDataSource>? connections = null,
        VariableScopeManager? variableScopeManager = null,
        ExecutionTree? executionTree = null,
        IReportContext? reportContext = null,
        EvaluatorOptions? options = null)
    {
        _handlers = handlers;
        _serviceProvider = serviceProvider;
        _functionRegistry = functionRegistry;
        _lineageTracker = lineageTracker;
        _dockerManager = dockerManager;
        _connectorRegistry = connectorRegistry;
        _sessionStateManager = sessionStateManager;
        _securityService = securityService;
        _logger = logger;
        _languageHelp = languageHelp;
        _bufferManager = _serviceProvider?.GetService<IBufferManager>();

        _options = options ?? new EvaluatorOptions { MaxSmtpEmailsPerScript = _securityService.MaxSmtpEmailsPerScript };
        _registry = registry ?? new EvaluatorComponentRegistry();
        _subqueryCache = new ETL_SQL.Core.Common.LruCache<SubqueryCacheKey, ETL_SQL.Core.Data.SubqueryResult>(_options.SubqueryCacheSize);
        _subqueryCache.OnEvictedAsync = async (val) =>
        {
            try { await val.DisposeAsync(); } catch { }
        };

        _variableScopeManager = variableScopeManager ?? new VariableScopeManager();
        _registry.Initialize(this, _logger, _variableScopeManager, reportContext);

        Telemetry.ExecutionTree.Clear();
        if (executionTree != null)
        {
            foreach (var node in executionTree.GetAllNodes()) Telemetry.ExecutionTree.AddNode(node);
        }
        _connections = connections ?? new ConcurrentDictionary<string, IDataSource>(StringComparer.OrdinalIgnoreCase);

        _queryCompiler = _registry.QueryCompiler;
        _metricsReporter = _registry.MetricsReporter;
        _expressionEvaluator = _registry.ExpressionEvaluator;
        _spillStore = _registry.SpillStore;
        _dataSourceManager = _registry.DataSourceManager;
        _schemaManager = _registry.SchemaManager;
        _procedureExecutor = _registry.ProcedureExecutor;
        _constraintValidator = new DataConstraintValidator(_expressionEvaluator, _connections);
        _spillCoordinator = new EvaluatorSpillCoordinator(this, _logger);

        // Link Telemetry to registry components if needed, or initialized via registry.Initialize
        Telemetry.IsProfiling = _options.IsProfiling;

        Functions.StandardFunctions.Register(functionRegistry);
        Functions.FileFunctions.Register(functionRegistry);
        Functions.LineageFunctions.Register(functionRegistry);
        Functions.RegexFunctions.Register(functionRegistry);
        Functions.JsonFunctions.Register(functionRegistry);
        Functions.XmlFunctions.Register(functionRegistry);
        Functions.FuzzyFunctions.Register(functionRegistry);
        LanguageHelpService.Initialize(languageHelp);

        foreach (var h in handlers)
        {
            _statementHandlers[h.SupportedStatementType] = h;
        }

        if (_statementHandlers.TryGetValue(typeof(SelectStatement), out var selectHandler))
        {
            _statementHandlers[typeof(SetOperationStatement)] = selectHandler;
        }

        SessionId = Guid.NewGuid().ToString("N")[..8];

        var config = _serviceProvider?.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
        InitializeThresholds(config);

        _logger.Info("Evaluator initialized.");

        // Standard OnMessage hook for capturing output into the Messages list
        // Filter by SessionId to avoid cross-pollination from background tasks
        _onMessageHandler = (msg, sid, col) =>
        {
            if (RedirectOutput && sid == SessionId)
            {
                var scrubbed = Scrub(msg);
                lock (_messagesLock)
                {
                    Messages.Add(new LogEntry(scrubbed, col, DateTime.UtcNow));
                    if (Messages.Count > MaxMessages)
                        Messages.RemoveAt(0);
                }
            }
        };
        _logger.OnMessage += _onMessageHandler;

        // Register for spill orchestration
        _bufferManager?.RegisterSpillable(this);
    }

    private void InitializeThresholds(Microsoft.Extensions.Configuration.IConfiguration? config)
    {
        if (config != null)
        {
            ReportContext.TemplatePath = config.GetValue<string>("Reporting:TemplatePath") ?? "./Templates";
        }

        MaxInMemoryBatches = DefaultThresholds.MaxInMemoryBatches(config);
        ForeachPageSize = DefaultThresholds.ForeachPageSize(config);
        JoinSpillThreshold = DefaultThresholds.JoinSpillThreshold(config);
        ExternalHashPartitions = DefaultThresholds.ExternalHashPartitions(config);
        BatchSize = DefaultThresholds.BatchSize(config);
        MaxRecursiveDepth = DefaultThresholds.MaxRecursiveDepth(config);
        ExternalSortChunkSize = DefaultThresholds.ExternalSortChunkSize(config);
        WindowSpillThreshold = DefaultThresholds.WindowSpillThreshold(config);
        OperatorMemoryGrantMB = DefaultThresholds.OperatorMemoryGrantMB(config);
        // Configure the process-wide grant pool (shared across concurrent jobs). 0 = unbounded.
        MemoryGrantArbiter.Shared.TotalBudgetBytes = (long)DefaultThresholds.TotalMemoryGrantMB(config) * 1024 * 1024;
        MemoryGovernorPolicy = DefaultThresholds.MemoryGovernorPolicy(config);
        _options.AdaptiveExecutionOptions = LoadAdaptiveOptions(config);
        TempTableSpillThresholdRows = DefaultThresholds.TempTableSpillThresholdRows(config);

        _options.BatchSize = BatchSize;
        _options.SubqueryCacheSize = DefaultThresholds.SubqueryCacheSize(config);
        _options.TempTableSpillThresholdRows = TempTableSpillThresholdRows;
        SpillEncryptionEnabled = DefaultThresholds.SpillEncryptionEnabled(config);
        SpillCompressionEnabled = DefaultThresholds.SpillCompressionEnabled(config);
        SpillFormat = DefaultThresholds.SpillFormat(config);
        MaxLastResultRows = DefaultThresholds.MaxLastResultRows(config);
        MaxMessages = config?.GetValue<int>("Engine:MaxMessages", 1000) ?? 1000;
        MaxInternalOperations = config?.GetValue<int>("Security:MaxInternalOperations", 100000) ?? 100000;
        MaxConnectionsPerScript = Math.Max(0, config?.GetValue<int>("Engine:MaxConnectionsPerScript", 100) ?? 100);
        MaxTempTablesPerScript = Math.Max(0, config?.GetValue<int>("Engine:MaxTempTablesPerScript", 100) ?? 100);
        MaxVariablesPerScript = Math.Max(0, config?.GetValue<int>("Engine:MaxVariablesPerScript", 100) ?? 100);
        MaxVisualsPerScript = Math.Max(0, config?.GetValue<int>("Engine:MaxVisualsPerScript", 100) ?? 100);
        WeekStartDay = DefaultThresholds.StartOfWeek(config);
        ScriptHashPolicy = DefaultThresholds.ScriptHashPolicy(config);
        IsPersistentSession = DefaultThresholds.PersistenceDefault(config);
        CaseSensitiveComparison = DefaultThresholds.CaseSensitiveComparison(config);
        UseColumnarTempTables = config?.GetValue("Engine:UseColumnarTempTables", true) ?? true;
        LineageEnabled = DefaultThresholds.LineageEnabled(config);
        LineageNamespace = config?.GetValue<string>("Lineage:Namespace") ?? "etl-sql";
        LineageImportCatalog = config?.GetValue<bool>("Lineage:ImportCatalogMetadata") ?? false;
        Telemetry.TelemetryEnabled = DefaultThresholds.TelemetryEnabled(config);
        AllowPlaintextSecrets = DefaultThresholds.AllowPlaintextSecrets(config);
        NoSaveSensitive = DefaultThresholds.NoSaveSensitive(config);
        NoSaveConnection = DefaultThresholds.NoSaveConnection(config);
        ConnectionEncryption = DefaultThresholds.ConnectionEncryption(config);
        AdaptiveExecutionEnabled = _options.AdaptiveExecutionOptions.Enabled;
    }

    private static AdaptiveExecutionOptions LoadAdaptiveOptions(Microsoft.Extensions.Configuration.IConfiguration? config)
    {
        var options = new AdaptiveExecutionOptions();
        if (config == null) return options;

        var section = config.GetSection("Engine:Adaptive");
        if (!section.Exists()) return options;

        return options with
        {
            Enabled = section.GetValue("Enabled", options.Enabled),
            SampleMs = section.GetValue("SampleMs", options.SampleMs),
            CpuHigh = section.GetValue("CpuHigh", options.CpuHigh),
            CpuLow = section.GetValue("CpuLow", options.CpuLow),
            MemoryHigh = section.GetValue("MemoryHigh", options.MemoryHigh),
            MemoryLow = section.GetValue("MemoryLow", options.MemoryLow),
            GrantHigh = section.GetValue("GrantHigh", options.GrantHigh),
            GrantLow = section.GetValue("GrantLow", options.GrantLow),
            MinBatchRows = section.GetValue("MinBatchRows", options.MinBatchRows),
            MaxPipelineDepth = section.GetValue("MaxPipelineDepth", options.MaxPipelineDepth),
            MinOperatorGrantRequestMB = section.GetValue("MinOperatorGrantRequestMB", options.MinOperatorGrantRequestMB)
        };
    }

    private void EnsureAdaptiveAdvisor()
    {
        if (_adaptiveAdvisor != null) return;

        _adaptiveController ??= new AdaptiveExecutionController(
            _options.AdaptiveExecutionOptions,
            MemoryArbiter.TotalBudgetBytes,
            Environment.ProcessorCount);

        _adaptiveAdvisor = _adaptiveController.CreateAdvisor(new AdaptiveExecutionCeilings(
            BatchRows: BatchSize,
            WorkerDegree: MaxParallelDegree,
            PipelineDepth: _options.AdaptiveExecutionOptions.MaxPipelineDepth,
            SpillWriteConcurrency: 1,
            OperatorGrantRequestMB: OperatorMemoryGrantMB));
        _ownsAdaptiveAdvisor = true;
    }

    private void StartAdaptiveSampler(CancellationToken cancellationToken)
    {
        if (!AdaptiveExecutionEnabled || _adaptiveAdvisor == null || _adaptiveSamplerTask != null)
            return;

        _adaptiveSampler ??= new ResourceSignalSampler(MemoryArbiter);
        _adaptiveSamplerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _adaptiveSamplerTask = RunAdaptiveSamplerAsync(_adaptiveSamplerCts.Token);
    }

    private async Task RunAdaptiveSamplerAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(50, _options.AdaptiveExecutionOptions.SampleMs)));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                _adaptiveController?.Observe(_adaptiveSampler?.Sample() ?? new ResourceSignals(0, 0, 0, 0, 0, 0));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Warning("Adaptive resource sampler stopped: " + ex.Message);
        }
    }

    private async Task StopAdaptiveSamplerAsync()
    {
        var cts = _adaptiveSamplerCts;
        var task = _adaptiveSamplerTask;
        _adaptiveSamplerCts = null;
        _adaptiveSamplerTask = null;
        if (cts == null || task == null) return;

        try
        {
            cts.Cancel();
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task AutoExportOpenLineageAsync(System.Threading.CancellationToken ct)
    {
        var config = _serviceProvider?.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
        if (config == null) return;

        // Phase 6: import database catalog metadata into lineage tags before export
        if (LineageImportCatalog)
            await ImportCatalogMetadataAsync(ct);

        var scriptName = LineageTracker.GlobalMetadata.TryGetValue("author", out var a) ? a : null;
        var sid = SessionId ?? "session";
        var jobNamespace = LineageNamespace ?? "etl-sql";

        var connectionNamespaces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in Connections)
        {
            if (kv.Value != null)
            {
                connectionNamespaces[kv.Key] = Engine.Lineage.OpenLineageExporter.ResolveConnectionNamespace(kv.Key, kv.Value);
            }
        }

        var olFile = config.GetValue<string>("Lineage:OpenLineageFile");
        if (!string.IsNullOrWhiteSpace(olFile))
        {
            var resolved = ResolvePath(olFile);
            await Engine.Lineage.OpenLineageExporter.ExportToFileAsync(
                LineageTracker, sid, scriptName, resolved, jobNamespace, connectionNamespaces, _logger, ct);
        }

        var olEndpoint = config.GetValue<string>("Lineage:OpenLineageEndpoint");
        if (!string.IsNullOrWhiteSpace(olEndpoint))
        {
            await Engine.Lineage.OpenLineageExporter.ExportToHttpAsync(
                LineageTracker, sid, scriptName, olEndpoint, jobNamespace, connectionNamespaces, _logger, ct);
        }
    }

    // Tables whose DB catalog metadata has already been imported this session.
    private HashSet<string>? _catalogImported;
    private readonly object _catalogImportGate = new();
    private readonly object _catalogRecordGate = new();

    /// <summary>
    /// When DB catalog import is enabled (off by default — see
    /// <c>Lineage:ImportCatalogMetadata</c> / <c>SET LINEAGE_IMPORT_CATALOG = ON</c>),
    /// import the given source tables' column metadata — including comments,
    /// recorded as the lineage description — before dependent lineage is
    /// recorded, so a database column comment inherits onto derived columns.
    /// Best-effort and idempotent per table per session.
    /// </summary>
    public async Task EnsureCatalogMetadataImportedAsync(IEnumerable<string> sourceTables, System.Threading.CancellationToken ct = default)
    {
        if (!LineageImportCatalog || sourceTables == null) return;
        var imports = sourceTables
            .Select(src => StartCatalogImportForTableAsync(src, ct))
            .Where(task => task != null)
            .Cast<Task>()
            .ToArray();
        if (imports.Length > 0)
            await Task.WhenAll(imports);
    }

    private async Task ImportCatalogMetadataAsync(System.Threading.CancellationToken ct)
    {
        var imports = LineageTracker.GetFullLineage()
            .ToList()
            .SelectMany(entry => entry.SourceTables)
            .Select(src => StartCatalogImportForTableAsync(src, ct))
            .Where(task => task != null)
            .Cast<Task>()
            .ToArray();
        if (imports.Length > 0)
            await Task.WhenAll(imports);
    }

    private Task? StartCatalogImportForTableAsync(string src, System.Threading.CancellationToken ct)
    {
        // Skip temp tables, report/dataset nodes, variables, already-processed
        if (string.IsNullOrEmpty(src) || src.StartsWith('#') || src.StartsWith('@') ||
            src.StartsWith("report:", StringComparison.OrdinalIgnoreCase) ||
            src.StartsWith("dataset:", StringComparison.OrdinalIgnoreCase))
            return null;

        lock (_catalogImportGate)
        {
            _catalogImported ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!_catalogImported.Add(src))
                return null;
        }

        return ImportCatalogForTableAsync(src, ct);
    }

    private async Task ImportCatalogForTableAsync(string src, System.Threading.CancellationToken ct)
    {

        // Parse: connectionAlias.schema.table  or  connectionAlias.table
        var parts = src.Split('.', 3);
        if (parts.Length < 2) return;

        var connAlias = parts[0];
        string schema, table;
        if (parts.Length == 3) { schema = parts[1]; table = parts[2]; }
        else { schema = "dbo"; table = parts[1]; }

        if (!_connections.TryGetValue(connAlias, out var ds)) return;
        var provider = ds.GetCatalogProvider();
        if (provider == null) return;

        try
        {
            var columns = await provider.GetColumnMetadataAsync(schema, table, ct);
            var rels = await provider.GetRelationshipsAsync(schema, table, ct);

            lock (_catalogRecordGate)
            {
                foreach (var col in columns)
                {
                    var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["db_type"] = col.DataType,
                        ["db_nullable"] = col.IsNullable ? "true" : "false",
                        ["db_is_pk"] = col.IsPrimaryKey ? "true" : "false",
                    };
                    // Record the DB column comment as the lineage description ("d")
                    // so it inherits onto derived columns and surfaces as the
                    // description, not merely a tag.
                    if (!string.IsNullOrEmpty(col.Description))
                        meta["d"] = col.Description!;
                    foreach (var kv in col.ExtraProperties)
                        meta[$"db_{kv.Key}"] = kv.Value;

                    LineageTracker.Record(src, Array.Empty<string>(), "DB_CATALOG",
                        targetColumn: col.ColumnName, metadata: meta);
                }

                // FK relationships → @db_referenced_by tag on the referenced table's column
                foreach (var rel in rels)
                {
                    var refMeta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["db_referenced_by"] = $"{src}.{rel.ForeignKeyColumn}"
                    };
                    LineageTracker.Record(rel.ReferencedTable, Array.Empty<string>(), "DB_CATALOG",
                        targetColumn: rel.ReferencedColumn, metadata: refMeta);
                }
            }

            // Attempt view/procedure definition expansion (best-effort, inside outer try)
            if (provider is IViewDefinitionProvider vdp)
            {
                try
                {
                    var def = await vdp.GetViewDefinitionAsync(schema, table, ct);
                    if (!string.IsNullOrWhiteSpace(def))
                        await ExpandViewLineageAsync(src, def, ct);
                }
                catch { /* unparseable DDL or unsupported object — silently skip */ }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("Catalog import failed for {Table}: {Message}", src, ex.Message);
        }
    }

    private async Task ExpandViewLineageAsync(string viewQualifiedName, string viewDdl, System.Threading.CancellationToken ct)
    {
        // Guard: only expand each view once to prevent infinite recursion through nested views
        _expandedViews ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!_expandedViews.Add(viewQualifiedName)) return;

        try
        {
            var tokens = new Lexer(viewDdl).Tokenize();
            var viewScript = new ETL_SQL.Core.Parser.Parser(tokens, viewDdl).Parse();
            var viewTracker = new LineageTracker(_logger);
            new LineageAnalyzer(viewTracker).Analyze(viewScript);

            foreach (var entry in viewTracker.GetFullLineage())
            {
                // Re-record: target is the view name (so downstream consumers see the true upstream)
                lock (_catalogRecordGate)
                {
                    LineageTracker.Record(
                        viewQualifiedName,
                        entry.SourceTables,
                        "VIEW_EXPAND",
                        targetColumn: entry.TargetColumn,
                        sourceColumns: entry.SourceColumns,
                        line: entry.Line);
                }
            }
        }
        catch { /* unparseable DDL — silently skip */ }
    }

    private HashSet<string>? _expandedViews;

    /// <summary>
    /// At execution start, clamp the governed resource thresholds that have no runtime ceiling down
    /// to the enterprise maximum. Their initial values may have arrived via configuration,
    /// environment variables, command-line options, a restored saved session, or report parameters —
    /// override sources that never pass through the <c>SET</c> ceiling. Clamping here (rather than at
    /// each override site) makes a locked value impossible to weaken by any path, and is
    /// deterministic in-process and across spawned processes because every host captures the snapshot
    /// at this same boundary.
    ///
    /// Only <c>MaxParallelDegree</c> (consumed directly by the parallel handler) and
    /// <c>MaxStringResultSize</c> lack a runtime enterprise ceiling and so must be clamped here.
    /// <c>MaxFileOperationsPerScript</c>, <c>MaxRecursiveNestingDepth</c>, and
    /// <c>MaxSmtpEmailsPerScript</c> are re-checked against the governed ceiling at each operation
    /// (see <c>OperationPolicyBoundary.EnforceCeiling</c> call sites), so a high initial value is
    /// already denied at runtime with the deterministic policy message — lowering their local limit
    /// here would only mask that enterprise denial behind the local guardrail's message.
    /// </summary>
    private void BindGovernedThresholdCeilings()
    {
        if (ExecutionPolicy is not { IsEnrolled: true } snapshot) return;
        MaxParallelDegree = (int)Core.Governance.OperationPolicyBoundary.ClampToGovernedCeiling(
            snapshot, "Security:MaxParallelDegree", MaxParallelDegree);
        MaxStringResultSize = Core.Governance.OperationPolicyBoundary.ClampToGovernedCeiling(
            snapshot, "Security:MaxStringResultSize", MaxStringResultSize);
    }

    public async Task Evaluate(Script script, System.Threading.CancellationToken cancellationToken = default)
    {
        if (CurrentRecursiveDepth == 0)
        {
            var actor = script.Metadata.TryGetValue("author", out var author)
                && !string.IsNullOrWhiteSpace(author) ? author : Environment.UserName;
            var mode = InteractiveMode
                ? ScriptExecutionMode.Interactive
                : string.IsNullOrWhiteSpace(JobName)
                    ? ScriptExecutionMode.Batch
                    : ScriptExecutionMode.Scheduled;
            var canonicalScript = string.Join("\n", script.Statements.Select(statement => statement.ToSql()));
            var scriptHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonicalScript))).ToLowerInvariant();
            ExecutionPolicy = ExecutionPolicySnapshot.Capture(
                EnterprisePolicyRuntime.Current, actor, mode, scriptHash, JobName);
            // Enterprise execution-mode gates — applied before any statement executes.
            Core.Governance.OperationPolicyBoundary.EnforceAllowedExecutionMode(ExecutionPolicy);
            Core.Governance.OperationPolicyBoundary.EnforceRemoteExecutionMode(ExecutionPolicy);
            BindGovernedThresholdCeilings();

            foreach (var rs in LastResultSets) rs.Clear();
            LastResultSets.Clear();
            ClearResults();
            _nodeReuseMap.Clear();
            _operationCount = 0;
            _smtpEmailsSentThisScript = 0;
            // Lineage and Telemetry are session-persistent; clearing handled by ResetSessionAsync
            _expressionEvaluator.ClearCaches();
            await _subqueryCache.ClearAsync();
            foreach (var src in _localSources.Values)
                try { await src.DisposeAsync(); } catch { }
            _localSources.Clear();

            if (!InteractiveMode)
            {
                lock (_messagesLock) { Messages.Clear(); }
            }
        }
        try
        {
            if (LineageEnabled)
            {
                var analyzer = new LineageAnalyzer(LineageTracker);
                analyzer.Analyze(script);
            }

            if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var firstError = script.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
                throw new ExecutionException($"Syntax error: {firstError.Message} at {firstError.Line}:{firstError.Column}");
            }

            ExecutionNode? scriptNode = null;
            if (Telemetry.TelemetryEnabled)
            {
                scriptNode = new ExecutionNode
                {
                    Name = "Script Execution",
                    Status = ExecutionStatus.Running,
                    StartTicks = Stopwatch.GetTimestamp()
                };
                Telemetry.ExecutionTree.AddNode(scriptNode);
                CurrentNodeId = scriptNode.Id;
            }

            // Inject script metadata into LineageTracker
            LineageTracker.GlobalMetadata.Clear();
            foreach (var kv in script.Metadata)
            {
                LineageTracker.GlobalMetadata[kv.Key] = kv.Value;
            }
            if (!LineageTracker.GlobalMetadata.ContainsKey("author"))
            {
                LineageTracker.GlobalMetadata["author"] = Environment.UserName;
            }
            if (!LineageTracker.GlobalMetadata.ContainsKey("engine_version"))
            {
                LineageTracker.GlobalMetadata["engine_version"] = LanguageMetadata.EngineVersion;
            }

            CancellationToken = cancellationToken;
            if (CurrentRecursiveDepth == 0)
                StartAdaptiveSampler(cancellationToken);

            if (IsResuming && VarContext.ContainsVariable("@_LAST_CHECKPOINT_LABEL"))
            {
                var labelVal = VarContext.GetVariable("@_LAST_CHECKPOINT_LABEL")?.ToString();
                if (!string.IsNullOrEmpty(labelVal))
                {
                    ResumeLabel = labelVal;
                    // Verify that the label actually exists in the script as a top-level label
                    bool labelExists = script.Statements.Any(s => s is SectionLabelStatement l && l.LabelName.Equals(ResumeLabel, StringComparison.OrdinalIgnoreCase));
                    if (!labelExists)
                    {
                        throw new ExecutionException($"Cannot resume execution: checkpoint label '{ResumeLabel}' is not defined in the script.");
                    }
                    Log($"Resuming execution from checkpoint '{ResumeLabel}'...", ConsoleColor.Cyan);
                }
                else
                {
                    throw new ExecutionException("--resume was specified but the saved session contains no checkpoint label. The job may not have reached a checkpoint yet. Run without --resume to start fresh.");
                }
            }
            else if (IsResuming)
            {
                throw new ExecutionException("--resume was specified but the saved session contains no checkpoint label. The job may not have reached a checkpoint yet. Run without --resume to start fresh.");
            }

            // Split into batches at GO boundaries.
            // Each batch runs independently — a failed batch is logged and skipped; later batches still execute.
            var batches = SplitIntoBatches(script.Statements);
            bool hasBatches = batches.Count > 1;
            int batchNum = 0;

            foreach (var batch in batches)
            {
                batchNum++;
                if (batch.Count == 0) continue;

                // Build a label -> index map once upfront so that GOTO resolution is O(1)
                // instead of O(n) per throw. Only built when the batch actually contains labels.
                Dictionary<string, int>? labelIndex = null;
                for (int k = 0; k < batch.Count; k++)
                {
                    if (batch[k] is SectionLabelStatement lbl)
                    {
                        labelIndex ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        labelIndex[lbl.LabelName] = k;
                    }
                }

                try
                {
                    for (int i = 0; i < batch.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var statement = batch[i];

                        if (IsResuming)
                        {
                            if (statement is SectionLabelStatement label && label.LabelName.Equals(ResumeLabel, StringComparison.OrdinalIgnoreCase))
                            {
                                IsResuming = false;
                                Log($"Resuming execution from checkpoint label '{label.LabelName}'...", ConsoleColor.Cyan);
                                // Execute the label to re-trigger/verify checkpoint saving
                            }
                            else
                            {
                                continue;
                            }
                        }

                        try
                        {
                            await EvaluateStatement(statement, cancellationToken);
                        }
                        catch (GotoException gotoEx)
                        {
                            if (labelIndex != null && labelIndex.TryGetValue(gotoEx.LabelName, out int targetIdx))
                            {
                                i = targetIdx - 1; // -1 because loop increment will do i++
                                _logger.Debug("GOTO redirecting execution to label '{LabelName}'", gotoEx.LabelName);
                                continue;
                            }
                            else
                            {
                                throw;
                            }
                        }
                    }
                    if (hasBatches) Log($"Batch {batchNum} completed.", ConsoleColor.DarkGray);
                }
                catch (OperationCanceledException) { throw; }
                catch (ReturnException) { throw; }
                catch (GotoException) { throw; }
                catch (Exception ex) when (hasBatches)
                {
                    Log($"Batch {batchNum} failed: {ex.Message}", ConsoleColor.Red);
                }
            }

            if (scriptNode != null)
            {
                scriptNode.Status = ExecutionStatus.Completed;
                scriptNode.EndTicks = Stopwatch.GetTimestamp();
            }

            // Auto-export OpenLineage at top-level script completion
            if (CurrentRecursiveDepth == 0)
            {
                await AutoExportOpenLineageAsync(cancellationToken);
                await CommitPendingJobStateAsync();
            }
        }
        catch (ReturnException ex)
        {
            if (ex.Value != null) Spectre.Console.AnsiConsole.MarkupLine($"[cyan][[RETURN]][/] {Spectre.Console.Markup.Escape(ex.Value?.ToString() ?? "")}");
            else Spectre.Console.AnsiConsole.MarkupLine("[cyan][[RETURN]][/]");
        }
        finally
        {
            if (CurrentRecursiveDepth == 0)
            {
                await StopAdaptiveSamplerAsync();
                _subqueryCache.Clear();
            }
            _variableScopeManager.PurgeSecretVariables();
            if (TranCount > 0 && AutoRollbackOnFinish)
            {
                _logger.Warning("Script execution ended with {Count} open transactions. Performing emergency rollback.", TranCount);
                await RollbackAllTransactions();
            }
        }
    }

    public void ClearResults()
    {
        LastResult?.Clear();
        LastResult = null;
    }

    private async Task CommitPendingJobStateAsync()
    {
        if (PendingJobStateUpdates.Count == 0) return;

        if (!string.IsNullOrEmpty(JobName))
        {
            var store = ServiceProvider.GetService(typeof(Core.Data.IJobHistoryStore)) as Core.Data.IJobHistoryStore;
            if (store != null)
            {
                foreach (var kv in PendingJobStateUpdates)
                {
                    await store.SetJobStateAsync(JobName, kv.Key, kv.Value);
                }
            }
        }
        else
        {
            CommitLocalJobState();
        }
        PendingJobStateUpdates.Clear();
    }

    private void CommitLocalJobState()
    {
        if (string.IsNullOrEmpty(CurrentScriptPath)) return;
        try
        {
            var stateFile = System.IO.Path.ChangeExtension(CurrentScriptPath, ".etlstate");
            var scriptDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(CurrentScriptPath));
            if (string.IsNullOrEmpty(scriptDir) || !SafePath.IsWithinRoot(scriptDir, stateFile))
                throw new Core.Common.Exceptions.ExecutionException("Refusing to write local job state outside the current script directory.");
            SecurityService.ValidateWriteAccess(stateFile);

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (System.IO.File.Exists(stateFile))
            {
                var text = System.IO.File.ReadAllText(stateFile);
                var existing = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(text);
                if (existing != null)
                {
                    foreach (var kv in existing)
                        dict[kv.Key] = kv.Value;
                }
            }

            foreach (var kv in PendingJobStateUpdates)
            {
                dict[kv.Key] = kv.Value;
            }

            var json = System.Text.Json.JsonSerializer.Serialize(dict, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(stateFile, json);
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to commit local job state: " + ex.Message);
        }
    }

    public (Dictionary<string, object?>, Dictionary<string, VariableMetadata>) GetGlobalState() => _variableScopeManager.GetGlobalState();

    private static List<List<Statement>> SplitIntoBatches(List<Statement> statements)
    {
        var batches = new List<List<Statement>>();
        var current = new List<Statement>();
        foreach (var stmt in statements)
        {
            if (stmt is GoStatement go)
            {
                for (int i = 0; i < go.Count; i++)
                    batches.Add(new List<Statement>(current));
                current = new List<Statement>();
            }
            else
            {
                current.Add(stmt);
            }
        }
        batches.Add(current);
        return batches;
    }

    public async Task LoadSessionState(SessionState state)
    {
        _variableScopeManager.LoadGlobalState(state.GlobalVariables, state.GlobalMetadata);
        DockerManager.LoadState(state.DockerConnectionStrings, state.LastDockerConnectionString);

        var connectionAuthorizer = new ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer(_securityService);
        foreach (var conn in state.Connections)
        {
            var connector = _connectorRegistry.GetConnector(conn.Type);
            if (connector == null) continue;

            // A saved connection must satisfy CURRENT organization policy before it is restored —
            // otherwise a connection saved under a looser policy could be reused to reach a now-denied
            // connector type or destination. A denial drops the connection rather than aborting the
            // whole session restore.
            try
            {
                connectionAuthorizer.Authorize(this, conn.Type,
                    connector.GetHost(conn.ConnectionString, conn.Options), conn.ConnectionString);
            }
            catch (ETL_SQL.Core.Governance.ConnectorPolicyDeniedException ex)
            {
                _logger.Warning("Saved connection '{Name}' was not restored: {Reason}", conn.Name, ex.Decision.Reason);
                continue;
            }

            _connections[conn.Name] = connector.CreateDataSource(this, conn.ConnectionString, conn.Options);
        }

        LineageTracker.Clear();
        LineageTracker.LoadState(state.LineageEntries);

        foreach (var temp in state.TempTables)
        {
            _connections[temp.Name] = await _dataSourceManager.RestoreTempTable(temp, ScriptPassword ?? ETL_SQL.Services.SecurityService.GetMachineKey());
        }
    }

    public async Task EvaluateStatement(Statement statement, CancellationToken cancellationToken = default)
    {
        CancellationToken = cancellationToken;
        // NOTE: ThrowIfCancellationRequested() is intentionally NOT called here for the hot-loop path.
        // The outer batch loop (Evaluate) already checks before dispatching each statement.
        // Callers that need an extra check (e.g., deeply nested recursive calls) may call it
        // themselves. Removing the duplicate saves one volatile read per statement.

        // Update PreviousErrorNumber and reset LastError for the new statement
        // We skip this for internal/structural nodes that don't count as "atomic" statements for @@ERROR purposes
        if (statement is not NoOpStatement && statement is not BlockStatement)
        {
            PreviousErrorNumber = LastError?.Number ?? 0;
            LastError = null;
        }

        var parentId = CurrentNodeId;

        ExecutionNode? node = null;

        if (Telemetry.TelemetryEnabled)
        {
            // Build the node name only when telemetry is active — avoids a string alloc
            // and 4 type-pattern checks on every statement dispatch in the common case.
            var nodeName = statement.GetType().Name.Replace("Statement", "");
            if (statement is UsePasswordStatement) nodeName = "USE PASSWORD";
            else if (statement is UseSetsStatement us) nodeName = $"USE SETS {us.Name}";
            else if (statement is CreateTableStatement cts) nodeName = $"CREATE TABLE {cts.TargetTable.TableName}";
            else if (statement is InsertStatement inst) nodeName = $"INSERT INTO {inst.TargetTable.TableName}";

            var cacheKey = new NodeReuseKey(parentId, statement);

            if (ReuseLoopNodes && _nodeReuseMap.TryGetValue(cacheKey, out var existingNode))
            {
                node = existingNode;
                node.Status = ExecutionStatus.Running;
                node.IterationCount++;
                node.StartTicks = Stopwatch.GetTimestamp();
                node.ErrorMessage = null;
            }
            else
            {
                node = new ExecutionNode
                {
                    Name = nodeName,
                    Status = ExecutionStatus.Running,
                    StartTicks = Stopwatch.GetTimestamp()
                };
                Telemetry.ExecutionTree.AddNode(node, parentId);
                if (ReuseLoopNodes) _nodeReuseMap[cacheKey] = node;
            }
            parentId = node.Id;
            CurrentNodeId = node.Id;
        }

        Stopwatch? sw = null;
        long startRows = 0;
        if (IsVerbose || Telemetry.IsProfiling)
        {
            startRows = Telemetry.RowsProcessed;
            sw = Stopwatch.StartNew();
            if (IsVerbose)
            {
                string sql = (statement is UsePasswordStatement ups) ? ups.ToSql(!ShowPassword) : statement.ToSql();
                _logger.Debug("Executing {Sql}", Scrub(sql));
            }
            _metricsReporter.ReportPreExecutionMetrics(statement);
        }

        if (statement is NoOpStatement)
        {
            if (node != null) node.Status = ExecutionStatus.Completed;
            return;
        }

        // Enterprise mutation guardrails (require-what-if / require-transaction) — no-op unless
        // enrolled and the statement mutates a persistent (non-#temp) target.
        Governance.MutationGuardrailPolicy.Enforce(this, statement);

        if (_statementHandlers.TryGetValue(statement.GetType(), out var handler))
        {
            try
            {
                await handler.Execute(statement, this);
                if (node != null) node.Status = ExecutionStatus.Completed;
            }
            catch (Exception ex)
            {
                if (node != null)
                {
                    node.Status = ExecutionStatus.Faulted;
                    node.ErrorMessage = ex.Message;
                }
                LastError = new ErrorInfo(50000, ex.Message, 16, 1, statement.Line, null);
                throw;
            }
            finally
            {
                if (node != null) node.EndTicks = Stopwatch.GetTimestamp();
                CurrentNodeId = parentId;
                // statement boundaries (essential for CREATE VISUAL).
            }
        }
        else
        {
            throw new ExecutionException($"No handler registered for {statement.GetType().Name} at Line {statement.Line}");
        }

        if (sw != null)
        {
            sw.Stop();
            var elapsed = sw.ElapsedMilliseconds;
            Telemetry.LastStatementRowsProcessed = Telemetry.RowsProcessed - startRows;
            Telemetry.LastExecutionTimeMs = elapsed;
            _metricsReporter.ReportPostExecutionMetrics(statement, elapsed);
            if (IsVerbose) _metricsReporter.ProvideTips(statement);
            LastIndexUsedName = null;
        }
    }

    public Task<IDataSource> ResolveDataSourceAsync(TableReference table) => _dataSourceManager.ResolveDataSourceAsync(table, _connections, _transactionManager);
    public IAsyncEnumerable<DataTable> ResolveAndApplyOperators(TableReference table) => _dataSourceManager.ResolveAndApplyOperators(table, _connections, _transactionManager, BatchSize);



    public async IAsyncEnumerable<DataTable> ExecuteQuery(Statement stmt)
    {
        if (stmt is ExplainStatement explain) yield return await EvaluateExplain(explain);
        else
        {
            var handler = (SelectStatementHandler)_statementHandlers[typeof(SelectStatement)];
            await foreach (var b in handler.EvaluateQuery(stmt, this)) yield return b;
        }
    }

    public IAsyncEnumerable<DataTable> EvaluateSelect(SelectStatement stmt)
    {
        var handler = (SelectStatementHandler)_statementHandlers[typeof(SelectStatement)];
        return handler.EvaluateSelect(stmt, this);
    }

    internal async Task<DataTable> EvaluateExplain(ExplainStatement stmt)
    {
        var handler = (Handlers.ExplainStatementHandler)_statementHandlers[typeof(ExplainStatement)];
        await handler.Execute(stmt, this);
        return LastResult!;
    }



    public IAsyncEnumerable<DataTable> InterceptProgress(IAsyncEnumerable<DataTable> chunks)
    {
        return _batchPipelineHelper.InterceptProgress(chunks, count =>
        {
            Telemetry.RowsProcessed += count;
            OnBatchProcessed?.Invoke(count);
        });
    }

    public IAsyncEnumerable<DataTable> AlignColumns(IAsyncEnumerable<DataTable> batches, List<string> targetCols)
        => _batchPipelineHelper.AlignColumns(batches, targetCols);

    public ForClause? GetForClause(Statement stmt)
    {
        if (stmt is SelectStatement sel) return sel.ForClause;
        if (stmt is SetOperationStatement setOp) return GetForClause(setOp.Right);
        return null;
    }

    public IAsyncEnumerable<DataTable> EvaluateForClause(IAsyncEnumerable<DataTable> batches, ForClause forClause)
        => _batchPipelineHelper.EvaluateForClause(batches, forClause);



    public async Task EvaluateDelete(DeleteStatement stmt)
    {
        var handler = _statementHandlers[typeof(DeleteStatement)];
        await handler.Execute(stmt, this);
    }

    public async Task EvaluateUpdate(UpdateStatement stmt)
    {
        var handler = _statementHandlers[typeof(UpdateStatement)];
        await handler.Execute(stmt, this);
    }



    public TableReference? GetIntoTable(Statement stmt)
    {
        if (stmt is SelectStatement s) return s.IntoTable;
        if (stmt is SetOperationStatement setOp) return GetIntoTable(setOp.Right) ?? GetIntoTable(setOp.Left);
        return null;
    }

    public async Task BeginTransaction() => await _transactionManager.BeginTransaction(_variableScopeManager.Variables, _connections);
    public async Task CommitTransaction() => await _transactionManager.CommitTransaction();
    public async Task RollbackTransaction(string? name = null) => await _transactionManager.RollbackTransaction(_variableScopeManager.Variables, _connections);
    public async Task RollbackAllTransactions() => await _transactionManager.RollbackAll(_variableScopeManager.Variables, _connections);
    internal void ReplaceDataSourceForTransaction(string connectionName, IDataSource original, IDataSource replacement)
        => _transactionManager.ReplaceDataSource(connectionName, original, replacement, _connections);



    public object? ResolveIdentifier(string name, Row? row)
    {
        // 1. Try current row
        if (row != null && row.TryGetValue(name, out var val)) return val;

        // 2. Try outer row stack (for correlated subqueries)
        foreach (var outer in _outerRowStack)
        {
            if (outer != null && outer.TryGetValue(name, out var oval)) return oval;
        }

        // 3. Try variables
        return _variableScopeManager.ResolveIdentifier(name, null);
    }

    public async ValueTask DisposeAsync()
    {
        if (_onMessageHandler != null)
        {
            _logger.OnMessage -= _onMessageHandler;
            _onMessageHandler = null;
        }

        if (_sessionId != null) _sessionStateManager.UnregisterActiveSession(_sessionId);
        await StopAdaptiveSamplerAsync();

        // Reclaim any 'Zombie' resource reservations (Reference Counting protection)
        if (!string.IsNullOrEmpty(SessionId))
        {
            _bufferManager?.ReleaseAllForSession(SessionId);
        }

        _spillStore?.Dispose();
        foreach (var conn in _connections.Values) await conn.DisposeAsync();
        await DockerManager.DisposeAsync();
        _connections.Clear();
        foreach (var src in _localSources.Values)
            try { await src.DisposeAsync(); } catch { }
        _localSources.Clear();
        if (_ownsAdaptiveAdvisor)
            _adaptiveAdvisor?.Dispose();
        _adaptiveAdvisor = null;
        _ownsAdaptiveAdvisor = false;
    }



    public IExecutionContext Fork()
    {
        var freshHandlers = _serviceProvider.GetServices<IStatementHandler>();
        var clonedReportContext = (ReportContext as ReportRegistry)?.Clone() ?? ReportContext;
        var fork = new Evaluator(freshHandlers, _serviceProvider, _functionRegistry, _lineageTracker, _dockerManager, _connectorRegistry, _sessionStateManager, _securityService, _logger, LanguageHelp, new EvaluatorComponentRegistry(), _connections, _variableScopeManager.Fork(), Telemetry.ExecutionTree, clonedReportContext)
        {
            IsVerbose = IsVerbose,
            RedirectOutput = RedirectOutput,
            IsWhatIf = IsWhatIf,
            ShowPassword = ShowPassword,
            AllowPlaintextSecrets = AllowPlaintextSecrets,
            NoSaveSensitive = NoSaveSensitive,
            NoSaveConnection = NoSaveConnection,
            ConnectionEncryption = ConnectionEncryption,
            BatchSize = BatchSize,
            PreviewLimit = PreviewLimit,
            ScriptPassword = ScriptPassword,
            SessionId = SessionId,
            DisplayExecuteTree = DisplayExecuteTree,
            MaxGroupingSets = MaxGroupingSets,
            ExecutionPolicy = ExecutionPolicy,
            ExecutionIdentity = ExecutionIdentity,
            MemoryArbiter = MemoryArbiter,
            _adaptiveController = _adaptiveController,
            _adaptiveAdvisor = _adaptiveAdvisor,
            _ownsAdaptiveAdvisor = false
        };
        fork._options.AdaptiveExecutionEnabled = AdaptiveExecutionEnabled;
        fork._options.AdaptiveExecutionOptions = _options.AdaptiveExecutionOptions;

        fork.Telemetry.IsProfiling = Telemetry.IsProfiling;

        // Note: CurrentNodeId is AsyncLocal and will automatically flow to the new thread if Task.Run is used,
        // but for a manual Fork we set it explicitly.
        fork.CurrentNodeId = CurrentNodeId;
        return fork;
    }

    public void Merge(IExecutionContext spawned)
    {
        if (spawned is Evaluator eval) _variableScopeManager.Merge(eval._variableScopeManager);
        lock (_lastResultSetsLock)
        {
            LastResultSets.AddRange(spawned.LastResultSets);
            if (spawned.LastResult != null) LastResult = spawned.LastResult;
        }
        lock (_messagesLock) foreach (var entry in spawned.Messages) Log(entry.Message, entry.Color);

        Telemetry.RowsProcessed += spawned.Telemetry.RowsProcessed;
        Telemetry.TotalSpilledBytes += spawned.Telemetry.TotalSpilledBytes;
    }

    private int _operationCount = 0;
    public void IncrementOperationCount(OperationType type = OperationType.FileSystem, string? path = null, int count = 1)
    {
        var total = System.Threading.Interlocked.Add(ref _operationCount, count);
        _securityService.CheckRunawayProtection(type, total, CurrentRecursiveDepth, AllowLargeFileOperationCount, AllowDeepRecursion, path);
        // Enterprise ceiling binds regardless of the local Allow*/safe-zone overrides above.
        if (type == OperationType.FileSystem && IsEnterpriseGoverned)
            OperationPolicyBoundary.EnforceCeiling(this, "Security:MaxFileOperationsPerScript",
                total, "<file-operation-count>");
    }

    private bool IsEnterpriseGoverned =>
        ExecutionPolicy?.IsEnrolled ?? EnterprisePolicyRuntime.Current.IsEnrolled;

    public IDisposable EnterRecursiveScope()
    {
        CurrentRecursiveDepth++;
        _securityService.CheckRunawayProtection(OperationType.FileSystem, _operationCount, CurrentRecursiveDepth, AllowLargeFileOperationCount, AllowDeepRecursion, null);
        // Enterprise ceiling binds regardless of the local Allow*/safe-zone overrides above.
        if (IsEnterpriseGoverned)
            OperationPolicyBoundary.EnforceCeiling(this, "Security:MaxRecursiveNestingDepth",
                CurrentRecursiveDepth, "<recursive-nesting-depth>");
        return new RecursiveScope(this);
    }

    private class RecursiveScope : IDisposable
    {
        private readonly Evaluator _evaluator;
        public RecursiveScope(Evaluator evaluator) => _evaluator = evaluator;
        public void Dispose() => _evaluator.CurrentRecursiveDepth--;
    }

    ETL_SQL.Services.SecurityService IExecutionContext.SecurityService => _securityService;
    /// <summary>Resets the entire session (variables, temp tables, results, transactions, lineage, report definitions) to a clean state.</summary>
    public async Task ResetSessionAsync()
    {
        _logger.Debug("[Evaluator] Resetting session state...");

        // 1. Rollback any active transactions
        if (_transactionManager.TranCount > 0)
        {
            _logger.Warning("[Evaluator] Rolling back {TranCount} dangling transactions during session reset.", _transactionManager.TranCount);
            await _transactionManager.RollbackAll(_variableScopeManager.Variables, _connections);
        }

        // 2. Reset Variables, Procedures, and Functions
        _variableScopeManager.Reset();

        // 3. Dispose and Clear Temp Tables (LocalSources) and Global Connections
        foreach (var conn in _connections.Values)
        {
            try { await conn.DisposeAsync(); }
            catch (Exception ex) { _logger.Debug("Error disposing connection: {Msg}", ex.Message); }
        }
        _connections.Clear();

        foreach (var src in _localSources.Values)
        {
            try { await src.DisposeAsync(); }
            catch (Exception ex) { _logger.Debug("Error disposing local source: {Msg}", ex.Message); }
        }
        _localSources.Clear();

        // 4. Clear Report/UI Definitions
        ReportContext.Clear();

        // 5. Clear Results and Telemetry
        ClearResults();
        LastResultSets.Clear();
        Telemetry.Clear();
        _subqueryCache.Clear();

        // 6. Clear Lineage
        _lineageTracker.Clear();

        // 7. Reset state indicators
        // SessionId = Guid.NewGuid().ToString("N"); // Preserve session ID identity during reset
        LastError = null;
        ActiveException = null;
        PreviousErrorNumber = 0;
        Telemetry.FetchStatus = 0;
        lock (_messagesLock) Messages.Clear();
        Parameters?.Clear();

        _logger.Debug("[Evaluator] Session reset complete.");
    }
    /// <summary>
    /// Struct key for _nodeReuseMap. Avoids boxing the nullable Guid that occurs with a
    /// value-tuple containing Guid? as a dictionary key on every hot-loop statement dispatch.
    /// </summary>
    private readonly struct NodeReuseKey : IEquatable<NodeReuseKey>
    {
        private readonly Guid _parentId;
        private readonly bool _hasParent;
        private readonly int _stmtHash;

        public NodeReuseKey(Guid? parentId, Statement stmt)
        {
            _hasParent = parentId.HasValue;
            _parentId = parentId.GetValueOrDefault();
            _stmtHash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(stmt);
        }

        public bool Equals(NodeReuseKey other)
            => _hasParent == other._hasParent && _parentId == other._parentId && _stmtHash == other._stmtHash;

        public override bool Equals(object? obj) => obj is NodeReuseKey k && Equals(k);

        public override int GetHashCode() => HashCode.Combine(_hasParent, _parentId, _stmtHash);
    }
}
