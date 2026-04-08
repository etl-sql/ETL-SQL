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
        private static readonly Random _random = new Random();
 
        private readonly ConcurrentDictionary<string, IDataSource> _connections;
        private readonly VariableScopeManager _variableScopeManager;
        private readonly QueryCompiler _queryCompiler;
        private readonly ExecutionMetricsReporter _metricsReporter;
        private readonly DataSourceManager _dataSourceManager;
        private readonly SchemaManager _schemaManager;
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
        public int MaxRecursiveDepth { get; set; } = 0;
        public string? LastIndexUsedName { get; set; }
        
        /// <summary>Size of row batches used during streaming operations.</summary>
        public int BatchSize { get; set; } = 10000;
        
        private bool _isVerbose;
        
        /// <summary>
        /// Whether to output detailed execution logs for this evaluator instance.
        /// Reads the global Logger.IsVerbose as a fallback so that a process-wide verbose
        /// flag still takes effect, but setting this property does NOT mutate the global
        /// flag — avoiding race conditions when multiple Evaluators run concurrently.
        /// </summary>
        public bool IsVerbose
        {
            get => _isVerbose || Logger.IsVerbose;
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

        /// <summary>Optional prompt callback for interactive USE SETS WITH_PROMPT. Null = non-interactive (auto-proceed).</summary>
        public Func<string, Task<bool>>? OnPrompt { get; set; }

        /// <summary>Whether to capture execution metrics for profiling.</summary>
        public bool IsProfiling { get; set; }
        
        /// <summary>Whether to run in dry-run mode (no side effects).</summary>
        public bool IsWhatIf { get; set; }
        
        /// <summary>Execution metrics for all statements run since profiling was enabled.</summary>
        public List<ExecutionMetrics> ProfileMetrics { get; } = new();
        
        /// <summary>Captured log messages if RedirectOutput is true.</summary>
        public List<string> Messages { get; } = new();
        
        /// <summary>Maximum number of messages to capture.</summary>
        public int MaxMessages { get; set; } = 100;
        
        /// <summary>Interface for managing Docker database containers.</summary>
        public IDockerManager DockerManager { get; }
        
        /// <summary>Interface for tracking data lineage.</summary>
        public ILineageTracker LineageTracker { get; }

        /// <summary>Manager for session persistence and cleanup.</summary>
        public SessionStateManager SessionStateManager { get; }

        /// <summary>Unique identifier for the current session.</summary>
        public string? SessionId { get; set; }

        private readonly Dictionary<Type, IStatementHandler> _statementHandlers = new();
        private readonly IEnumerable<IStatementHandler> _allHandlers;
        private readonly IServiceProvider _serviceProvider;
        private readonly ExpressionEvaluator _expressionEvaluator;
        private readonly ProcedureExecutor _procedureExecutor;
        private readonly BatchPipelineHelper _batchPipelineHelper;
        private readonly IConnectorRegistry _connectorRegistry;
        
        /// <summary>Cache for scalar subquery results to avoid redundant execution.</summary>
        public Dictionary<Statement, object?> SubqueryCache => _subqueryCache;
        
        /// <summary>Stack of row contexts for correlated subquery resolution.</summary>
        public Stack<Row> OuterRowStack => _outerRowStack;
        
        /// <summaryRegistry of all scalar and aggregate functions available in the session.</summary>
        public Core.Functions.IFunctionRegistry FunctionRegistry { get; }

        // ── Interface Implementations (IDataContext, IVariableContext, IEngineContext, etc.) ────────────────
        public IDictionary<string, IDataSource> Connections => _connections;
        public IDictionary<string, object?> Variables => _variableScopeManager.GlobalVariables;
        public IDictionary<string, object?> CurrentVariables => _variableScopeManager.CurrentVariables;
        public IDictionary<string, VariableMetadata> VariableMetadata => _variableScopeManager.GlobalMetadata;
        public IDictionary<string, VariableMetadata> CurrentMetadata => _variableScopeManager.CurrentMetadata;

        public void SetVariable(string name, object? value) => _variableScopeManager.SetVariable(name, value);
        public object? GetVariable(string name)
        {
            if (name.Equals("@@TRANCOUNT", StringComparison.OrdinalIgnoreCase)) return TranCount;
            return _variableScopeManager.GetVariable(name);
        }
        public bool ContainsVariable(string name) => _variableScopeManager.ContainsVariable(name);
        public bool ContainsVariableInCurrentScope(string name) => _variableScopeManager.CurrentVariables.ContainsKey(name);
        public void DeclareVariable(string name, object? value, VariableMetadata? metadata = null) => _variableScopeManager.DeclareVariable(name, value, metadata);
        public Dictionary<string, object?> GetVariablesWithMetadata(Func<VariableMetadata, bool> predicate) => _variableScopeManager.GetVariablesWithMetadata(predicate);

        public void PushScope(Dictionary<string, object?> vars, Dictionary<string, VariableMetadata>? metadata = null) => _variableScopeManager.PushScope(vars, metadata);
        public void PopScope() => _variableScopeManager.PopScope();

        /// <summary>
        /// Initializes a new instance of the Evaluator.
        /// </summary>
        public Evaluator(IEnumerable<IStatementHandler> handlers, IServiceProvider serviceProvider, Core.Functions.IFunctionRegistry functionRegistry, ILineageTracker lineageTracker, IDockerManager dockerManager, IConnectorRegistry connectorRegistry, SessionStateManager sessionStateManager)
            : this(handlers, serviceProvider, functionRegistry, lineageTracker, dockerManager, connectorRegistry, sessionStateManager, new ConcurrentDictionary<string, IDataSource>(StringComparer.OrdinalIgnoreCase), new VariableScopeManager())
        {
        }

        private Evaluator(IEnumerable<IStatementHandler> handlers, IServiceProvider serviceProvider, Core.Functions.IFunctionRegistry functionRegistry, ILineageTracker lineageTracker, IDockerManager dockerManager, IConnectorRegistry connectorRegistry, SessionStateManager sessionStateManager, ConcurrentDictionary<string, IDataSource> connections, VariableScopeManager variableScopeManager)
        {
            _allHandlers = handlers;
            _serviceProvider = serviceProvider;
            FunctionRegistry = functionRegistry;
            LineageTracker = lineageTracker;
            DockerManager = dockerManager;
            _connectorRegistry = connectorRegistry;
            SessionStateManager = sessionStateManager;
            
            _connections = connections;
            _variableScopeManager = variableScopeManager;
            _queryCompiler = new QueryCompiler(this);
            _metricsReporter = new ExecutionMetricsReporter(this);
            _expressionEvaluator = new ExpressionEvaluator(this);
            _dataSourceManager = new DataSourceManager(this, _expressionEvaluator);
            _schemaManager = new SchemaManager(this, _variableScopeManager);
            _procedureExecutor = new ProcedureExecutor(_variableScopeManager, this);
            _batchPipelineHelper = new BatchPipelineHelper();
            
            Functions.FileFunctions.Register(FunctionRegistry);
            Functions.LineageFunctions.Register(FunctionRegistry);
            Functions.RegexFunctions.Register(FunctionRegistry);
            Functions.JsonFunctions.Register(FunctionRegistry);
            Functions.XmlFunctions.Register(FunctionRegistry);
            foreach (var handler in handlers)
            {
                _statementHandlers[handler.SupportedStatementType] = handler;
            }

            // Special mapping: SelectStatementHandler also handles SetOperationStatement
            if (_statementHandlers.TryGetValue(typeof(SelectStatement), out var selectHandler))
            {
                _statementHandlers[typeof(SetOperationStatement)] = selectHandler;
            }

            Logger.OnMessage += (msg, col) => 
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

        /// <summary>
        /// Orchestrates the execution of a multi-statement script.
        /// </summary>
        /// <param name="script">The parsed script to evaluate.</param>
        public async Task Evaluate(Script script)
        {
            LastResultSets.Clear();
            try
            {
                // Perform static lineage analysis before execution
                var analyzer = new LineageAnalyzer(LineageTracker);
                analyzer.Analyze(script);

                if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                {
                    var firstError = script.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
                    throw new ExecutionException($"Syntax error: {firstError.Message} at {firstError.Line}:{firstError.Column}");
                }

                foreach (var statement in script.Statements)
                {
                    await EvaluateStatement(statement);
                }
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

        /// <summary>Clears the cached result of the last query.</summary>
        public void ClearResults() => LastResult = null;

        /// <summary>Captures the global variable state for session persistence.</summary>
        public (Dictionary<string, object?>, Dictionary<string, VariableMetadata>) GetGlobalState() => _variableScopeManager.GetGlobalState();

        /// <summary>Loads a previously saved session state into this evaluator instance.</summary>
        public async Task LoadSessionState(SessionState state)
        {
            // 1. Restore Variables
            _variableScopeManager.LoadGlobalState(state.GlobalVariables, state.GlobalMetadata);

            // 2. Restore Docker State
            DockerManager.LoadState(state.DockerConnectionStrings, state.LastDockerConnectionString);

            // 3. Restore Connections
            foreach (var conn in state.Connections)
            {
                var connector = _connectorRegistry.GetConnector(conn.Type);
                if (connector != null)
                {
                    var ds = connector.CreateDataSource(conn.ConnectionString, conn.Options);
                    _connections[conn.Name] = ds;
                }
            }

            // 4. Restore Lineage
            LineageTracker.Clear();
            LineageTracker.LoadState(state.LineageEntries);
            
            // 5. Restore Temp Tables (#tables)
            foreach (var temp in state.TempTables)
            {
                _connections[temp.Name] = await _dataSourceManager.RestoreTempTable(temp, ScriptPassword ?? SessionStateManager.GetMachineKey());
            }
        }

        /// <summary>
        /// Dispatches a single statement to its registered handler and captures metrics.
        /// </summary>
        /// <param name="statement">The statement to execute.</param>
        public async Task EvaluateStatement(Statement statement)
        {
            Stopwatch? sw = null;
            long startMem = 0;
            long startRows = RowsProcessed;
            if (IsVerbose || IsProfiling)
            {
                sw = Stopwatch.StartNew();
                if (IsVerbose)
                {
                    string sql = (statement is UsePasswordStatement ups) ? ups.ToSql(!ShowPassword) : statement.ToSql();
                    Logger.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] EXECUTING: {sql}", ConsoleColor.DarkGray);
                }
                _metricsReporter.ReportPreExecutionMetrics(statement);
                if (IsProfiling) startMem = GC.GetTotalMemory(false);
            }

            if (_statementHandlers.TryGetValue(statement.GetType(), out var handler))
            {
                await handler.Execute(statement, this);
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
                
                if (IsProfiling)
                {
                   LastStatementRowsProcessed = RowsProcessed - startRows;
                }

                if (IsVerbose) _metricsReporter.ProvideTips(statement);
                LastIndexUsedName = null;
            }
        }

        /// <summary>
        /// Resolves a table reference to a functional IDataSource.
        /// Handles subqueries, function calls, dual table, and temporary tables.
        /// </summary>
        public Task<IDataSource> ResolveDataSourceAsync(TableReference table) => _dataSourceManager.ResolveDataSourceAsync(table, _connections, _transactionManager);

        /// <summary>
        /// Reads batches from a data source and applies high-level operators like PIVOT or UNPIVOT.
        /// </summary>
        public IAsyncEnumerable<DataTable> ResolveAndApplyOperators(TableReference table) => _dataSourceManager.ResolveAndApplyOperators(table, _connections, _transactionManager, BatchSize);

        /// <summary>
        /// Executes a query statement (SELECT, UNION, etc.) and returns an async stream of row batches.
        /// </summary>
        public async IAsyncEnumerable<DataTable> ExecuteQuery(Statement stmt)
        {
            if (stmt is ExplainStatement explain)
            {
                yield return await EvaluateExplain(explain);
            }
            else
            {
                var handler = (SelectStatementHandler)_statementHandlers[typeof(SelectStatement)];
                await foreach (var b in handler.EvaluateQuery(stmt, this)) yield return b;
            }
        }

        internal async Task<DataTable> EvaluateExplain(ExplainStatement stmt)
        {
            var handler = (Handlers.ExplainStatementHandler)_statementHandlers[typeof(ExplainStatement)];
            await handler.Execute(stmt, this);
            return LastResult!;
        }

        internal async IAsyncEnumerable<DataTable> EvaluateSelect(SelectStatement stmt)
        {
            var handler = (SelectStatementHandler)_statementHandlers[typeof(SelectStatement)];
            await foreach (var batch in handler.EvaluateSelect(stmt, this))
            {
                yield return batch;
            }
        }

        internal async IAsyncEnumerable<DataTable> EvaluateSetOperation(SetOperationStatement setOp)
        {
            var handler = (SelectStatementHandler)_statementHandlers[typeof(SelectStatement)];
            await foreach (var batch in handler.EvaluateSetOperation(setOp, this))
            {
                yield return batch;
            }
        }

        /// <summary>Evaluates a scalar expression within a given row context.</summary>
        public Task<object?> EvaluateValue(Expression? expr, Row context) => _expressionEvaluator.Evaluate(expr, context);

        /// <summary>
        /// Compiles a scalar expression back into a provider-specific SQL string (e.g., for push-down).
        /// </summary>
        public string CompileExpression(Expression e, string d = "MSSQL") => _queryCompiler.CompileExpression(e, d);

        /// <summary>
        /// Compiles a full SELECT or MERGE statement back into a provider-specific SQL string.
        /// </summary>
        public string CompileQuery(Statement s, string d = "MSSQL") => _queryCompiler.CompileQuery(s, d);

        /// <summary>Retrieves the physical SQL table name for a table reference.</summary>
        public string GetSqlTableName(TableReference t) => t.TableName;

        /// <summary>Wraps an async stream of batches to trigger progress events.</summary>
        public IAsyncEnumerable<DataTable> InterceptProgress(IAsyncEnumerable<DataTable> chunks)
            => _batchPipelineHelper.InterceptProgress(chunks, OnBatchProcessed);

        /// <summary>Ensures batches conform to a specific set of target columns (projection/alignment).</summary>
        public IAsyncEnumerable<DataTable> AlignColumns(IAsyncEnumerable<DataTable> batches, List<string> targetCols)
            => _batchPipelineHelper.AlignColumns(batches, targetCols);

        /// <summary>Attempts to extract a FOR JSON or FOR XML clause from a statement.</summary>
        public ForClause? GetForClause(Statement stmt)
        {
            if (stmt is SelectStatement sel) return sel.ForClause;
            if (stmt is SetOperationStatement setOp) return GetForClause(setOp.Right);
            return null;
        }

        /// <summary>Applies a FOR JSON or FOR XML transformation to a stream of batches.</summary>
        public IAsyncEnumerable<DataTable> EvaluateForClause(IAsyncEnumerable<DataTable> batches, ForClause forClause)
            => _batchPipelineHelper.EvaluateForClause(batches, forClause);


        
        /// <summary>Checks for soft equality between two objects (e.g., numeric type coercion).</summary>
        public bool IsSoftEqual(object? l, object? r) => _expressionEvaluator.IsSoftEqual(l, r);
        
        /// <summary>Compares two constant values for ordering.</summary>
        public int CompareConstants(object? l, object? r) => _expressionEvaluator.CompareConstants(l, r);
        
        /// <summary>Performs a mathematical operation between two values.</summary>
        public object? MathOp(object? l, object? r, TokenType op) => _expressionEvaluator.MathOp(l, r, op);
        
        /// <summary>Evaluates a LIKE pattern match.</summary>
        public bool EvaluateLike(object? left, object? right) => _expressionEvaluator.EvaluateLike(left, right);

        /// <summary>Executes a CREATE TABLE statement.</summary>
        public Task EvaluateCreateTable(CreateTableStatement stmt) => _schemaManager.EvaluateCreateTable(stmt, _connections);

        /// <summary>Executes a DROP TABLE statement.</summary>
        public Task EvaluateDropTable(DropTableStatement stmt) => _schemaManager.EvaluateDropTable(stmt, _connections);

        /// <summary>Executes a DROP CONNECTION statement.</summary>
        public Task EvaluateDropConnection(DropConnectionStatement stmt) => _schemaManager.EvaluateDropConnection(stmt, _connections);

        /// <summary>Executes a DROP PROCEDURE statement.</summary>
        public void EvaluateDropProcedure(DropProcedureStatement stmt) => _schemaManager.EvaluateDropProcedure(stmt);

        /// <summary>Executes a DROP FUNCTION statement.</summary>
        public void EvaluateDropFunction(DropFunctionStatement stmt) => _schemaManager.EvaluateDropFunction(stmt);

        /// <summary>Executes a DROP INDEX statement.</summary>
        public Task EvaluateDropIndex(DropIndexStatement stmt) => _schemaManager.EvaluateDropIndex(stmt, _connections);

        /// <summary>Executes a CREATE INDEX statement.</summary>
        public Task EvaluateCreateIndex(CreateIndexStatement stmt) => _schemaManager.EvaluateCreateIndex(stmt, _connections);

        /// <summary>Executes a CLEAR SESSION statement, explicitly deleting session files.</summary>
        public Task EvaluateClearSession(ClearSessionStatement stmt)
        {
            if (!string.IsNullOrEmpty(SessionId))
            {
                Logger.WriteLine($"Clearing session {SessionId}...", ConsoleColor.Cyan);
                SessionStateManager.ClearSession(SessionId);
            }
            return Task.CompletedTask;
        }

        /// <summary>Registers a new stored procedure in the session.</summary>
        public void EvaluateCreateProcedure(CreateProcedureStatement stmt)
        {
            _variableScopeManager.SetProcedure(stmt.ProcedureName, stmt);
        }

        /// <summary>Registers a new user-defined function in the session.</summary>
        public void EvaluateCreateFunction(CreateFunctionStatement stmt)
        {
            _variableScopeManager.SetFunction(stmt.FunctionName, stmt);
        }

        /// <summary>Checks if a procedure with the given name exists.</summary>
        public bool ProcedureExists(string name) => _variableScopeManager.TryGetProcedure(name, out _);
        
        /// <summary>Checks if a function with the given name exists.</summary>
        public bool FunctionExists(string name) => _variableScopeManager.TryGetFunction(name, out _);

        /// <summary>Evaluates a user-defined function call.</summary>
        public Task<object?> EvaluateUserDefinedFunction(FunctionCallExpression f, List<object?> args, Row context)
            => _procedureExecutor.EvaluateUserDefinedFunction(f, args, context);

        /// <summary>Executes a stored procedure call.</summary>
        public Task EvaluateProcedure(string name, List<object?> args)
            => _procedureExecutor.EvaluateProcedure(name, args);

        /// <summary>Executes a DELETE statement.</summary>
        public async Task EvaluateDelete(DeleteStatement stmt)
        {
            var handler = _statementHandlers[typeof(DeleteStatement)];
            await handler.Execute(stmt, this);
        }

        /// <summary>Executes an UPDATE statement.</summary>
        public async Task EvaluateUpdate(UpdateStatement stmt)
        {
            var handler = _statementHandlers[typeof(UpdateStatement)];
            await handler.Execute(stmt, this);
        }

        /// <summary>Evaluates a boolean expression and returns true or false.</summary>
        public async Task<bool> EvaluateCondition(Expression? expr, Row context)
        {
            if (expr == null) return true;
            var res = await EvaluateValue(expr, context);
            return res is bool b && b;
        }

        /// <summary>Identifies columns from a specific table alias that are involved in equality filters (for indexing).</summary>
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

        /// <summary>Checks if an identifier belongs to a specific table alias.</summary>
        private bool IsFromAlias(string identifier, string? alias)
        {
            if (string.IsNullOrEmpty(alias)) return true;
            if (identifier.Contains(".")) return identifier.StartsWith(alias + ".", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        /// <summary>Extracts the column name from a potentially qualified identifier (e.g., 'T.Col' -> 'Col').</summary>
        private string GetColumnName(string identifier)
        {
            int dot = identifier.IndexOf('.');
            return dot >= 0 ? identifier.Substring(dot + 1) : identifier;
        }

        /// <summary>Casts a value to a specific SQL data type.</summary>
        public object? CastToType(object? value, string dataType) => _expressionEvaluator.CastToType(value, dataType);

        /// <summary>Checks if a connection refers to a physical database that supports SQL pushdown.</summary>
        public bool IsSqlPushdown(string conn) => !string.Equals(conn, "DUAL", StringComparison.OrdinalIgnoreCase) && _connections.TryGetValue(conn, out var ds) && ds is IDatabaseSource db && db.SupportsSqlPushdown;

        /// <summary>Attempts to extract an INTO target from a SELECT or SET operation.</summary>
        public TableReference? GetIntoTable(Statement stmt)
        {
            if (stmt is SelectStatement s) return s.IntoTable;
            if (stmt is SetOperationStatement setOp) return GetIntoTable(setOp.Left);
            return null;
        }

        /// <summary>Starts a new transaction or increments the nesting level.</summary>
        public async Task BeginTransaction()
        {
            await _transactionManager.BeginTransaction(_variableScopeManager.GlobalVariables, _connections);
        }

        /// <summary>Commits the current transaction or decrements the nesting level.</summary>
        public async Task CommitTransaction()
        {
            await _transactionManager.CommitTransaction();
        }

        /// <summary>Rolls back the current transaction scope.</summary>
        public async Task RollbackTransaction(string? name = null)
        {
            await _transactionManager.RollbackTransaction(_variableScopeManager.GlobalVariables, _connections);
        }

        /// <summary>Rolls back all nested transactions.</summary>
        public async Task RollbackAll()
        {
            await _transactionManager.RollbackAll(_variableScopeManager.GlobalVariables, _connections);
        }

        /// <summary>Logs a message to the internal message buffer (used when RedirectOutput is true).</summary>
        public void Log(string message, ConsoleColor color = ConsoleColor.White)
        {
            Messages.Add(message);
            if (Messages.Count > MaxMessages)
            {
                Messages.RemoveAt(0);
                if (Messages.Count > 0 && !Messages[0].StartsWith("[TRUNCATED]"))
                {
                    Messages[0] = "[TRUNCATED] " + Messages[0];
                }
            }
        }

        /// <summary>Resolves a logical connection/file path to a physical disk path.</summary>
        public string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // Check if path starts with a connection name
            var parts = path.Split(new[] { '/', '\\' }, 2);
            var connName = parts[0];

            if (_connections.TryGetValue(connName, out var ds))
            {
                var baseUri = ds.Path;
                if (!string.IsNullOrEmpty(baseUri) && baseUri != "MSSQL" && baseUri != "POSTGRES" && baseUri != "MYSQL" && baseUri != "SQLITE" && baseUri != "ORACLE")
                {
                    if (parts.Length > 1) return Path.Combine(baseUri, parts[1]);
                    return baseUri;
                }
            }

            return path;
        }

        /// <summary>Resolves an identifier to a value from a row or the variable scope.</summary>
        public object? ResolveIdentifier(string name, Row? row) => _variableScopeManager.ResolveIdentifier(name, row);

        /// <summary>Gracefully shuts down the engine and releases all data source resources.</summary>
        public async ValueTask DisposeAsync()
        {
            foreach (var conn in _connections.Values)
            {
                await conn.DisposeAsync();
            }
            await DockerManager.DisposeAsync();
            _connections.Clear();
        }

        /// <summary>Validates a check constraint expression against a row of data.</summary>
        public async Task<bool> ValidateCheckConstraint(Expression expression, Row row)
        {
            var result = await _expressionEvaluator.Evaluate(expression, row);
            return result != null && Convert.ToBoolean(result);
        }

        /// <summary>Validates that a foreign key reference exists in the target table.</summary>
        public async Task<bool> ValidateForeignKey(ForeignKeyReference reference, List<string> sourceColumns, Row row)
        {
            string connName = reference.Table.ConnectionName ?? reference.Table.TableName;
            if (!_connections.TryGetValue(connName, out var dataSource) || dataSource is not InMemoryDataSource targetTable)
                return true; 

            var sourceValues = sourceColumns.Select(col => row[col]).ToList();
            if (sourceValues.All(v => v == null || v == DBNull.Value)) return true;

            await foreach (var batch in targetTable.ReadBatches())
            {
                foreach (var targetRow in batch.Rows)
                {
                    bool allMatch = true;
                    for (int i = 0; i < sourceValues.Count; i++)
                    {
                        var targetVal = targetRow[reference.Columns[i]];
                        if (!IsSoftEqual(sourceValues[i], targetVal))
                        {
                            allMatch = false;
                            break;
                        }
                    }
                    if (allMatch) return true;
                }
            }

            return false;
        }
        public IExecutionContext Fork()
        {
            // Resolve fresh handlers to avoid sharing mutable handler state across forks (resolves Item #20 race condition)
            var freshHandlers = _serviceProvider.GetServices<IStatementHandler>();
            var fork = new Evaluator(freshHandlers, _serviceProvider, FunctionRegistry, LineageTracker, DockerManager, _connectorRegistry, SessionStateManager, _connections, _variableScopeManager.Fork())
            {
                IsVerbose = IsVerbose,
                RedirectOutput = RedirectOutput,
                IsProfiling = IsProfiling,
                IsWhatIf = IsWhatIf,
                ShowPassword = ShowPassword,
                BatchSize = BatchSize,
                PreviewLimit = PreviewLimit,
                ScriptPassword = ScriptPassword,
                SessionId = SessionId
            };
            return fork;
        }

        public void Merge(IExecutionContext spawned)
        {
            if (spawned is Evaluator eval)
            {
                _variableScopeManager.Merge(eval._variableScopeManager);
            }

            // Sync result sets
            lock (_lastResultSetsLock)
            {
                LastResultSets.AddRange(spawned.LastResultSets);
                if (spawned.LastResult != null) LastResult = spawned.LastResult;
            }

            // Sync messages
            lock (_messagesLock)
            {
                foreach (var msg in spawned.Messages) Log(msg);
            }

            // Sync row counts
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
    }
}
