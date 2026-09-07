using Microsoft.Extensions.Logging;

namespace ETL_SQL.Common;

/// <summary>Routes host events and scopes to the engine's sanitized application file sink.</summary>
public sealed class ApplicationLoggerProvider(LoggerService logger) : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new HostLogger(this, categoryName);
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
    public void Dispose() { } // LoggerService is owned by the host container.

    private sealed class HostLogger(ApplicationLoggerProvider provider, string category) : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider._scopes.Push(state);
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => level != Microsoft.Extensions.Logging.LogLevel.None;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => provider.Write(category, logLevel, eventId, state, exception);
    }

    private void Write<TState>(string category, Microsoft.Extensions.Logging.LogLevel level, EventId eventId,
        TState state, Exception? exception) => logger.WriteHostEvent(category, level, eventId, state, exception, _scopes);
}
