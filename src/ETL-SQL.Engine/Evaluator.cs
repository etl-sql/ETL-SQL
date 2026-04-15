using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Collections.Concurrent;

using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Engine.Handlers;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine.Services;
using ETL_SQL.Core.Data;
using ETL_SQL.Services;

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
    public class Evaluator : IExecutionContext, IAsyncDisposable, IDataValidator
    {
        private readonly IEnumerable<IStatementHandler> _handlers;
        private readonly IServiceProvider _serviceProvider;
        private readonly Core.Functions.IFunctionRegistry _functionRegistry;
        private readonly ILineageTracker _lineageTracker;
        private readonly IDockerManager _dockerManager;
        private readonly IConnectorRegistry _connectorRegistry;
        private readonly SessionStateManager _sessionStateManager;
        private readonly SecurityService _securityService;
        private readonly ETL_SQL.Common.ILogger _logger;
        private readonly ConcurrentDictionary<string, IDataSource> _connections;
        private readonly Dictionary<string, IDataSource> _localSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly VariableScopeManager _variableScopeManager;
        private readonly QueryCompiler _queryCompiler;
        private readonly ExecutionMetricsReporter _metricsReporter;
        private readonly DataSourceManager _dataSourceManager;
        private readonly SchemaManager _schemaManager;
        private readonly ExpressionEvaluator _expressionEvaluator;
        private readonly ProcedureExecutor _procedureExecutor;
        private readonly BatchPipelineHelper _batchPipelineHelper = new();
        private readonly Dictionary<Type, IStatementHandler> _statementHandlers = new();

        private readonly Stack<Row> _outerRowStack = new();
        private readonly Dictionary<Statement, object?> _subqueryCache = new(new StatementSqlEqualityComparer());
        private readonly TransactionManager _transactionManager = new();

        /// <summary>Current transaction nesting level.</summary>
        public int TranCount => _transactionManager.TranCount;

        /// <summary>Total bytes spilled to disk for large joins/sorts.</summary>
        private long _totalSpilledBytes = 0;
        public long TotalSpilledBytes 
        { 
            get => System.Threading.Interlocked.Read(ref _totalSpilledBytes); 
            set => System.Threading.Interlocked.Exchange(ref _totalSpilledBytes, value); 
        }
        
        public int PartitionsCount { get; set; } = 0;
        public int MaxRecursiveDepth { get; set; } = 10000;
        public int CurrentRecursiveDepth { get; set; } = 0;
        public string? LastIndexUsedName { get; set; }
        public ErrorInfo? LastError { get; set; }
        public int PreviousErrorNumber { get; set; } = 0;

        
        /// <summary>Size of row batches used during streaming operations.</summary>
        public int BatchSize { get; set; } = 10000;
        
        /// <summary>Number of batches held in memory before spilling to disk for #temp tables.</summary>
        public int MaxInMemoryBatches { get; set; } = LanguageMetadata.DefaultMaxInMemoryBatches;

        /// <summary>Maximum rows to fetch per page for remote FOREACH pushdown.</summary>
        public int ForeachPageSize { get; set; } = 10000;
        
        private bool _isVerbose;
        
        /// <summary>
        /// Whether to output detailed execution logs for this evaluator instance.
        /// Reads the global Logger.IsVerbose as a fallback so that a process-wide verbose
        /// flag still takes effect, but setting this property does NOT mutate the global
        /// flag — avoiding race conditions when multiple Evaluators run concurrently.
        /// </summary>
        public bool IsVerbose
        {
            get => _isVerbose || _logger.IsVerbose;
            set => _isVerbose = value;
        }
        
        /// <summary>If true, Log messages are captured in the Messages list instead of direct console output.</summary>
        public bool RedirectOutput { get; set; }
        
        /// <summary>Limit the number of rows returned for previews.</summary>
        public int? PreviewLimit { get; set; }
        
        /// <summary>Preference for showing sensitive data in plain text in the UI.</summary>
        public bool ShowPassword { get; set; }
        
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

        // ── IReportContext ──────────────────────────────────────────────────
        /// <inheritdoc />
        public IDictionary<string, CreateVisualStatement> VisualDefinitions { get; } = new Dictionary<string, CreateVisualStatement>(StringComparer.OrdinalIgnoreCase);
        /// <inheritdoc />
        public IDictionary<string, CreatePageStatement> PageDefinitions { get; } = new Dictionary<string, CreatePageStatement>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Optional prompt callback for interactive USE SETS WITH_PROMPT. Null = non-interactive (auto-proceed).</summary>
        public Func<string, Task<bool>>? OnPrompt { get; set; }

        /// <summary>Whether to capture execution metrics for profiling.</summary>
        public bool IsJsonMode { get; set; }
        public bool IsProfiling { get; set; }
        
        /// <summary>Whether to run in dry-run mode (no side effects).</summary>
        public bool IsWhatIf { get; set; }

        /// <summary>Whether to display a graphical execution tree during the script run.</summary>
        public bool DisplayExecuteTree { get; set; } = true;

        /// <summary>The high-level execution tree for visual progress tracking.</summary>
        public ExecutionTree ExecutionTree { get; } = new();

        public SecurityService SecurityService => _securityService;

        public bool AllowUnknownFileTypes { get; set; }
        public bool AllowLargeFileOperationCount { get; set; }
        public bool AllowDeepRecursion { get; set; }

        public int JoinSpillThreshold { get; set; } = 100000;
        public int ExternalHashPartitions { get; set; } = 32;
        public int ExternalSortChunkSize { get; set; } = 100000;
        public int WindowSpillThreshold { get; set; } = LanguageMetadata.DefaultWindowSpillThreshold;


        /// <summary>The ID of the currently executing node in this task/context.</summary>
        public Guid? CurrentNodeId
        {
            get => ExecutionNode.Current.Value?.Id;
            set => ExecutionNode.Current.Value = value.HasValue ? ExecutionTree.GetNode(value.Value) : null;
        }
        
        /// <summary>Execution metrics for all statements run since profiling was enabled.</summary>
        public List<ExecutionMetrics> ProfileMetrics { get; } = new();
        
        /// <summary>Captured log messages if RedirectOutput is true.</summary>
        public List<string> Messages { get; } = new();
        
        /// <summary>Maximum number of messages to capture.</summary>
        public int MaxMessages { get; set; } = 1000;
        
        /// <summary>Interface for managing Docker database containers.</summary>
        public IDockerManager DockerManager => _dockerManager;
        
        /// <summary>Interface for tracking data lineage.</summary>
        public ILineageTracker LineageTracker => _lineageTracker;

        /// <summary>Manager for session persistence and cleanup.</summary>
        public SessionStateManager SessionStateManager => _sessionStateManager;

        public ILogger Logger => _logger;

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
                _sessionId = value;
                _logger.SessionId = value;
            }
        }
        private string? _sessionId;

        
        /// <summary>Cache for scalar subquery results to avoid redundant execution.</summary>
        public Dictionary<Statement, object?> SubqueryCache => _subqueryCache;
        
        /// <summary>Token used to cancel long-running operations in this context.</summary>
        public System.Threading.CancellationToken CancellationToken { get; private set; } = System.Threading.CancellationToken.None;
        
        /// <summary>Stack of row contexts for correlated subquery resolution.</summary>
        public Stack<Row> OuterRowStack => _outerRowStack;
        
        /// <summaryRegistry of all scalar and aggregate functions available in the session.</summary>
        public Core.Functions.IFunctionRegistry FunctionRegistry => _functionRegistry;

        // ── Interface Implementations (IDataContext, IVariableContext, IEngineContext, etc.) ────────────────
        public IDictionary<string, IDataSource> Connections => _connections;
        public IDictionary<string, IDataSource> LocalSources => _localSources;
        public IDictionary<string, object?> Variables => _variableScopeManager.GlobalVariables;
        public IDictionary<string, object?> CurrentVariables => _variableScopeManager.CurrentVariables;
        public IDictionary<string, VariableMetadata> VariableMetadata => _variableScopeManager.GlobalMetadata;
        public IDictionary<string, VariableMetadata> CurrentMetadata => _variableScopeManager.CurrentMetadata;

        public void SetVariable(string name, object? value) => _variableScopeManager.SetVariable(name, value);
        public object? GetVariable(string name)
        {
            if (name.Equals("@@TRANCOUNT", StringComparison.OrdinalIgnoreCase)) return TranCount;
            if (name.Equals("@@RESULTSETS", StringComparison.OrdinalIgnoreCase)) return LastResultSets;
            if (name.Equals("@@VERSION", StringComparison.OrdinalIgnoreCase)) return LanguageMetadata.GetFullVersionString();
            if (name.Equals("@@ROWCOUNT", StringComparison.OrdinalIgnoreCase)) return RowsProcessed;
            if (name.Equals("@@ERROR", StringComparison.OrdinalIgnoreCase)) return PreviousErrorNumber;
            if (name.Equals("@@TOTAL_SPILLED_BYTES", StringComparison.OrdinalIgnoreCase)) return TotalSpilledBytes;
            if (name.Equals("@@PARTITIONS_COUNT", StringComparison.OrdinalIgnoreCase)) return PartitionsCount;
            
            return _variableScopeManager.GetVariable(name);

        }

        public bool ContainsVariable(string name) => _variableScopeManager.ContainsVariable(name);
        public bool ContainsVariableInCurrentScope(string name) => _variableScopeManager.CurrentVariables.ContainsKey(name);
        public void DeclareVariable(string name, object? value, VariableMetadata? metadata = null) => _variableScopeManager.DeclareVariable(name, value, metadata);
        public Dictionary<string, object?> GetVariablesWithMetadata(Func<VariableMetadata, bool> predicate) => _variableScopeManager.GetVariablesWithMetadata(predicate);

        public void PushScope(Dictionary<string, object?> vars, Dictionary<string, VariableMetadata>? metadata = null) => _variableScopeManager.PushScope(vars, metadata);
        public void PopScope() => _variableScopeManager.PopScope();

        public Evaluator(
            IEnumerable<IStatementHandler> handlers,
            IServiceProvider serviceProvider,
            Core.Functions.IFunctionRegistry functionRegistry,
            ILineageTracker lineageTracker,
            IDockerManager dockerManager,
            IConnectorRegistry connectorRegistry,
            SessionStateManager sessionStateManager,
            SecurityService securityService,
            ILogger logger)
            : this(handlers, serviceProvider, functionRegistry, lineageTracker, dockerManager, connectorRegistry, sessionStateManager, securityService, logger, null, null, null)
        {
        }

        // Removed attribute to allow DI to pick the simpler constructor above
        public Evaluator(
            IEnumerable<IStatementHandler> handlers,
            IServiceProvider serviceProvider,
            Core.Functions.IFunctionRegistry functionRegistry,
            ILineageTracker lineageTracker,
            IDockerManager dockerManager,
            IConnectorRegistry connectorRegistry,
            SessionStateManager sessionStateManager,
            SecurityService securityService,
            ILogger logger,
            ConcurrentDictionary<string, IDataSource>? connections,
            VariableScopeManager? variableScopeManager,
            ExecutionTree? executionTree)
        {
            _handlers = handlers;
            _serviceProvider = serviceProvider;
            _functionRegistry = functionRegistry;
            _lineageTracker = lineageTracker;
            _dockerManager = dockerManager;
            _connectorRegistry = connectorRegistry;
            _sessionStateManager = sessionStateManager;
            _logger = logger;
            _securityService = securityService;
            ExecutionTree = executionTree ?? new ExecutionTree();
            _connections = connections ?? new ConcurrentDictionary<string, IDataSource>(StringComparer.OrdinalIgnoreCase);
            _variableScopeManager = variableScopeManager ?? new VariableScopeManager();
            _queryCompiler = new QueryCompiler(this);
            _metricsReporter = new ExecutionMetricsReporter(this);
            _expressionEvaluator = new ExpressionEvaluator(this);
            _dataSourceManager = new DataSourceManager(_logger, this, _expressionEvaluator);
            _schemaManager = new SchemaManager(_logger, this, _variableScopeManager);
            _procedureExecutor = new ProcedureExecutor(_variableScopeManager, this);

            Functions.FileFunctions.Register(functionRegistry);
            Functions.LineageFunctions.Register(functionRegistry);
            Functions.RegexFunctions.Register(functionRegistry);
            Functions.JsonFunctions.Register(functionRegistry);
            Functions.XmlFunctions.Register(functionRegistry);

            foreach (var handler in handlers)
            {
                _statementHandlers[handler.SupportedStatementType] = handler;
            }

            // Special mapping: SelectStatementHandler also handles SetOperationStatement
            if (_statementHandlers.TryGetValue(typeof(SelectStatement), out var selectHandler))
            {
                _statementHandlers[typeof(SetOperationStatement)] = selectHandler;
            }

            // Assign a short session ID for log correlation across concurrent sessions.
            // Callers can override this after construction if they have a meaningful ID.
            SessionId = Guid.NewGuid().ToString("N")[..8];

            _logger.Info("Evaluator initialized.");

            // Standard OnMessage hook for capturing output into the Messages list
            _logger.OnMessage += (msg, col) =>
            {
                if (RedirectOutput)
                {
                    lock (_messagesLock)
                    {
                        Messages.Add(msg);
                        if (Messages.Count > MaxMessages)
                            Messages.RemoveAt(0);
                    }
                }
            };
        }

        public async Task Evaluate(Script script, System.Threading.CancellationToken cancellationToken = default)
        {
            LastResultSets.Clear();
            _operationCount = 0;
            CurrentRecursiveDepth = 0;
            try
            {
                var analyzer = new LineageAnalyzer(LineageTracker);
                analyzer.Analyze(script);

                if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                {
                    var firstError = script.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
                    throw new ExecutionException($"Syntax error: {firstError.Message} at {firstError.Line}:{firstError.Column}");
                }

                var scriptNode = new ExecutionNode {
                    Name = "Script Execution",
                    Status = ExecutionStatus.Running,
                    StartTicks = Stopwatch.GetTimestamp()
                };
                ExecutionTree.AddNode(scriptNode);
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
                foreach (var statement in script.Statements)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await EvaluateStatement(statement);
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
                _subqueryCache.Clear();
            }
        }

        public void ClearResults() => LastResult = null;
        public (Dictionary<string, object?>, Dictionary<string, VariableMetadata>) GetGlobalState() => _variableScopeManager.GetGlobalState();

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
                _connections[temp.Name] = await _dataSourceManager.RestoreTempTable(temp, ScriptPassword ?? _sessionStateManager.GetMachineKey());
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
            
            var node = new ExecutionNode { 
                Name = nodeName,
                Status = ExecutionStatus.Running,
                StartTicks = Stopwatch.GetTimestamp()
            };
            
            ExecutionTree.AddNode(node, parentId);
            CurrentNodeId = node.Id;

            Stopwatch? sw = null;
            long startRows = RowsProcessed;
            if (IsVerbose || IsProfiling)
            {
                sw = Stopwatch.StartNew();
                if (IsVerbose)
                {
                    string sql = (statement is UsePasswordStatement ups) ? ups.ToSql(!ShowPassword) : statement.ToSql();
                    _logger.Debug("Executing {Sql}", sql);
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

            if (sw != null)
            {
                sw.Stop();
                var elapsed = sw.ElapsedMilliseconds;
                _metricsReporter.ReportPostExecutionMetrics(statement, elapsed);
                if (IsProfiling) LastStatementRowsProcessed = RowsProcessed - startRows;
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

        public Task<object?> EvaluateValue(Expression? expr, Row context) => _expressionEvaluator.Evaluate(expr, context);
        public IAsyncEnumerable<Row> EvaluateStream(Expression? expr, Row context) => _expressionEvaluator.EvaluateStream(expr, context);
        public string CompileExpression(Expression e, string d = "MSSQL") => _queryCompiler.CompileExpression(e, d);
        public string CompileQuery(Statement s, string d = "MSSQL") => _queryCompiler.CompileQuery(s, d);
        public string GetSqlTableName(TableReference t, string dialect = "MSSQL")
        {
            var parts = new List<string>();
            if (t.DatabaseName != null) parts.Add(t.DatabaseName);
            if (t.SchemaName != null) parts.Add(t.SchemaName);
            
            // Handle case where TableName contains a dot (e.g. "schema.table") but SchemaName is null
            if (t.TableName.Contains(".") && t.SchemaName == null)
            {
                parts.AddRange(t.TableName.Split('.'));
            }
            else
            {
                parts.Add(t.TableName);
            }

            // Security Hardening (CR-S2): Apply dialect-appropriate identifier quoting
            Func<string, string> quote = dialect.Equals("MSSQL", StringComparison.OrdinalIgnoreCase)
                ? s => s.StartsWith("[") ? s : $"[{s.Replace("]", "]]")}]"
                : s => 
                {
                    if (s.StartsWith("\"")) return s;
                    // For Postgres/Oracle, we only quote if the identifier contains special characters 
                    // that REQUIRES quoting. 
                    bool needsQuoting = s.Any(c => !char.IsLetterOrDigit(c) && c != '_');

                    return needsQuoting ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
                };

            return string.Join(".", parts.Select(quote));
        }

        public IAsyncEnumerable<DataTable> InterceptProgress(IAsyncEnumerable<DataTable> chunks)
            => _batchPipelineHelper.InterceptProgress(chunks, OnBatchProcessed);

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

        public Task EvaluateCreateTable(CreateTableStatement stmt) => _schemaManager.EvaluateCreateTable(stmt, _connections);
        public Task EvaluateDropTable(DropTableStatement stmt) => _schemaManager.EvaluateDropTable(stmt, _connections);
        public Task EvaluateDropConnection(DropConnectionStatement stmt) => _schemaManager.EvaluateDropConnection(stmt, _connections);
        public void EvaluateDropProcedure(DropProcedureStatement stmt) => _schemaManager.EvaluateDropProcedure(stmt);
        public void EvaluateDropFunction(DropFunctionStatement stmt) => _schemaManager.EvaluateDropFunction(stmt);
        public Task EvaluateDropIndex(DropIndexStatement stmt) => _schemaManager.EvaluateDropIndex(stmt, _connections);
        public Task EvaluateCreateIndex(CreateIndexStatement stmt) => _schemaManager.EvaluateCreateIndex(stmt, _connections);

        public Task EvaluateClearSession(ClearSessionStatement stmt)
        {
            if (SessionId != null) _sessionStateManager.ClearSession(SessionId);
            return Task.CompletedTask;
        }

        public void EvaluateCreateProcedure(CreateProcedureStatement stmt) => _variableScopeManager.SetProcedure(stmt.ProcedureName, stmt);
        public void EvaluateCreateFunction(CreateFunctionStatement stmt) => _variableScopeManager.SetFunction(stmt.FunctionName, stmt);
        public bool ProcedureExists(string name) => _variableScopeManager.TryGetProcedure(name, out _);
        public bool FunctionExists(string name) => _variableScopeManager.TryGetFunction(name, out _);

        public Task<object?> EvaluateUserDefinedFunction(FunctionCallExpression f, List<object?> args, Row context)
            => _procedureExecutor.EvaluateUserDefinedFunction(f, args, context);

        public Task EvaluateProcedure(string name, List<object?> args)
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
            return res is bool b && b;
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

        public async Task BeginTransaction() => await _transactionManager.BeginTransaction(_variableScopeManager.GlobalVariables, _connections);
        public async Task CommitTransaction() => await _transactionManager.CommitTransaction();
        public async Task RollbackTransaction(string? name = null) => await _transactionManager.RollbackTransaction(_variableScopeManager.GlobalVariables, _connections);
        public async Task RollbackAll() => await _transactionManager.RollbackAll(_variableScopeManager.GlobalVariables, _connections);

        public void Log(string message, ConsoleColor color = ConsoleColor.White)
        {
            lock (_messagesLock)
            {
                Messages.Add(message);
                if (Messages.Count > MaxMessages)
                {
                    Messages.RemoveAt(0);
                    if (Messages.Count > 0 && !Messages[0].StartsWith("[TRUNCATED]"))
                        Messages[0] = "[TRUNCATED] " + Messages[0];
                }
            }
        }

        public string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

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

            var fullPath = Path.GetFullPath(resolved);

            if (_securityService != null)
            {
                _securityService.ValidatePath(fullPath);
                _securityService.ValidateFileType(fullPath, AllowUnknownFileTypes);
            }
            else
            {
                // Internal test fallback: Log a warning if the service is missing
                _logger.Debug("Security validation skipped for path {Path}; SecurityService not initialized", fullPath);
            }
            
            return fullPath;
        }

        public object? ResolveIdentifier(string name, Row? row) => _variableScopeManager.ResolveIdentifier(name, row);

        public async ValueTask DisposeAsync()
        {
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
            var fork = new Evaluator(freshHandlers, _serviceProvider, _functionRegistry, _lineageTracker, _dockerManager, _connectorRegistry, _sessionStateManager, _securityService, _logger, _connections, _variableScopeManager.Fork(), ExecutionTree)
            {
                IsVerbose = IsVerbose,
                RedirectOutput = RedirectOutput,
                IsProfiling = IsProfiling,
                IsWhatIf = IsWhatIf,
                ShowPassword = ShowPassword,
                BatchSize = BatchSize,
                PreviewLimit = PreviewLimit,
                ScriptPassword = ScriptPassword,
                SessionId = SessionId,
                DisplayExecuteTree = DisplayExecuteTree
            };
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
            lock (_messagesLock) foreach (var msg in spawned.Messages) Log(msg);
            System.Threading.Interlocked.Add(ref _rowsProcessed, spawned.RowsProcessed);
            System.Threading.Interlocked.Add(ref _totalSpilledBytes, spawned.TotalSpilledBytes);
        }

        private long _rowsProcessed = 0;
        public long RowsProcessed 
        { 
            get => System.Threading.Interlocked.Read(ref _rowsProcessed); 
            set => System.Threading.Interlocked.Exchange(ref _rowsProcessed, value); 
        }
        public long LastStatementRowsProcessed { get; set; }

        private int _operationCount = 0;
        public void IncrementOperationCount(string? path = null)
        {
            _operationCount++;
            _securityService.CheckRunawayProtection(_operationCount, CurrentRecursiveDepth, AllowLargeFileOperationCount, AllowDeepRecursion, path);
        }
    }
}
