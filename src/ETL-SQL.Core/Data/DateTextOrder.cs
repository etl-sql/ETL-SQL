using System;
using System.Threading;

namespace ETL_SQL.Core.Data;

/// <summary>How a slash date such as <c>08/01/2026</c> is read: month first (1 August) or day first.</summary>
public enum DateOrder
{
    /// <summary><c>MDY</c>: 08/01/2026 is 1 August, as SQL Server (us_english) and PostgreSQL read it by default.</summary>
    MonthFirst,

    /// <summary><c>DMY</c>: 08/01/2026 is 8 January.</summary>
    DayFirst
}

/// <summary>
/// The date order that text read as a date follows. A script sets it with <c>SET DATEFORMAT</c>;
/// <c>Engine:DateFormat</c> in appsettings sets the default (MDY when absent). It never depends on
/// the host's locale. ISO text (<c>2026-08-01</c>) reads the same under either order.
/// Entry points bind their session with <see cref="Bind"/>.
/// </summary>
public static class DateTextOrder
{
    private static readonly AsyncLocal<IExecutionContext?> Session = new();

    /// <summary>
    /// The order where no script is bound: MDY. Every entry point that runs a script or builds its
    /// report binds the session, whose order starts from <c>Engine:DateFormat</c>. Deliberately not
    /// settable: a process-wide mutable default would leak between concurrent sessions.
    /// </summary>
    public const DateOrder Default = DateOrder.MonthFirst;

    /// <summary>The bound script's order (it can change mid-script), otherwise the default.</summary>
    public static DateOrder Current => Session.Value?.DateOrder ?? Default;

    /// <summary>
    /// Makes <paramref name="context"/>'s order the one text dates follow in this asynchronous flow.
    /// Call it at the top of an entry point (running a script, building its report); the binding
    /// ends when that async method returns.
    /// </summary>
    public static void Bind(IExecutionContext? context) => Session.Value = context;

    /// <summary>Reads <c>MDY</c> or <c>DMY</c> (any case).</summary>
    public static bool TryParse(string? text, out DateOrder order)
    {
        switch (text?.Trim().ToUpperInvariant())
        {
            case "MDY": order = DateOrder.MonthFirst; return true;
            case "DMY": order = DateOrder.DayFirst; return true;
            default: order = DateOrder.MonthFirst; return false;
        }
    }

    /// <summary>The setting's name for an order: <c>MDY</c> or <c>DMY</c>.</summary>
    public static string Name(DateOrder order) => order == DateOrder.DayFirst ? "DMY" : "MDY";
}
