using System.Diagnostics;
using System.Reflection;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine.Handlers;
using Xunit;

namespace ETL_SQL.Tests.Hardening;

public class ToolSecretDeliveryTests
{
    private static readonly MethodInfo AddSecret = typeof(ExecuteToolStatementHandler)
        .GetMethod("AddContainerSecret", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public void ContainerArgumentsContainOnlySecretName()
    {
        var start = new ProcessStartInfo("docker");
        var secret = "synthetic-child-secret-" + Guid.NewGuid().ToString("N");
        AddSecret.Invoke(null, [start, "TOOL_PASSWORD", secret]);
        Assert.Equal(new[] { "-e", "TOOL_PASSWORD" }, start.ArgumentList);
        Assert.Equal(secret, start.Environment["TOOL_PASSWORD"]);
        Assert.DoesNotContain(secret, string.Join(' ', start.ArgumentList));
        Assert.DoesNotContain(secret, start.Arguments);
    }

    [Theory]
    [InlineData("PASSWORD=value")]
    [InlineData("--privileged")]
    [InlineData("1PASSWORD")]
    [InlineData("BAD\nNAME")]
    public void InvalidNamesCannotTurnIntoDockerOptionsOrAssignments(string name)
    {
        var start = new ProcessStartInfo("docker");
        var exception = Assert.Throws<TargetInvocationException>(() => AddSecret.Invoke(null, [start, name, "synthetic"]));
        Assert.IsType<ExecutionException>(exception.InnerException);
        Assert.Empty(start.ArgumentList);
    }
}
