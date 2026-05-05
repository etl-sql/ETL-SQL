using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;

using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Engine.Handlers;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine.Services;
using ETL_SQL.Core.Data;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;
using ETL_SQL.Core.Spill;
using ETL_SQL.Core.Execution;

namespace ETL_SQL.Engine
{
    // Using ExecutionException from ETL_SQL.Core.Common.Exceptions

    public class BreakException : Exception { }
    public class ContinueException : Exception { }

    public class ReturnException : Exception
    {
        public object? Value { get; }
        public ReturnException(object? value) { Value = value; }
    }

    /// <summary>
    /// The primary execution engine for ETL-SQL scripts.
    /// Coordinates connections, variables, statement handlers, and expression evaluation.
    /// </summary>
    public class Evaluator : IExecutionContext, IAsyncDisposable, IDataValidator, ISpillable
    {
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

        public IDictionary<string, IDataSource> Connections => _connections;
        public IDictionary<string, IDataSource> LocalSources => _localSources;

        private readonly VariableScopeManager _variableScopeManager;
        private readonly EvaluatorComponentRegistry _registry;
        private readonly QueryCompiler _queryCompiler;
        private readonly ExecutionMetricsReporter _metricsReporter;
        private readonly DataSourceManager _dataSourceManager;
        private readonly SchemaManager _schemaManager;
        private readonly ExpressionEvaluator _expressionEvaluator;
        private readonly ProcedureExecutor _procedureExecutor;
        private readonly BatchPipelineHelper _batchPipelineHelper = new();
        private readonly Dictionary<Type, IStatementHandler> _statementHandlers = new();

        private readonly Stack<Row> _outerRowStack = new();
        private readonly ETL_SQL.Core.Common.LruCache<SubqueryCacheKey, ETL_SQL.Core.Data.SubqueryResult> _subqueryCache;
        private readonly Dictionary<(Guid? ParentId, Statement Stmt), ExecutionNode> _nodeReuseMap = new();
        private readonly TransactionManager _transactionManager = new();
        private readonly ISpillStore _spillStore;
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
        
        [System.Obsolete("Use Telemetry.IsProfiling")]
        public bool IsProfiling { get => Telemetry.IsProfiling; set => Telemetry.IsProfiling = value; }
        
        [System.Obsolete("Use Telemetry.RowsProcessed")]
        public long RowsProcessed => Telemetry.RowsProcessed;
        
        [System.Obsolete("Use Telemetry.PartitionsCount")]
        public int PartitionsCount => Telemetry.PartitionsCount;
        
        [System.Obsolete("Use Telemetry.TotalSpilledBytes")]
        public long TotalSpilledBytes => Telemetry.TotalSpilledBytes;

        [System.Obsolete("Use Telemetry.AggregateExpansionRatio")]
        public double AggregateExpansionRatio => Telemetry.AggregateExpansionRatio;

        [System.Obsolete("Use Telemetry.TelemetryEnabled")]
        public bool TelemetryEnabled { get => Telemetry.TelemetryEnabled; set => Telemetry.TelemetryEnabled = value; }

        [System.Obsolete("Use Telemetry.AggregateGroupsCount")]
        public long AggregateGroupsCount => Telemetry.AggregateGroupsCount;
        
        [System.Obsolete("Use Telemetry.ProfileMetrics")]
        public List<ExecutionMetrics> ProfileMetrics => Telemetry.ProfileMetrics;
        
        public List<LogEntry> Messages { get; } = new();
        public int MaxMessages { get; set; } = 1000;
        
        public IVariableContext VarContext => _variableScopeManager;
        public IReportContext ReportContext => _registry.ReportContext;
        public ITelemetryContext Telemetry => _registry.TelemetryManager;

        public long TempTableSpillThresholdRows { get => _options.TempTableSpillThresholdRows; set => _options.TempTableSpillThresholdRows = value; }
        public int MaxRecursiveDepth { get => _options.MaxRecursiveDepth; set => _options.MaxRecursiveDepth = value; }
        public int CurrentRecursiveDepth { get; set; } = 0;
        public string? LastIndexUsedName { get; set; }
        public ErrorInfo? LastError { get; set; }
        public ErrorInfo? ActiveException { get; set; }
        public int PreviousErrorNumber { get; set; } = 0;
        
