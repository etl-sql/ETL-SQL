using System;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Spill;
using ETL_SQL.Engine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Engine.Services;
/// <summary>
/// Registry that holds and initializes the internal components of the <see cref="Evaluator"/>.
/// This improves modularity and adheres to the Single Responsibility Principle.
/// </summary>
public class EvaluatorComponentRegistry
{
    public QueryCompiler QueryCompiler { get; private set; } = null!;
    public ExecutionMetricsReporter MetricsReporter { get; private set; } = null!;
    public ExpressionEvaluator ExpressionEvaluator { get; private set; } = null!;
    public ISpillStore SpillStore { get; private set; } = null!;
    public DataSourceManager DataSourceManager { get; private set; } = null!;
    public SchemaManager SchemaManager { get; private set; } = null!;
    public ProcedureExecutor ProcedureExecutor { get; private set; } = null!;
    public IReportContext ReportContext { get; private set; } = null!;
    public ExecutionTelemetryManager TelemetryManager { get; private set; } = null!;

    /// <summary>
    /// Initializes all components with the provided context.
    /// </summary>
    public void Initialize(Evaluator evaluator, ILogger logger, VariableScopeManager variableScopeManager, IReportContext? reportContext = null, IServiceProvider? serviceProvider = null)
    {
        TelemetryManager = new ExecutionTelemetryManager();
        QueryCompiler = new QueryCompiler(evaluator);
        MetricsReporter = new ExecutionMetricsReporter(evaluator);
        ExpressionEvaluator = new ExpressionEvaluator(evaluator);
        SpillStore = new Spill.SpillStore(evaluator);
        DataSourceManager = new DataSourceManager(
            logger,
            evaluator,
            ExpressionEvaluator,
            serviceProvider?.GetService<IJobHistoryStore>(),
            serviceProvider?.GetService<IHostMetricsStore>(),
            serviceProvider?.GetService<IBundleStore>(),
            serviceProvider?.GetService<ETL_SQL.Core.Execution.ISessionStateManager>(),
            serviceProvider?.GetService<Microsoft.Extensions.Configuration.IConfiguration>(),
            serviceProvider?.GetService<ILineageCatalogStore>());
        SchemaManager = new SchemaManager(logger, evaluator, variableScopeManager);
        ProcedureExecutor = new ProcedureExecutor(variableScopeManager, evaluator);
        ReportContext = reportContext ?? new ReportRegistry(serviceProvider?.GetService<Microsoft.Extensions.Configuration.IConfiguration>());
    }
}
