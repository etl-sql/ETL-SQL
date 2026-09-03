using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Governance;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Governance;

/// <summary>
/// The identity a preview-as run evaluates author predicates under.
///
/// <para>Every test here is about a way this could quietly become an escalation rather than a
/// preview. An audience that carried administrator authority would answer "all rows" whatever the
/// predicate said; one that carried a user id would compare equal to a real person in a predicate
/// written against <c>@@CURRENT_USER_ID</c>; and one that replaced the real actor would move dataset
/// and connection authority — which is keyed on the real user — onto a made-up name.</para>
/// </summary>
public class PreviewIdentityTests
{
    private static Evaluator NewEvaluator() =>
        DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

    [Fact]
    public void A_preview_audience_is_never_an_administrator()
    {
        var preview = ExecutionIdentity.Preview("a sales rep", ["Sales"], ["Reader"], "dana", "tenant-1");

        Assert.False(preview.IsAdmin);
        Assert.False(preview.AdminBypassesRowLevelSecurity);
        // Both together are the point: with either one set, a predicate asking about any group at
        // all short-circuits to true and the preview stops telling the author anything.
        Assert.False(preview.EffectiveHasGroup("Finance"));
    }

    [Fact]
    public void A_preview_audience_carries_no_user_id()
    {
        var preview = ExecutionIdentity.Preview("a sales rep", ["Sales"], null, "dana", "tenant-1");

        Assert.Null(preview.EffectiveUserId);
    }

    [Fact]
    public void The_real_actor_is_unchanged_by_a_preview()
    {
        var preview = ExecutionIdentity.Preview("a sales rep", ["Sales"], null, "dana", "tenant-1");

        Assert.Equal("dana", preview.RealUser);
        Assert.True(preview.IsImpersonating);
    }

    [Fact]
    public void The_tenant_binding_comes_from_the_caller()
    {
        var preview = ExecutionIdentity.Preview("a sales rep", null, null, "dana", "tenant-1");

        Assert.Equal("tenant-1", preview.TenantId);
    }

    [Fact]
    public void The_callers_token_ceiling_is_carried_through()
    {
        // A ceiling caps what roles and grants authorize and never grants, so carrying the caller's
        // cannot escalate — while dropping it would deny a service caller what their own run allows.
        var preview = ExecutionIdentity.Preview("a sales rep", null, null, "svc", "tenant-1", ["reports:run"]);

        Assert.Equal(["reports:run"], preview.Scopes.ToArray());
    }

    [Fact]
    public async Task Author_predicates_see_the_audience_and_the_audit_trail_sees_the_actor()
    {
        var evaluator = NewEvaluator();
        evaluator.ExecutionIdentity = ExecutionIdentity.Preview("a northern rep", ["Region:North"], null, "dana", null);

        await TestHelpers.Execute(evaluator, """
            DECLARE @north = HAS_GROUP('Region:North');
            DECLARE @south = HAS_GROUP('Region:South');
            DECLARE @who = @@CURRENT_USER;
            DECLARE @actor = @@REAL_USER;
            DECLARE @admin = @@IS_ADMIN;
            """);

        Assert.Equal(true, evaluator.GetVariable("@north"));
        Assert.Equal(false, evaluator.GetVariable("@south"));
        Assert.Equal("a northern rep", evaluator.GetVariable("@who"));
        Assert.Equal("dana", evaluator.GetVariable("@actor"));
        Assert.Equal(false, evaluator.GetVariable("@admin"));
    }

    [Fact]
    public async Task An_audience_with_no_membership_is_a_real_thing_to_preview()
    {
        // What a user in no group sees is exactly the case an author most needs to check, and it is
        // not the same as running with no identity at all — which is what the workstation host does
        // without a preview, and why an RLS-guarded report shows nothing there.
        var evaluator = NewEvaluator();
        evaluator.ExecutionIdentity = ExecutionIdentity.Preview("a new starter", [], [], "dana", null);

        await TestHelpers.Execute(evaluator, """
            DECLARE @any = HAS_GROUP('Region:North');
            DECLARE @who = @@CURRENT_USER;
            """);

        Assert.Equal(false, evaluator.GetVariable("@any"));
        Assert.Equal("a new starter", evaluator.GetVariable("@who"));
    }

    [Fact]
    public void An_unnamed_audience_still_answers_current_user_with_something()
    {
        // @@CURRENT_USER must not become null under a preview: null is what "no identity at all"
        // answers, and the two mean different things to a predicate.
        var preview = ExecutionIdentity.Preview(null, ["Sales"], null, "dana", null);

        Assert.False(string.IsNullOrWhiteSpace(preview.EffectiveUser));
    }
}
