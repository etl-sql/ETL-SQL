using System;
using System.IO;
using ETL_SQL.App;
using ETL_SQL.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    /// <summary>
    /// The suite must not touch the developer's own ETL-SQL data. <c>CLEAR SESSION ALL</c> and
    /// <c>CLEAR SESSION STALE</c> delete every inactive session under the session root, and the
    /// default root is the user's <c>%LocalAppData%\ETL-SQL\Sessions</c>.
    /// </summary>
    public class TestIsolationTests
    {
        [Fact]
        public void DefaultServiceProvider_KeepsSessionsOutOfTheUsersStore()
        {
            var sessions = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<ISessionStateManager>();
            var userStore = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETL-SQL", "Sessions");

            Assert.False(
                Path.GetFullPath(sessions.SessionRoot).StartsWith(Path.GetFullPath(userStore), StringComparison.OrdinalIgnoreCase),
                $"Test sessions are written to the user's session store: {sessions.SessionRoot}");
        }
    }
}
