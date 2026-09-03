using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Services;

/// <summary>
/// A recurring job this script already declares: the schedule, the job, and the link between them.
/// </summary>
/// <param name="Target">The script or report path the job runs. Never the open buffer — see below.</param>
public sealed record ScheduledJob(
    string Job,
    string Target,
    string TargetKind,
    IReadOnlyList<string> Schedules,
    int Line);

/// <param name="Cron">The cadence, as written.</param>
public sealed record DeclaredSchedule(string Name, string Cron, string? TimeZone, int Line);

/// <param name="CanSchedule">
/// False when the document has no saved path. A job names a path on a server, so scheduling an
/// unsaved buffer produces a job that fails on its first tick and reports a missing file.
/// </param>
public sealed record ScriptScheduleHandoff(
    bool Parsed,
    string? Error,
    bool CanSchedule,
    string? Reason,
    string? SuggestedTarget,
    string? SuggestedTargetKind,
    IReadOnlyList<DeclaredSchedule> Schedules,
    IReadOnlyList<ScheduledJob> Jobs)
{
    public static ScriptScheduleHandoff Failed(string error) =>
        new(false, error, false, null, null, null, [], []);
}

public sealed record ScheduleEditResult(bool Applied, string Script, string? Error = null, string? Job = null)
{
    public static ScheduleEditResult Ok(string script, string job) => new(true, script, null, job);
    public static ScheduleEditResult Refused(string script, string error) => new(false, script, error);
}

/// <summary>
/// Turns the document in front of the author into a recurring job, and hands off from there.
///
/// <para><b>Studio does not host schedules or subscriptions, deliberately.</b> A schedule lives in
/// the Orchestrator's catalog and a subscription lives in the Portal's, each with a permission model,
/// a history, and an operator who owns it. A workbench that listed and edited them would be a second
/// door onto both, with a weaker gate and no history — the same mistake as previewing as a named
/// user on an unsaved draft. What Studio does instead is the one thing only it can: write the
/// statements that make <em>this</em> document recurring, into the file the author is looking at,
/// and then open the Orchestrator at the job it just named.</para>
///
/// <para><b>The statements are the artifact.</b> Writing <c>CREATE SCHEDULE</c> / <c>CREATE JOB</c> /
/// <c>ALTER JOB … ADD SCHEDULE</c> into the script means the recurrence is reviewable, diffable, and
/// deployable with everything else; a button that registered a job directly would leave the fact of
/// its existence nowhere in the repository.</para>
///
/// <para><b>An unsaved document cannot be scheduled.</b> A job names a path, not a buffer, so
/// scheduling one that has never been saved produces a job that fails on its first tick with a
/// missing file — hours later, to somebody else. It is refused here instead, with the reason.</para>
/// </summary>
public sealed class ScriptScheduleHandoffService
{
    /// <summary>Cadences the panel offers, as cron. The field stays editable; cron is the language's.</summary>
    public static IReadOnlyList<(string Label, string Cron)> Cadences { get; } =
    [
        ("Every hour", "0 * * * *"),
        ("Every day at 2am", "0 2 * * *"),
        ("Every weekday at 7am", "0 7 * * 1-5"),
        ("Every Monday at 6am", "0 6 * * 1"),
        ("Every month on the 1st at 3am", "0 3 1 * *"),
    ];

    /// <summary>What this script already declares about running itself on a schedule.</summary>
    public ScriptScheduleHandoff Read(string? scriptText, string? documentPath)
    {
        var source = scriptText ?? string.Empty;
        if (!ScriptTextEditing.TryParse(source, out var ast, out var parseError))
            return ScriptScheduleHandoff.Failed(parseError);

        var schedules = ScriptTextEditing.Flatten(ast.Statements)
            .OfType<CreateScheduleStatement>()
            .Select(statement => new DeclaredSchedule(statement.Name, statement.Cron, statement.TimeZone, statement.Line))
            .ToArray();

        var attachments = ScriptTextEditing.Flatten(ast.Statements)
            .OfType<AlterJobAttachmentStatement>()
            .Where(statement => statement.Kind == CatalogObjectKind.Schedule
                && statement.Action == JobAttachmentAction.Add)
            .GroupBy(statement => statement.JobName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.TargetName).ToArray(), StringComparer.OrdinalIgnoreCase);

        var jobs = ScriptTextEditing.Flatten(ast.Statements)
            .OfType<CreateJobStatement>()
            .Select(statement => new ScheduledJob(
                statement.JobName,
                statement.TargetPath,
                statement.TargetKind.ToString().ToLowerInvariant(),
                attachments.TryGetValue(statement.JobName, out var linked) ? linked : [],
                statement.Line))
            .ToArray();