        public bool AllowUnknownFileTypes { get => _options.AllowUnknownFileTypes; set => _options.AllowUnknownFileTypes = value; }
        public bool AllowLargeFileOperationCount { get => _options.AllowLargeFileOperationCount; set => _options.AllowLargeFileOperationCount = value; }
        public bool AllowDeepRecursion { get => _options.AllowDeepRecursion; set => _options.AllowDeepRecursion = value; }
        public bool AllowLargeStringResults { get => _options.AllowLargeStringResults; set => _options.AllowLargeStringResults = value; }
        public HashSet<string> AllowedFileTypeOverrides => _options.AllowedFileTypeOverrides;

        public int MaxParallelDegree { get => _options.MaxParallelDegree; set => _options.MaxParallelDegree = value; }
        public long MaxStringResultSize { get => _options.MaxStringResultSize; set => _options.MaxStringResultSize = value; }
        public int RegexMatchTimeoutMs { get => _options.RegexMatchTimeoutMs; set => _options.RegexMatchTimeoutMs = value; }
        public string? CurrentScriptPath { get; set; }
        public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();
        public int MaxFileOperations { get => _options.MaxFileOperations; set => _options.MaxFileOperations = value; }
        public int MaxGroupingSets { get => _options.MaxGroupingSets; set => _options.MaxGroupingSets = value; }
        public long MaxSessionSize { get => _options.MaxSessionSize; set => _options.MaxSessionSize = value; }
        public int MaxLastResultRows { get => _options.MaxLastResultRows; set => _options.MaxLastResultRows = value; }
        public int MaxGenerateRows { get => _options.MaxGenerateRows; set => _options.MaxGenerateRows = value; }
        public int MaxInternalOperations 
        { 
            get => _options.MaxInternalOperations; 
            set { _options.MaxInternalOperations = value; _securityService.MaxInternalOperations = value; } 
        }

        public bool IsPersistentSession { get; set; }
        public List<object?>? Parameters { get; set; }
        /// <summary>Start-of-week day for RELDATE W/WS/WE anchors. Settable at runtime via SET WEEK_START_DAY.</summary>
        public DayOfWeek WeekStartDay { get => _options.WeekStartDay; set => _options.WeekStartDay = value; }
        /// <summary>Hash-mismatch policy for script integrity checks. Settable at runtime via SET SCRIPT_HASH_POLICY.</summary>
        public string ScriptHashPolicy { get => _options.ScriptHashPolicy; set => _options.ScriptHashPolicy = value; }
        
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
        
        /// <summary>Master password for decrypting connection strings.</summary>
        public string? MasterPassword { get; set; }


        /// <summary>Script-level password for encryption/decryption of sensitive data within the script.</summary>
        public string? ScriptPassword { get; set; }
        
        /// <summary>Event raised when a batch of rows is processed.</summary>
        public Action<long>? OnBatchProcessed { get; set; }
        
        /// <summary>Event raised when a new result set is produced.</summary>
        public Action<DataTable>? OnResultSet { get; set; }
        
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
        
        public IServiceProvider ServiceProvider => _serviceProvider;

        public int JoinSpillThreshold { get => _options.JoinSpillThreshold; set => _options.JoinSpillThreshold = value; }
        public int ExternalHashPartitions { get => _options.ExternalHashPartitions; set => _options.ExternalHashPartitions = value; }
        public int ExternalSortChunkSize { get => _options.ExternalSortChunkSize; set => _options.ExternalSortChunkSize = value; }
        public int WindowSpillThreshold { get => _options.WindowSpillThreshold; set => _options.WindowSpillThreshold = value; }
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

        // ── Interface Implementations (IDataContext, IVariableContext, IEngineContext, etc.) ────────────────
        public string SpillToken => $"Session_{SessionId}";

