using System;
using System.Collections.Generic;
using ETL_SQL.Common;
using Xunit;

namespace ETL_SQL.Tests.App
{
    /// <summary>
    /// A failure's reason must survive <c>--silent</c>.
    ///
    /// <para><see cref="ILogger.WriteLine"/> derives its log level from the console colour — red is
    /// an error, yellow a warning — and silent mode keeps only errors. That makes the colour a
    /// severity decision by accident. A lint failure printed its red "Linting failed:" header and
    /// then dropped every yellow line saying what was wrong, so a silent run reported a non-zero
    /// exit and no reason. The sample gate's <c>@expected-error</c> check reads exactly that output,
    /// which meant it could never verify a lint failure at all.</para>
    /// </summary>
    public class SilentModeDiagnosticsTests
    {
        [Theory]
        [InlineData(ConsoleColor.Red, LogLevel.Error)]
        [InlineData(ConsoleColor.Yellow, LogLevel.Warning)]
        [InlineData(ConsoleColor.White, LogLevel.Info)]
        [InlineData(ConsoleColor.Green, LogLevel.Info)]
        public void WriteLine_TakesItsSeverityFromTheColour(ConsoleColor colour, LogLevel expected)
        {
            var capture = new LevelCapturingLogger();
            ILogger logger = capture;

            logger.WriteLine("diagnostic", colour);

            Assert.Equal(expected, Assert.Single(capture.Levels));
        }

        [Fact]
        public void AFatalDiagnostic_MustNotBeWrittenAtAColourSilentModeDrops()
        {
            // The rule the lint reporter now follows: the lines explaining a fatal error go out at
            // Error, so they survive when everything below Error is suppressed.
            var capture = new LevelCapturingLogger();
            ILogger logger = capture;

            logger.Error("  - Line 46, Col 27: Variable '@backup_target' is used but not declared.");

            Assert.Equal(LogLevel.Error, Assert.Single(capture.Levels));
        }

        private sealed class LevelCapturingLogger : ILogger
        {
            public List<LogLevel> Levels { get; } = [];
            public string? SessionId { get; set; }
            public bool IsDebugEnabled => true;
            public bool IsVerboseEnabled => true;
            public bool IsVerbose { get; set; }
            public bool SuppressConsole { get; set; }
            public bool IsJsonMode { get; set; }
            public event Action<string, string?, ConsoleColor>? OnMessage;

            public void Log(LogLevel level, string message, Exception? ex = null)
            {
                Levels.Add(level);
                OnMessage?.Invoke(message, SessionId, ConsoleColor.White);
            }
        }
    }
}
