using System;
using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser.Components;

/// <summary>
/// <c>SCHEDULE</c> and <c>NOTIFICATION</c> — peer entities to <c>JOB</c>, plus the
/// <c>ALTER JOB … ADD|REMOVE</c> attachments that link them. See
/// <c>docs/architecture/decisions/job_schedule_notification.md</c>.
/// </summary>
/// <remarks>
/// <c>NOTIFICATION</c>, <c>REMOVE</c>, <c>CRON</c>, and the trigger words are matched contextually as
/// identifiers rather than promoted to keywords. Adding a reserved word is not free — the lexer maps
/// every keyword string to a token everywhere, so <c>SELECT success FROM …</c> would stop parsing.
/// These names are all plausible column names, and none of them needs to be reserved to be
/// unambiguous in the positions used here.
/// </remarks>
public class CatalogParser : ParserComponent
{
    public CatalogParser(IParser parser, StatementParser parent) : base(parser, parent) { }

    private const string TriggerWords = "SUCCESS, FAILURE, or COMPLETION";

    /// <summary>True when the next token begins a <c>NOTIFICATION</c> clause.</summary>
    public bool IsNotificationKeyword() => IsIdentifier("NOTIFICATION");

    // ── CREATE ────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>CREATE [OR ALTER|OR REPLACE] SCHEDULE &lt;name&gt; ON '&lt;cron&gt;'
    /// [AT TIME ZONE '&lt;tz&gt;'] [WITH (…)];</c>
    /// </summary>
    public Statement ParseCreateSchedule(Token startToken, ObjectCreationMode mode)
    {
        var name = ConsumeIdentifier("Expected a schedule name after CREATE SCHEDULE").Value;

        // ON carries the cron expression. It reads as "runs on this cadence" and matches
        // CREATE JOB's existing ON SCHEDULE spelling.
        Consume(TokenType.ON, $"Expected ON '<cron>' after the schedule name in CREATE SCHEDULE {name}");
        var cron = Consume(TokenType.STRING_LITERAL,
            "Expected a cron expression in quotes, e.g. ON '0 2 * * *'").Value;

        string? timeZone = ParseOptionalTimeZone();
        var metadata = ParseOptionalWithMetadata("CREATE SCHEDULE");
        Match(TokenType.SEMICOLON);

        return new CreateScheduleStatement
        {
            Name = name,
            Cron = cron,
            TimeZone = timeZone,
            Metadata = metadata,
            Mode = mode,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    /// <summary>
    /// <c>CREATE [OR ALTER|OR REPLACE] NOTIFICATION &lt;name&gt; USING &lt;connection&gt;
    /// [TO '&lt;recipient&gt;'] [WITH (…)];</c>
    /// </summary>
    public Statement ParseCreateNotification(Token startToken, ObjectCreationMode mode)
    {
        var name = ConsumeIdentifier("Expected a notification name after CREATE NOTIFICATION").Value;

        Consume(TokenType.USING,
            $"Expected USING <connection> after the notification name in CREATE NOTIFICATION {name}");
        var connection = ConsumeIdentifier("Expected a connection name after USING").Value;

        string? recipient = null;
        if (Match(TokenType.TO))
            recipient = Consume(TokenType.STRING_LITERAL,
                "Expected a recipient in quotes after TO, e.g. TO 'ops@example.com'").Value;

        var metadata = ParseOptionalWithMetadata("CREATE NOTIFICATION");
        Match(TokenType.SEMICOLON);

        return new CreateNotificationStatement
        {
            Name = name,
            ConnectionName = connection,
            Recipient = recipient,
            Metadata = metadata,
            Mode = mode,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    // ── ALTER ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>ALTER SCHEDULE &lt;name&gt; SET CRON = '…' | SET TIME ZONE '…' | SET (…);</c>
    /// </summary>
    public Statement ParseAlterSchedule(Token startToken)
    {
        var name = ConsumeIdentifier("Expected a schedule name after ALTER SCHEDULE").Value;

        string? cron = null, timeZone = null;
        var metadata = new CatalogObjectOptions();
        var sawClause = false;

        while (Match(TokenType.SET))
        {
            sawClause = true;
            if (MatchIdentifier("CRON"))
            {
                Match(TokenType.EQUALS);
                cron = Consume(TokenType.STRING_LITERAL, "Expected a cron expression in quotes after SET CRON").Value;
            }
            else if (Match(TokenType.TIME))
            {
                Consume(TokenType.ZONE, "Expected ZONE after SET TIME");
                Match(TokenType.EQUALS);
                timeZone = Consume(TokenType.STRING_LITERAL, "Expected a time zone in quotes after SET TIME ZONE").Value;
            }
            else if (_parser.Current.Type == TokenType.LPAREN)
            {
                metadata = ParseMetadataBody("ALTER SCHEDULE");
            }
            else
            {
                throw new SyntaxException(
                    $"Expected CRON, TIME ZONE, or '(' after SET in ALTER SCHEDULE {name}.",
                    _parser.Current.Line, _parser.Current.Column);
            }
            Match(TokenType.COMMA);
        }

        if (!sawClause)
            throw new SyntaxException(
                $"ALTER SCHEDULE {name} needs at least one SET clause — SET CRON = '…', " +
                "SET TIME ZONE '…', or SET (DISPLAY_NAME = '…').",
                startToken.Line, startToken.Column);

        Match(TokenType.SEMICOLON);
        return new AlterCatalogObjectStatement
        {
            Kind = CatalogObjectKind.Schedule,
            Name = name,
            Cron = cron,
            TimeZone = timeZone,
            Metadata = metadata,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    /// <summary>
    /// <c>ALTER NOTIFICATION &lt;name&gt; SET TO '…' | SET USING &lt;connection&gt; | SET (…);</c>
    /// </summary>
    public Statement ParseAlterNotification(Token startToken)
    {
        var name = ConsumeIdentifier("Expected a notification name after ALTER NOTIFICATION").Value;

        string? connection = null, recipient = null;
        var metadata = new CatalogObjectOptions();
        var sawClause = false;

        while (Match(TokenType.SET))
        {
            sawClause = true;
            if (Match(TokenType.TO))
            {
                Match(TokenType.EQUALS);
                recipient = Consume(TokenType.STRING_LITERAL, "Expected a recipient in quotes after SET TO").Value;
            }
            else if (Match(TokenType.USING))
            {
                Match(TokenType.EQUALS);
                connection = ConsumeIdentifier("Expected a connection name after SET USING").Value;
            }
            else if (_parser.Current.Type == TokenType.LPAREN)
            {
                metadata = ParseMetadataBody("ALTER NOTIFICATION");
            }
            else
            {
                throw new SyntaxException(
                    $"Expected TO, USING, or '(' after SET in ALTER NOTIFICATION {name}.",
                    _parser.Current.Line, _parser.Current.Column);
            }
            Match(TokenType.COMMA);
        }

        if (!sawClause)
            throw new SyntaxException(
                $"ALTER NOTIFICATION {name} needs at least one SET clause — SET TO '…', " +
                "SET USING <connection>, or SET (DISPLAY_NAME = '…').",
                startToken.Line, startToken.Column);

        Match(TokenType.SEMICOLON);
        return new AlterCatalogObjectStatement
        {
            Kind = CatalogObjectKind.Notification,
            Name = name,
            ConnectionName = connection,
            Recipient = recipient,
            Metadata = metadata,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    /// <summary>
    /// The attachment half of <c>ALTER JOB</c>:
    /// <c>ALTER JOB &lt;job&gt; ADD|REMOVE SCHEDULE &lt;name&gt;</c> and
    /// <c>… ADD|REMOVE NOTIFICATION &lt;name&gt; ON SUCCESS|FAILURE|COMPLETION</c>.
    /// </summary>
    public Statement ParseAlterJobAttachment(Token startToken, string jobName, JobAttachmentAction action)
    {
        if (Match(TokenType.SCHEDULE))
        {
            var scheduleName = ConsumeIdentifier(
                $"Expected a schedule name after {action.ToString().ToUpperInvariant()} SCHEDULE").Value;
            Match(TokenType.SEMICOLON);
            return new AlterJobAttachmentStatement
            {
                Action = action,
                Kind = CatalogObjectKind.Schedule,
                JobName = jobName,
                TargetName = scheduleName,
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        if (MatchIdentifier("NOTIFICATION"))
        {
            var notificationName = ConsumeIdentifier(
                $"Expected a notification name after {action.ToString().ToUpperInvariant()} NOTIFICATION").Value;

            // The outcome is required, not defaulted. "Notify me about this job" has no single
            // obvious meaning, and guessing one would silently deliver on outcomes nobody asked for.
            Consume(TokenType.ON,
                $"Expected ON {TriggerWords} after the notification name — a notification fires on a " +
                "specific outcome, so the outcome is required.");
            var trigger = ParseTriggerCondition();

            Match(TokenType.SEMICOLON);
            return new AlterJobAttachmentStatement
            {
                Action = action,
                Kind = CatalogObjectKind.Notification,
                JobName = jobName,
                TargetName = notificationName,
                Trigger = trigger,
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        throw new SyntaxException(
            $"Expected SCHEDULE or NOTIFICATION after ALTER JOB {jobName} " +
            $"{action.ToString().ToUpperInvariant()}.",
            _parser.Current.Line, _parser.Current.Column);
    }

    // ── DROP / ENABLE / DISABLE ───────────────────────────────────────────────

    /// <summary><c>DROP SCHEDULE|NOTIFICATION [IF EXISTS] &lt;name&gt;;</c></summary>
    public Statement ParseDropCatalogObject(Token startToken, CatalogObjectKind kind)
    {
        var keyword = kind.ToString().ToUpperInvariant();
        var ifExists = false;
        if (Match(TokenType.IF))
        {
            Consume(TokenType.EXISTS, $"Expected EXISTS after IF in DROP {keyword}");
            ifExists = true;
        }

        var name = ConsumeIdentifier($"Expected a name after DROP {keyword}").Value;
        RejectTrailingIfExists(keyword, name);
        Match(TokenType.SEMICOLON);

        return new DropCatalogObjectStatement
        {
            Kind = kind,
            Name = name,
            IfExists = ifExists,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    /// <summary><c>ENABLE|DISABLE SCHEDULE|NOTIFICATION &lt;name&gt;;</c></summary>
    public Statement ParseSetCatalogObjectEnabled(Token startToken, CatalogObjectKind kind, bool isEnabled)
    {
        var name = ConsumeIdentifier(
            $"Expected a name after {(isEnabled ? "ENABLE" : "DISABLE")} {kind.ToString().ToUpperInvariant()}").Value;
        Match(TokenType.SEMICOLON);

        return new SetCatalogObjectEnabledStatement
        {
            Kind = kind,
            Name = name,
            IsEnabled = isEnabled,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    // ── Shared clauses ────────────────────────────────────────────────────────

    /// <summary>
    /// Optional <c>AT TIME ZONE '&lt;id&gt;'</c>. Distinguished from a target-connection <c>AT</c> by
    /// the two-token lookahead the expression parser already uses, so the two never collide.
    /// </summary>
    private string? ParseOptionalTimeZone()
    {
        if (_parser.Current.Type != TokenType.AT) return null;
        if (_parser.Peek.Type != TokenType.TIME || _parser.Peek2.Type != TokenType.ZONE) return null;

        Advance();  // AT
        Advance();  // TIME
        Advance();  // ZONE
        return Consume(TokenType.STRING_LITERAL,
            "Expected a time zone in quotes after AT TIME ZONE, e.g. 'America/New_York'").Value;
    }

    private string ParseTriggerCondition()
    {
        foreach (var trigger in new[] { "SUCCESS", "FAILURE", "COMPLETION" })
            if (MatchIdentifier(trigger)) return trigger;

        throw new SyntaxException(
            $"Expected {TriggerWords} after ON, but found '{_parser.Current.Value}'.",
            _parser.Current.Line, _parser.Current.Column);
    }

    private CatalogObjectOptions ParseOptionalWithMetadata(string statement) =>
        Match(TokenType.WITH) ? ParseMetadataBody(statement) : new CatalogObjectOptions();

    /// <summary>
    /// <c>(DISPLAY_NAME = '…', DESCRIPTION = '…', &lt;key&gt; = '…')</c>. Everything here is
    /// presentation or classification and is never read by the scheduler, which is what allows it to
    /// be freely editable while the object's name stays fixed.
    /// </summary>
    private CatalogObjectOptions ParseMetadataBody(string statement)
    {
        Consume(TokenType.LPAREN, $"Expected '(' after WITH in {statement}");

        string? displayName = null, description = null;
        Dictionary<string, string>? options = null;

        while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
        {
            var keyToken = Advance();
            var key = keyToken.Value.ToUpperInvariant();
            Consume(TokenType.EQUALS, $"Expected '=' after '{keyToken.Value}' in {statement}");
            var value = Consume(TokenType.STRING_LITERAL,
                $"Expected a quoted value for '{keyToken.Value}' in {statement}").Value;

            switch (key)
            {
                case "DISPLAY_NAME": displayName = value; break;
                case "DESCRIPTION": description = value; break;
                default:
                    options ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    options[key] = value;
                    break;
            }

            if (!Match(TokenType.COMMA)) break;
        }

        Consume(TokenType.RPAREN, $"Expected ')' to close the WITH options in {statement}");
        return new CatalogObjectOptions
        {
            DisplayName = displayName,
            Description = description,
            Options = options
        };
    }

    /// <summary>
    /// Existence modifiers precede the object name for every kind. Catching the post-name spelling
    /// here keeps these statements consistent with the sixteen DROP kinds that already reject it.
    /// </summary>
    private void RejectTrailingIfExists(string objectKind, string name)
    {
        if (_parser.Current.Type != TokenType.IF || _parser.Peek.Type != TokenType.EXISTS) return;

        throw new SyntaxException(
            $"IF EXISTS must come before the object name. Use 'DROP {objectKind} IF EXISTS {name}'.",
            _parser.Current.Line, _parser.Current.Column);
    }
}