        public object? GetVariable(string name)
        {
            if (SystemVariableProvider.IsSystemVariable(name))
                return SystemVariableProvider.Resolve(name, this);
            
            return VarContext.GetVariable(name);
        }

        public void SetVariable(string name, object? value) => VarContext.SetVariable(name, value);
        public void DeclareVariable(string name, object? value, VariableMetadata? metadata = null) => VarContext.DeclareVariable(name, value, metadata);
        public bool ContainsVariable(string name) => VarContext.ContainsVariable(name);
        public void PushScope(Dictionary<string, object?> vars, Dictionary<string, VariableMetadata>? metadata = null) => VarContext.PushScope(vars, metadata);
        public void PopScope() => VarContext.PopScope();
        
        public void SetProcedure(string name, CreateProcedureStatement stmt) => VarContext.SetProcedure(name, stmt);
        public bool TryGetProcedure(string name, out CreateProcedureStatement? stmt) => VarContext.TryGetProcedure(name, out stmt);
        public void SetFunction(string name, CreateFunctionStatement stmt) => VarContext.SetFunction(name, stmt);
        public bool RemoveFunction(string name) => VarContext.RemoveFunction(name);
        public bool TryGetFunction(string name, out CreateFunctionStatement? stmt) => VarContext.TryGetFunction(name, out stmt);
        public bool RemoveProcedure(string name) => VarContext.RemoveProcedure(name);
        public IDictionary<string, (object? Value, VariableMetadata Metadata)> GetVariablesWithMetadata(Func<VariableMetadata, bool>? predicate = null) => VarContext.GetVariablesWithMetadata(predicate);
        public bool ContainsVariableInCurrentScope(string name) => VarContext.ContainsVariableInCurrentScope(name);
        
