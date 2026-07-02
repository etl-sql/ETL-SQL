using System;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Governance;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// Phase 1 row-level-security primitives: identity system variables, the HAS_GROUP/HAS_ROLE
    /// predicates, admin bypass, fail-closed behavior, and @@ immutability enforcement.
    /// </summary>
    public class RowLevelSecurityIdentityTests
    {
        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static ExecutionIdentity User(string name, int id, bool admin = false,
            bool adminBypass = true, string? realUser = null, params string[] groups) =>
            new()
            {
                EffectiveUser = name,
                EffectiveUserId = id,
                RealUser = realUser ?? name,
                IsAdmin = admin,
                AdminBypassesRowLevelSecurity = adminBypass,
                Groups = groups
            };

        [Fact]
        public async Task IdentityVariables_ResolveFromInjectedIdentity()
        {
            var eval = NewEvaluator();
            eval.ExecutionIdentity = User("jane", 42, groups: new[] { "Region:East" });

            await TestHelpers.Execute(eval, @"
DECLARE @u = @@CURRENT_USER;
DECLARE @id = @@CURRENT_USER_ID;
DECLARE @real = @@REAL_USER;
DECLARE @admin = @@IS_ADMIN;");

            Assert.Equal("jane", eval.GetVariable("@u"));
            Assert.Equal(42, Convert.ToInt32(eval.GetVariable("@id")));
            Assert.Equal("jane", eval.GetVariable("@real"));
            Assert.Equal(false, eval.GetVariable("@admin"));
        }

        [Fact]
        public async Task RealUser_DiffersFromEffective_UnderImpersonation()
        {
            var eval = NewEvaluator();
            eval.ExecutionIdentity = User("jane", 42, realUser: "admin.bob");

            await TestHelpers.Execute(eval, @"
DECLARE @eff = @@CURRENT_USER;
DECLARE @real = @@REAL_USER;");

            Assert.Equal("jane", eval.GetVariable("@eff"));
            Assert.Equal("admin.bob", eval.GetVariable("@real"));
        }

        [Fact]
        public async Task HasGroup_IsCaseInsensitive_AndFalseForNonMember()
        {
            var eval = NewEvaluator();
            eval.ExecutionIdentity = User("jane", 42, groups: new[] { "Region:East" });

            await TestHelpers.Execute(eval, @"
DECLARE @in = HAS_GROUP('region:EAST');
DECLARE @out = HAS_GROUP('Region:West');");

            Assert.Equal(true, eval.GetVariable("@in"));
            Assert.Equal(false, eval.GetVariable("@out"));
        }

        [Fact]
        public async Task Admin_BypassesRowLevelSecurity_WhenEnabled()
        {
            var eval = NewEvaluator();
            eval.ExecutionIdentity = User("boss", 1, admin: true, adminBypass: true);

            await TestHelpers.Execute(eval, "DECLARE @r = HAS_GROUP('AnyGroupTheyAreNotIn');");

            Assert.Equal(true, eval.GetVariable("@r"));
        }

        [Fact]
        public async Task Admin_DoesNotBypass_WhenDisabled()
        {
            var eval = NewEvaluator();
            eval.ExecutionIdentity = User("boss", 1, admin: true, adminBypass: false);

            await TestHelpers.Execute(eval, "DECLARE @r = HAS_GROUP('AnyGroupTheyAreNotIn');");

            Assert.Equal(false, eval.GetVariable("@r"));
        }

        [Fact]
        public async Task NoIdentity_FailsClosed()
        {
            var eval = NewEvaluator();
            // No ExecutionIdentity injected.
            await TestHelpers.Execute(eval, @"
DECLARE @u = @@CURRENT_USER;
DECLARE @g = HAS_GROUP('Region:East');
DECLARE @admin = @@IS_ADMIN;");

            Assert.Null(eval.GetVariable("@u"));
            Assert.Equal(false, eval.GetVariable("@g"));
            Assert.Equal(false, eval.GetVariable("@admin"));
        }

        [Fact]
        public async Task SystemVariable_CannotBeAssigned()
        {
            var eval = NewEvaluator();
            eval.ExecutionIdentity = User("jane", 42);

            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                TestHelpers.Execute(eval, "SET @@CURRENT_USER = 'attacker';"));
            Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);

            // The injected identity is unchanged.
            await TestHelpers.Execute(eval, "DECLARE @u = @@CURRENT_USER;");
            Assert.Equal("jane", eval.GetVariable("@u"));
        }

        [Fact]
        public async Task SystemVariable_CannotBeDeclared()
        {
            var eval = NewEvaluator();

            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                TestHelpers.Execute(eval, "DECLARE @@CURRENT_USER = 'attacker';"));
            Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task UserGroups_TableValued_YieldsOneRowPerGroup()
        {
            var eval = NewEvaluator();
            eval.ExecutionIdentity = User("jane", 42, groups: new[] { "Region:East", "Level:Manager" });

            await TestHelpers.Execute(eval, @"
SELECT Value INTO #g FROM USER_GROUPS();
DECLARE @c = (SELECT COUNT(*) FROM #g);");

            Assert.Equal(2, Convert.ToInt32(eval.GetVariable("@c")));
        }

        [Fact]
        public async Task RlsPredicate_FiltersRows_ByUserGroupsSubquery()
        {
            var eval = NewEvaluator();
            eval.ExecutionIdentity = User("jane", 42, groups: new[] { "East" });

            await TestHelpers.Execute(eval, @"
CREATE TABLE #sales (id INT, region VARCHAR);
INSERT INTO #sales VALUES (1, 'East'), (2, 'West'), (3, 'East');
SELECT * INTO #visible FROM #sales WHERE region IN (SELECT Value FROM USER_GROUPS());
DECLARE @c = (SELECT COUNT(*) FROM #visible);");

            Assert.Equal(2, Convert.ToInt32(eval.GetVariable("@c")));
        }

        [Fact]
        public async Task UserGroups_Empty_WhenNoIdentity()
        {
            var eval = NewEvaluator();
            await TestHelpers.Execute(eval, @"
SELECT Value INTO #g FROM USER_GROUPS();
DECLARE @c = (SELECT COUNT(*) FROM #g);");

            Assert.Equal(0, Convert.ToInt32(eval.GetVariable("@c")));
        }

        [Fact]
        public async Task RlsPredicate_FiltersRows_ByGroupMembership()
        {
            var eval = NewEvaluator();
            eval.ExecutionIdentity = User("jane", 42, groups: new[] { "Region:East" });

            await TestHelpers.Execute(eval, @"
CREATE TABLE #sales (id INT, region VARCHAR);
INSERT INTO #sales VALUES (1, 'East'), (2, 'West'), (3, 'East');
SELECT * INTO #visible FROM #sales WHERE HAS_GROUP('Region:' + region);
SELECT COUNT(*) AS c INTO #cnt FROM #visible;
DECLARE @c = (SELECT c FROM #cnt);");

            Assert.Equal(2, Convert.ToInt32(eval.GetVariable("@c")));
        }
    }
}
