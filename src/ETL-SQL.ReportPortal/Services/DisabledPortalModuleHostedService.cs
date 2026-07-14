namespace ETL_SQL.ReportPortal.Services;

public sealed class DisabledPortalModuleHostedService(string moduleName, string serviceName) : IHostedService
{
    public string ModuleName { get; } = moduleName;
    public string ServiceName { get; } = serviceName;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
