using System;
using System.Collections.Generic;

namespace ETL_SQL.Tests.Reporting.CascadingSlicers;

public enum DescendantResetBehavior
{
    RetainIfEligibleElseResetToFirst,
    RetainIfEligibleElseResetToNull,
    AlwaysResetToFirst,
    AlwaysResetToNull,
    RetainValueEvenIfInvalid
}

public record CascadingStateSnapshot(
    IReadOnlyDictionary<string, string?> ParameterValues,
    IReadOnlyDictionary<string, IReadOnlyList<string>> EligibleOptionSets);

public record SetParameterAction(
    string ParameterName,
    string? NewValue);

public record StateTransitionScenario(
    string ScenarioId,
    string Title,
    string FixtureFile,
    CascadingStateSnapshot InitialState,
    SetParameterAction TriggerAction,
    IReadOnlyList<string> ExpectedInvalidatedParameters,
    CascadingStateSnapshot ExpectedFinalState,
    int ExpectedQueryRefreshCount,
    DescendantResetBehavior ResetPolicy,
    bool IsSupportedToday,
    string StatusExplanation);