        var path = (documentPath ?? string.Empty).Trim();
        var canSchedule = path.Length > 0;
        return new ScriptScheduleHandoff(
            true,
            null,
            canSchedule,
            canSchedule ? null : "Save this document first. A job names a path on the server, so one pointing at an unsaved buffer fails on its first run — hours later, to somebody else.",
            canSchedule ? path : null,
            path.EndsWith(".rptsql", StringComparison.OrdinalIgnoreCase) ? "report" : "script",
            schedules,
            jobs);
    }

    /// <summary>
    /// Writes the three statements that make this document recurring, at the end of the script.
    /// </summary>
    /// <param name="reuseSchedule">
    /// An existing schedule name to attach instead of declaring a new one. Two jobs sharing a cadence
    /// should share the schedule that names it, or changing the cadence means finding every copy.
    /// </param>
    public ScheduleEditResult Schedule(
        string? scriptText,
        string? documentPath,
        string jobName,
        string? scheduleName,
        string? cron,
        string? timeZone,
        string? reuseSchedule)
    {
        var source = scriptText ?? string.Empty;
        var handoff = Read(source, documentPath);
        if (!handoff.Parsed) return ScheduleEditResult.Refused(source, handoff.Error!);
        if (!handoff.CanSchedule) return ScheduleEditResult.Refused(source, handoff.Reason!);

        var job = Identifier(jobName);
        if (job is null)
            return ScheduleEditResult.Refused(source, $"'{jobName}' is not a usable job name. Use letters, digits and underscores.");
        if (handoff.Jobs.Any(existing => existing.Job.Equals(job, StringComparison.OrdinalIgnoreCase)))
            return ScheduleEditResult.Refused(source, $"This script already declares a job called '{job}'.");

        string schedule;
        string? declaration = null;
        if (!string.IsNullOrWhiteSpace(reuseSchedule))
        {
            var reused = handoff.Schedules
                .FirstOrDefault(item => item.Name.Equals(reuseSchedule!.Trim(), StringComparison.OrdinalIgnoreCase));
            if (reused is null)
                return ScheduleEditResult.Refused(source, $"This script declares no schedule called '{reuseSchedule}'.");
            schedule = reused.Name;
        }
        else
        {
            var named = Identifier(scheduleName);
            if (named is null)
                return ScheduleEditResult.Refused(source, $"'{scheduleName}' is not a usable schedule name. Use letters, digits and underscores.");
            if (string.IsNullOrWhiteSpace(cron))
                return ScheduleEditResult.Refused(source, "A schedule needs a cadence, as a cron expression.");
            if (handoff.Schedules.Any(item => item.Name.Equals(named, StringComparison.OrdinalIgnoreCase)))
                return ScheduleEditResult.Refused(source, $"This script already declares a schedule called '{named}'.");

            schedule = named;
            var zone = string.IsNullOrWhiteSpace(timeZone) ? string.Empty : $" AT TIME ZONE '{Escape(timeZone!.Trim())}'";
            declaration = $"CREATE SCHEDULE {named} ON '{Escape(cron!.Trim())}'{zone};";
        }

        var lineEnding = ScriptTextEditing.DetectLineEnding(source);
        var target = handoff.SuggestedTarget!;
        var kind = handoff.SuggestedTargetKind == "report" ? "REPORT" : "SCRIPT";

        var lines = new List<string>();
        if (declaration is not null) lines.Add(declaration);
        lines.Add($"CREATE JOB {job} FOR {kind} '{Escape(target)}';");
        lines.Add($"ALTER JOB {job} ADD SCHEDULE {schedule};");

        var text = string.Join(lineEnding, lines) + lineEnding;
        var insertAt = source.Length;
        var prefix = ScriptTextEditing.NeedsBlankLineBefore(source, insertAt) ? lineEnding : string.Empty;
        if (source.Length > 0 && !source.EndsWith('\n')) prefix = lineEnding + prefix;

        var edited = ScriptTextEditing.Splice(source, insertAt, insertAt, prefix + text);
        return ScriptTextEditing.TryParse(edited, out _, out var error)
            ? ScheduleEditResult.Ok(edited, job)
            : ScheduleEditResult.Refused(source, $"That schedule would not parse: {error}");
    }

    /// <summary>A name the lexer reads back as one token, or null.</summary>
    private static string? Identifier(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0) return null;
        if (!char.IsLetter(trimmed[0]) && trimmed[0] != '_') return null;
        return trimmed.All(character => char.IsLetterOrDigit(character) || character == '_') ? trimmed : null;
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