        public IDictionary<string, object?> Variables => VarContext.Variables;
        public IDictionary<string, object?> CurrentVariables => VarContext.CurrentVariables;
        public IDictionary<string, VariableMetadata> VariableMetadata => VarContext.VariableMetadata;
        public IDictionary<string, VariableMetadata> CurrentMetadata => VarContext.CurrentMetadata;
        public long MemoryUsageBytes 
        {
            get
            {
                // Estimate variable metadata and subquery cache overhead
                long varBytes = Variables.Count * 256;
                long subqueryBytes = 0;
                foreach(var result in _subqueryCache.Values) subqueryBytes += result.MemoryUsageBytes;
                return varBytes + subqueryBytes;
            }
        }
        public Task<bool> SpillAsync()
        {
            if (_subqueryCache.Count > 0)
            {
                _subqueryCache.Clear();
                _logger.Warning("Evaluator spilled: Subquery cache cleared to reclaim memory.");
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

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
            
            _options = options ?? new EvaluatorOptions();
            _registry = registry ?? new EvaluatorComponentRegistry();
            _subqueryCache = new ETL_SQL.Core.Common.LruCache<SubqueryCacheKey, ETL_SQL.Core.Data.SubqueryResult>(_options.SubqueryCacheSize);

            _variableScopeManager = variableScopeManager ?? new VariableScopeManager();
            _registry.Initialize(this, _logger, _variableScopeManager, reportContext);

            Telemetry.ExecutionTree.Clear();
            if (executionTree != null)
            {
               foreach(var node in executionTree.GetAllNodes()) Telemetry.ExecutionTree.AddNode(node);
            }
            _connections = connections ?? new ConcurrentDictionary<string, IDataSource>(StringComparer.OrdinalIgnoreCase);
            
            _queryCompiler = _registry.QueryCompiler;
            _metricsReporter = _registry.MetricsReporter;
            _expressionEvaluator = _registry.ExpressionEvaluator;
            _spillStore = _registry.SpillStore;
            _dataSourceManager = _registry.DataSourceManager;
            _schemaManager = _registry.SchemaManager;
            _procedureExecutor = _registry.ProcedureExecutor;
            
            // Link Telemetry to registry components if needed, or initialized via registry.Initialize
            Telemetry.IsProfiling = _options.IsProfiling;

            Functions.StandardFunctions.Register(functionRegistry);
            Functions.FileFunctions.Register(functionRegistry);
            Functions.LineageFunctions.Register(functionRegistry);
            Functions.RegexFunctions.Register(functionRegistry);
            Functions.JsonFunctions.Register(functionRegistry);
            Functions.XmlFunctions.Register(functionRegistry);
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
            TempTableSpillThresholdRows = DefaultThresholds.TempTableSpillThresholdRows(config);
            
            _options.BatchSize = BatchSize;
            _options.SubqueryCacheSize = DefaultThresholds.SubqueryCacheSize(config);
            _options.TempTableSpillThresholdRows = TempTableSpillThresholdRows;
            SpillEncryptionEnabled = DefaultThresholds.SpillEncryptionEnabled(config);
            SpillCompressionEnabled = DefaultThresholds.SpillCompressionEnabled(config);
            SpillFormat = DefaultThresholds.SpillFormat(config);
            MaxLastResultRows = DefaultThresholds.MaxLastResultRows(config);
            MaxMessages = config?.GetValue<int>("Engine:MaxMessages", 1000) ?? 1000;
            WeekStartDay = DefaultThresholds.StartOfWeek(config);
            ScriptHashPolicy = DefaultThresholds.ScriptHashPolicy(config);
        }


        public async Task Evaluate(Script script, System.Threading.CancellationToken cancellationToken = default)
        {
            if (CurrentRecursiveDepth == 0)
            {
                LastResultSets.Clear();
                LastResult = null;
                _nodeReuseMap.Clear();
                _operationCount = 0;
                Telemetry.Clear();
                // lock(_messagesLock) { Messages.Clear(); } // Don't clear messages on every evaluate, allows TUI history
            }
            try
            {
                var analyzer = new LineageAnalyzer(LineageTracker);
                analyzer.Analyze(script);

                if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) {
                    var firstError = script.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
                    throw new ExecutionException($"Syntax error: {firstError.Message} at {firstError.Line}:{firstError.Column}");
                }

                var scriptNode = new ExecutionNode {
                    Name = "Script Execution",
                    Status = ExecutionStatus.Running,
                    StartTicks = Stopwatch.GetTimestamp()
                };
                Telemetry.ExecutionTree.AddNode(scriptNode);
                CurrentNodeId = scriptNode.Id;

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

                // Split into batches at GO boundaries.
                // Each batch runs independently — a failed batch is logged and skipped; later batches still execute.
                var batches = SplitIntoBatches(script.Statements);
                bool hasBatches = batches.Count > 1;
                int batchNum = 0;

                foreach (var batch in batches)
                {
                    batchNum++;
                    if (batch.Count == 0) continue;

                    try
                    {
                        foreach (var statement in batch)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await EvaluateStatement(statement);
                        }
                        if (hasBatches) Log($"Batch {batchNum} completed.", ConsoleColor.DarkGray);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (ReturnException) { throw; }
                    catch (Exception ex) when (hasBatches)
                    {
                        Log($"Batch {batchNum} failed: {ex.Message}", ConsoleColor.Red);
                    }
                }

                scriptNode.Status = ExecutionStatus.Completed;
                scriptNode.EndTicks = Stopwatch.GetTimestamp();
            }
            catch (ReturnException ex)
            {
                if (ex.Value != null) Spectre.Console.AnsiConsole.MarkupLine($"[cyan][RETURN][/] {Spectre.Console.Markup.Escape(ex.Value?.ToString() ?? "")}");
                else Spectre.Console.AnsiConsole.MarkupLine("[cyan][RETURN][/]");
            }
            finally
            {
                if (CurrentRecursiveDepth == 0)
                {
                    _subqueryCache.Clear();
                }
                _variableScopeManager.PurgeSecretVariables();
                if (TranCount > 0 && AutoRollbackOnFinish)
                {
                    _logger.Warning("Script execution ended with {Count} open transactions. Performing emergency rollback.", TranCount);
                    await RollbackAll();
                }
            }
        }

