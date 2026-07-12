using System.Runtime.InteropServices;
using System.Text;

namespace ETL_SQL.Core.Governance;

public interface IBootstrapSecurityEventSink
{
    void Emit(SecurityEvent securityEvent);
}

public sealed class BootstrapSecurityEventReporter(
    IEnumerable<IBootstrapSecurityEventSink> sinks)
{
    private readonly IBootstrapSecurityEventSink[] _sinks =
        sinks?.ToArray() ?? throw new ArgumentNullException(nameof(sinks));

    public void Report(string stage, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentNullException.ThrowIfNull(exception);
        var securityEvent = SecurityEventSanitizer.Sanitize(SecurityEventContract.Create(
            SecurityEventSeverity.Critical,
            SecurityEventType.PolicyAvailabilityFailure,
            "machine",
            "machine",
            stage,
            SecurityEventDecision.Failed,
            exception.Message) with
        {
            HostName = Environment.MachineName
        });

        foreach (var sink in _sinks)
        {
            try { sink.Emit(securityEvent); }
            catch { /* Bootstrap reporting must never replace the startup failure. */ }
        }
    }

    public static BootstrapSecurityEventReporter CreateDefault(string structuredFilePath)
    {
        var sinks = new List<IBootstrapSecurityEventSink>();
        if (OperatingSystem.IsWindows()) sinks.Add(new WindowsEventLogBootstrapSecurityEventSink());
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            sinks.Add(new SyslogBootstrapSecurityEventSink());
        sinks.Add(new StructuredFileBootstrapSecurityEventSink(structuredFilePath));
        return new BootstrapSecurityEventReporter(sinks);
    }
}

public sealed class StructuredFileBootstrapSecurityEventSink : IBootstrapSecurityEventSink
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _sync = new();

    public StructuredFileBootstrapSecurityEventSink(
        string path,
        long maxBytes = 5 * 1024 * 1024)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("Bootstrap security-event path must be fully qualified.", nameof(path));
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _path = Path.GetFullPath(path);
        _maxBytes = maxBytes;
    }

    public void Emit(SecurityEvent securityEvent)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        var sanitized = SecurityEventSanitizer.Sanitize(securityEvent);
        var line = SecurityEventContract.Serialize(sanitized) + Environment.NewLine;
        lock (_sync)
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Bootstrap security-event path has no parent directory.");
            Directory.CreateDirectory(directory);
            if (File.Exists(_path) && new FileInfo(_path).Length + Encoding.UTF8.GetByteCount(line) > _maxBytes)
            {
                var previous = _path + ".previous";
                File.Move(_path, previous, overwrite: true);
            }
            File.AppendAllText(_path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}

internal sealed class WindowsEventLogBootstrapSecurityEventSink : IBootstrapSecurityEventSink
{
    private const ushort ErrorType = 0x0001;

    public void Emit(SecurityEvent securityEvent)
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = RegisterEventSourceW(null, "ETL-SQL");
        if (handle == IntPtr.Zero) return;
        try
        {
            var message = SecurityEventContract.Serialize(SecurityEventSanitizer.Sanitize(securityEvent));
            ReportEventW(handle, ErrorType, 0, 1000, IntPtr.Zero, 1, 0, [message], IntPtr.Zero);
        }
        finally
        {
            DeregisterEventSource(handle);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterEventSourceW(string? serverName, string sourceName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReportEventW(
        IntPtr eventLog,
        ushort type,
        ushort category,
        uint eventId,
        IntPtr userSid,
        ushort stringCount,
        uint dataSize,
        string[] strings,
        IntPtr rawData);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeregisterEventSource(IntPtr eventLog);
}

internal sealed class SyslogBootstrapSecurityEventSink : IBootstrapSecurityEventSink
{
    private const int LogAuth = 4 << 3;
    private const int LogCritical = 2;

    public void Emit(SecurityEvent securityEvent)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        var message = SecurityEventContract.Serialize(SecurityEventSanitizer.Sanitize(securityEvent));
        OpenLog("etl-sql", 0, LogAuth);
        try { Syslog(LogCritical, "%s", message); }
        finally { CloseLog(); }
    }

    [DllImport("libc", EntryPoint = "openlog", CharSet = CharSet.Ansi)]
    private static extern void OpenLog(string ident, int option, int facility);

    [DllImport("libc", EntryPoint = "syslog", CharSet = CharSet.Ansi)]
    private static extern void Syslog(int priority, string format, string message);

    [DllImport("libc", EntryPoint = "closelog")]
    private static extern void CloseLog();
}
