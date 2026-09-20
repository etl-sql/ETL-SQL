using ETL_SQL.Analysis.Services;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class PipelineTaskKindCompatibilityTests
{
    /// <summary>
    /// The kind name an older client still sends keeps working.
    /// </summary>
    /// <remarks>
    /// <c>fileoperation</c> meant COPY FILE when copying was the only file verb the canvas had. A
    /// client pinned to that build is still out there, and the parse used to fall back to an
    /// execution task for anything it did not recognise — so a stale chip would have gone on
    /// "working" while writing a completely different statement.
    /// </remarks>
    [Fact]
    public void TheRetiredKindNameStillMeansWhatItMeant()
    {
        Assert.Equal(PipelineTaskKind.CopyFile, PipelineTaskKinds.Parse("fileoperation"));
        Assert.Equal(PipelineTaskKind.CopyFile, PipelineTaskKinds.Parse("FileOperation"));
        Assert.Null(PipelineTaskKinds.Parse("not_a_kind"));

        // Naming nothing is not the same as naming something wrong. An absent kind is the default
        // the draft record already carries, and nothing is written on it alone: an execution draft
        // with no connection and no body is refused for those.
        Assert.Equal(PipelineTaskKind.Execution, PipelineTaskKinds.Parse(null));
        Assert.Equal(PipelineTaskKind.Execution, PipelineTaskKinds.Parse("  "));
    }
}
