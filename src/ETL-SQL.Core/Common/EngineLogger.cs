using System;

namespace ETL_SQL.Common;
/// <summary>
/// A lightweight standalone implementation of ILogger used as a fallback or for
/// simple "Global" logging before DI is fully initialized.
/// </summary>
public class EngineLogger : ILogger
{
    private readonly string _category;

    public EngineLogger(string category = "Engine")
    {
        _category = category;
    }

    public string? SessionId { get; set; }
    public bool IsDebugEnabled => true; // Fallback assumes enabled or handled by global flags
    public bool IsVerboseEnabled => true;
    public bool IsVerbose { get; set; }
    public bool SuppressConsole { get; set; }
    public bool IsJsonMode { get; set; }
    public event Action<string, string?, ConsoleColor>? OnMessage;

    public void Log(LogLevel level, string message, Exception? ex = null)
    {
        var color = level switch
        {
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Debug => ConsoleColor.DarkGray,
            _ => ConsoleColor.White
        };
        WriteToConsole(level.ToString().ToUpper(), message, color, ex);
    }

    public void WriteLine(string message, ConsoleColor color = ConsoleColor.White)
    {
        WriteToConsole("INFO", message, color);
    }

    private void WriteToConsole(string level, string message, ConsoleColor color, Exception? ex = null)
    {
        var sid = SessionId != null ? $" [{SessionId}]" : "";
        var safeMessage = ETL_SQL.Core.Common.SecretRedactor.Redact(message) ?? string.Empty;
        var safeException = ETL_SQL.Core.Common.SecretRedactor.RedactException(ex);
        string formattedMessage = $"[{level}] [{_category}]{sid} {safeMessage}";
        if (safeException != null) formattedMessage += $"{Environment.NewLine}Exception: {safeException.Message}";

        // Prefer a wired-up subscriber (e.g. the DI logging sink) so output is not duplicated.
        if (OnMessage != null)
        {
            OnMessage.Invoke(formattedMessage, SessionId, color);
            return;
        }

        // No subscriber: fall back to the console so diagnostics — including errors raised
        // during early startup, in tools, or in tests using the bare/Global logger — are not
        // silently dropped. JSON mode still emits a structured frame for --json consumers.
        if (IsJsonMode)
        {
            var msg = new { type = "message", level = level.ToLowerInvariant(), text = safeMessage };
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(msg));
            return;
        }

        if (SuppressConsole) return;

        if (color != ConsoleColor.White) Console.ForegroundColor = color;
        Console.WriteLine(formattedMessage);
        if (color != ConsoleColor.White) Console.ResetColor();
    }
}