        public void ClearResults() => LastResult = null;
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

            foreach (var conn in state.Connections)
            {
                var connector = _connectorRegistry.GetConnector(conn.Type);
                if (connector != null)
                {
                    var ds = connector.CreateDataSource(this, conn.ConnectionString, conn.Options);
                    _connections[conn.Name] = ds;
                }
            }

            LineageTracker.Clear();
            LineageTracker.LoadState(state.LineageEntries);
            
            foreach (var temp in state.TempTables)
            {
                _connections[temp.Name] = await _dataSourceManager.RestoreTempTable(temp, ScriptPassword ?? ETL_SQL.Services.SecurityService.GetMachineKey());
            }
        }

        public async Task EvaluateStatement(Statement statement)
        {
            // Update PreviousErrorNumber and reset LastError for the new statement
            // We skip this for internal/structural nodes that don't count as "atomic" statements for @@ERROR purposes
            if (statement is not NoOpStatement && statement is not BlockStatement)
            {
                PreviousErrorNumber = LastError?.Number ?? 0;
                LastError = null;
            }

            var parentId = CurrentNodeId;

            var nodeName = statement.GetType().Name.Replace("Statement", "");
            
            // Refine node name for readability
            if (statement is UsePasswordStatement) nodeName = "USE PASSWORD";
            else if (statement is UseSetsStatement us) nodeName = $"USE SETS {us.Name}";
            else if (statement is CreateTableStatement cts) nodeName = $"CREATE TABLE {cts.TargetTable.TableName}";
            else if (statement is InsertStatement inst) nodeName = $"INSERT INTO {inst.TargetTable.TableName}";
            
            ExecutionNode node;
            var cacheKey = (parentId, statement);
            
            if (ReuseLoopNodes && _nodeReuseMap.TryGetValue(cacheKey, out var existingNode))
            {
                node = existingNode;
                node.Status = ExecutionStatus.Running;
                node.IterationCount++;
                // Note: StartTicks is updated to reflect the CURRENT iteration start,
                // while Cumulative Duration is implicitly handled by the UI or calculated via snapshot.
                node.StartTicks = Stopwatch.GetTimestamp();
                node.ErrorMessage = null;
            }
            else
            {
                node = new ExecutionNode { 
                    Name = statement.GetType().Name.Replace("Statement", ""),
                    Status = ExecutionStatus.Running,
                    StartTicks = Stopwatch.GetTimestamp()
                };
                Telemetry.ExecutionTree.AddNode(node, parentId);
                parentId = node.Id;
                if (ReuseLoopNodes) _nodeReuseMap[cacheKey] = node;
            }

            CurrentNodeId = node.Id;

            Stopwatch? sw = null;
            long startRows = Telemetry.RowsProcessed;
            if (IsVerbose || Telemetry.IsProfiling)
            {
                sw = Stopwatch.StartNew();
                if (IsVerbose)
                {
                    string sql = (statement is UsePasswordStatement ups) ? ups.ToSql(!ShowPassword) : statement.ToSql();
                    _logger.Debug("Executing {Sql}", Scrub(sql));
                }
                _metricsReporter.ReportPreExecutionMetrics(statement);
            }

            if (_statementHandlers.TryGetValue(statement.GetType(), out var handler))
            {
                try
                {
                    await handler.Execute(statement, this);
                    node.Status = ExecutionStatus.Completed;
                }
                catch (Exception ex)
                {
                    node.Status = ExecutionStatus.Faulted;
                    node.ErrorMessage = ex.Message;
                    LastError = new ErrorInfo(50000, ex.Message, 16, 1, statement.Line, null);
                    throw;
                }

                finally
                {
                    node.EndTicks = Stopwatch.GetTimestamp();
                    CurrentNodeId = parentId;
                    
                    _localSources.Clear();
                }
            }
            else
            {
                throw new ExecutionException($"No handler registered for {statement.GetType().Name} at Line {statement.Line}");
            }

            Telemetry.LastStatementRowsProcessed = Telemetry.RowsProcessed - startRows;

            if (sw != null)
            {
                sw.Stop();
                var elapsed = sw.ElapsedMilliseconds;
                Telemetry.LastExecutionTimeMs = elapsed; // Track absolute last statement timing
                _metricsReporter.ReportPostExecutionMetrics(statement, elapsed);
                if (IsVerbose) _metricsReporter.ProvideTips(statement);
                LastIndexUsedName = null;
            }
        }

        public Task<IDataSource> ResolveDataSourceAsync(TableReference table) => _dataSourceManager.ResolveDataSourceAsync(table, _connections, _transactionManager);
        public IAsyncEnumerable<DataTable> ResolveAndApplyOperators(TableReference table) => _dataSourceManager.ResolveAndApplyOperators(table, _connections, _transactionManager, BatchSize);

        public async Task<object?> ExecuteValue(string expression, Row? context = null, bool decryptSensitive = false)
        {
            var lexer = new ETL_SQL.Core.Parser.Lexer(expression);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens, expression);
            var expr = parser.ParseExpression();
            return await EvaluateValue(expr, context ?? new Row(), decryptSensitive);
        }

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

        public Task<object?> EvaluateValue(Expression? expr, Row context, bool decryptSensitive = false) => _expressionEvaluator.Evaluate(expr, context, decryptSensitive);
        public IAsyncEnumerable<Row> EvaluateStream(Expression? expr, Row context) => _expressionEvaluator.EvaluateStream(expr, context);
        public CompiledSql CompileExpression(Expression e, string d = "MSSQL") => _queryCompiler.CompileExpression(e, d);
        public CompiledSql CompileQuery(Statement s, string d = "MSSQL") => _queryCompiler.CompileQuery(s, d);
        public string GetSqlTableName(TableReference t, string dialect = "MSSQL")
        {
            var parts = new List<string>();
            if (t.DatabaseName != null) parts.Add(t.DatabaseName);
            if (t.SchemaName != null) parts.Add(t.SchemaName);
            
            if (t.TableName.Contains(".") && t.SchemaName == null)
            {
                parts.AddRange(t.TableName.Split('.'));
            }
            else
            {
                parts.Add(t.TableName);
            }

            Func<string, string> quote = dialect.ToUpperInvariant() switch
            {
                "MSSQL" => QuoteIdentifierMssql,
                "ORACLE" => s => QuoteIdentifierStandard(s.ToUpperInvariant()),
                _ => QuoteIdentifierStandard
            };

            return string.Join(".", parts.Select(quote));
        }

        private static string QuoteIdentifierMssql(string s) => 
            s.StartsWith("[") ? s : $"[{s.Replace("]", "]]")}]";

        private static string QuoteIdentifierStandard(string s)
        {
            if (s.StartsWith("\"")) return s;
            // For Postgres/Oracle, we only quote if the identifier contains special characters 
            // that REQUIRE quoting. 
            bool needsQuoting = s.Any(c => !char.IsLetterOrDigit(c) && c != '_');
            return needsQuoting ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
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

        public bool IsSoftEqual(object? l, object? r) => _expressionEvaluator.IsSoftEqual(l, r);
        public int CompareConstants(object? l, object? r) => _expressionEvaluator.CompareConstants(l, r);
        public object? MathOp(object? l, object? r, TokenType op) => _expressionEvaluator.MathOp(l, r, op);
        public bool EvaluateLike(object? left, object? right) => _expressionEvaluator.EvaluateLike(left, right);




        public void EvaluateCreateProcedure(CreateProcedureStatement stmt) => _variableScopeManager.SetProcedure(stmt.ProcedureName, stmt);
        public void EvaluateCreateFunction(CreateFunctionStatement stmt) => _variableScopeManager.SetFunction(stmt.FunctionName, stmt);
        public bool ProcedureExists(string name) => _variableScopeManager.TryGetProcedure(name, out _);
        public bool FunctionExists(string name) => _variableScopeManager.TryGetFunction(name, out _);

        public Task<object?> EvaluateUserDefinedFunction(FunctionCallExpression f, List<object?> args, Row context)
            => _procedureExecutor.EvaluateUserDefinedFunction(f, args, context);

        public Task EvaluateProcedure(string name, List<(string? Name, object? Value)> args)
            => _procedureExecutor.EvaluateProcedure(name, args);

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

        public async Task<bool> EvaluateCondition(Expression? expr, Row context)
        {
            if (expr == null) return true;
            var res = await EvaluateValue(expr, context);
            if (res == null || res == DBNull.Value) return false;
            if (res is bool b) return b;
            try { return Convert.ToBoolean(res); } catch { return false; }
        }

        public List<string> GetIndexedColumns(Expression? cond, string alias)
        {
            var cols = new List<string>();
            if (cond is BinaryExpression bin)
            {
                if (bin.Operator == TokenType.EQUALS)
                {
                    if (bin.Left is IdentifierExpression lid && IsFromAlias(lid.Name, alias)) cols.Add(GetColumnName(lid.Name));
                    if (bin.Right is IdentifierExpression rid && IsFromAlias(rid.Name, alias)) cols.Add(GetColumnName(rid.Name));
                }
                else if (bin.Operator == TokenType.AND)
                {
                    cols.AddRange(GetIndexedColumns(bin.Left, alias));
                    cols.AddRange(GetIndexedColumns(bin.Right, alias));
                }
            }
            return cols.Distinct().ToList();
        }

        private bool IsFromAlias(string identifier, string? alias)
        {
            if (string.IsNullOrEmpty(alias)) return true;
            if (identifier.Contains(".")) return identifier.StartsWith(alias + ".", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private string GetColumnName(string identifier)
        {
            int dot = identifier.IndexOf('.');
            return dot >= 0 ? identifier.Substring(dot + 1) : identifier;
        }

        public object? CastToType(object? value, string dataType) => _expressionEvaluator.CastToType(value, dataType);
        public bool IsSqlPushdown(string conn) => !string.Equals(conn, "DUAL", StringComparison.OrdinalIgnoreCase) && _connections.TryGetValue(conn, out var ds) && ds is IDatabaseSource db && db.SupportsSqlPushdown;

        public TableReference? GetIntoTable(Statement stmt)
        {
            if (stmt is SelectStatement s) return s.IntoTable;
            if (stmt is SetOperationStatement setOp) return GetIntoTable(setOp.Left);
            return null;
        }

        public async Task BeginTransaction() => await _transactionManager.BeginTransaction(_variableScopeManager.Variables, _connections);
        public async Task CommitTransaction() => await _transactionManager.CommitTransaction();
        public async Task RollbackTransaction(string? name = null) => await _transactionManager.RollbackTransaction(_variableScopeManager.Variables, _connections);
        public async Task RollbackAll() => await _transactionManager.RollbackAll(_variableScopeManager.Variables, _connections);

        public void Log(string message, ConsoleColor color = ConsoleColor.White, bool forwardToLogger = true)
        {
            var scrubbed = Scrub(message);
            
            if (forwardToLogger)
            {
                _logger.WriteLine(scrubbed, color);
                
                // If output is redirected, the OnMessage event (subscribed in constructor) 
                // will handle adding to the Messages list to avoid double-capture.
                if (RedirectOutput) return;
            }

            lock (_messagesLock)
            {
                Messages.Add(new LogEntry(scrubbed, color, DateTime.Now));
                if (Messages.Count > MaxMessages)
                {
                    Messages.RemoveAt(0);
                    if (Messages.Count > 0 && !Messages[0].Message.StartsWith("[TRUNCATED]"))
                    {
                        var first = Messages[0];
                        Messages[0] = first with { Message = "[TRUNCATED] " + first.Message };
                    }
                }
            }
        }

        public static string Scrub(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;
            // Scrub standard connection string passwords, tokens, etc.
            var res = System.Text.RegularExpressions.Regex.Replace(message, @"(?i)(password|pwd|token|secret)\s*=\s*[^\s;]+", "$1=********");
            // Scrub ETL-SQL encrypted constants
            res = System.Text.RegularExpressions.Regex.Replace(res, @"ENC:[a-zA-Z0-9+/=]+", "ENC:********");
            return res;
        }

        public string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // Strip surrounding double-quotes that Windows "Copy as path" adds (e.g. "C:\tmp\file.csv")
            if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
                path = path[1..^1];

            string resolved = path;
            var parts = path.Split(new[] { '/', '\\' }, 2);
            var connName = parts[0];
            if (_connections.TryGetValue(connName, out var ds))
            {
                var baseUri = ds.Path;
                if (!string.IsNullOrEmpty(baseUri) && baseUri != "MSSQL" && baseUri != "POSTGRES" && baseUri != "MYSQL" && baseUri != "SQLITE" && baseUri != "ORACLE")
                {
                    if (parts.Length > 1) resolved = Path.Combine(baseUri, parts[1]);
                    else resolved = baseUri;
                }
            }

            // Security Hardening: Always return full paths and validate
            // If the path contains a placeholder, we skip full-path resolution to avoid breaking the placeholder
            if (resolved.Contains("${"))
            {
                return resolved;
            }

            var fullPath = Path.IsPathRooted(resolved)
                ? Path.GetFullPath(resolved)
                : Path.GetFullPath(resolved, WorkingDirectory);

            if (_securityService != null)
            {
                _securityService.ValidatePath(fullPath);
                // Security Hardening: We removed ValidateFileType from ResolvePath because it was causing 
                // false positives for non-data-source operations like RUN SCRIPT. 
                // Data connectors now perform their own explicit file type validation.
            }
            else
            {
                // Internal test fallback: Log a warning if the service is missing
                _logger.Debug("Security validation skipped for path {Path}; SecurityService not initialized", fullPath);
            }
            
            return fullPath;
        }

        public object? ResolveIdentifier(string name, Row? row)
        {
            // 1. Try current row
            if (row != null && row.Columns.TryGetValue(name, out var val)) return val;
            
            // 2. Try outer row stack (for correlated subqueries)
            foreach (var outer in _outerRowStack)
            {
                if (outer != null && outer.Columns.TryGetValue(name, out var oval)) return oval;
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
            
            // Reclaim any 'Zombie' resource reservations (Reference Counting protection)
            if (!string.IsNullOrEmpty(SessionId))
            {
                _bufferManager?.ReleaseAllForSession(SessionId);
            }

            _spillStore?.Dispose();
            foreach (var conn in _connections.Values) await conn.DisposeAsync();
            await DockerManager.DisposeAsync();
            _connections.Clear();
        }

        public async Task<bool> ValidateCheckConstraint(Expression expression, Row row)
        {
            var result = await _expressionEvaluator.Evaluate(expression, row);
            return result != null && Convert.ToBoolean(result);
        }

        public async Task<bool> ValidateForeignKey(ForeignKeyReference reference, List<string> sourceColumns, Row row)
        {
            string connName = reference.Table.ConnectionName ?? reference.Table.TableName;
            if (!_connections.TryGetValue(connName, out var dataSource)) return true; 
            var sourceValues = sourceColumns.Select(col => row[col]).ToList();
            if (sourceValues.All(v => v == null || v == DBNull.Value)) return true;
            return await dataSource.ExistsAsync(reference.Columns, sourceValues);
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
                BatchSize = BatchSize,
                PreviewLimit = PreviewLimit,
                ScriptPassword = ScriptPassword,
                SessionId = SessionId,
                DisplayExecuteTree = DisplayExecuteTree,
                MaxGroupingSets = MaxGroupingSets
            };
            
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
        }

        public IDisposable EnterRecursiveScope()
        {
            CurrentRecursiveDepth++;
            _securityService.CheckRunawayProtection(OperationType.FileSystem, _operationCount, CurrentRecursiveDepth, AllowLargeFileOperationCount, AllowDeepRecursion, null);
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
    }
}
