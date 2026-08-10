using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Tests.Multitenancy;

/// <summary>
/// A shared, tenant-aware surface: anything that serves more than one tenant from one store, queue,
/// cache, index, or endpoint.
/// </summary>
public interface ISharedTenantSurface
{
    /// <summary>Writes a value under a logical id, scoped to the caller's server-derived context.</summary>
    Task WriteAsync(TenantContext context, string logicalId, string value);

    /// <summary>
    /// Reads by a <em>caller-supplied</em> identifier. Implementations must treat that identifier as
    /// untrusted: it may name another tenant's row, and must not be allowed to select one.
    /// </summary>
    Task<string?> ReadAsync(TenantContext context, string callerSuppliedId);

    /// <summary>Enumerates what this tenant can see.</summary>
    Task<IReadOnlyList<string>> ListAsync(TenantContext context);
}

/// <summary>
/// The cross-tenant negative contract every shared surface must satisfy before it ships
/// (SaaS isolation domain 1, Shared half; SaaSTenantIsolation.md §6.1–6.2).
/// </summary>
/// <remarks>
/// <para>
/// This exists ahead of the surfaces it governs, and deliberately so. The product is host-fixed
/// today — one deployment per tenant — so there is currently nothing shared to point these tests at.
/// The moment a shared control plane, store, queue, or index appears, its test class inherits this
/// and cannot ship without answering all of it. Writing the guard after the first shared surface is
/// how a boundary gets certified by whoever is in a hurry.
/// </para>
/// <para>
/// Modelled on <c>ArtifactStorageContractTests</c>, which already works this way here: one abstract
/// contract, one concrete subclass per implementation.
/// </para>
/// </remarks>
public abstract class SharedTenantSurfaceContractTests
{
    protected abstract ISharedTenantSurface CreateSurface();

    private static TenantContext Acme => TenantContext.FromHostConfiguration("acme");
    private static TenantContext Globex => TenantContext.FromHostConfiguration("globex");

    [Fact]
    public async Task ACallerSuppliedIdentifierNamingAnotherTenantsRowCannotReadIt()
    {
        var surface = CreateSurface();
        await surface.WriteAsync(Acme, "secret-report", "acme numbers");

        // Globex knows the exact scoped identifier and asks for it directly. Knowledge of an
        // identifier must never be authority over it.
        var stolen = await ReadOrRefusedAsync(surface, Globex, Acme.ScopeKey("secret-report"));

        Assert.Null(stolen);
    }

    [Fact]
    public async Task AnUnscopedIdentifierDoesNotResolveAcrossTenants()
    {
        var surface = CreateSurface();
        await surface.WriteAsync(Acme, "shared-name", "acme numbers");

        // Both tenants use the same logical name; the bare name must not reach the other's row.
        var leaked = await ReadOrRefusedAsync(surface, Globex, "shared-name");

        Assert.Null(leaked);
    }

    [Fact]
    public async Task EqualLogicalIdsInDifferentTenantsDoNotCollide()
    {
        var surface = CreateSurface();
        await surface.WriteAsync(Acme, "job/1", "acme job");
        await surface.WriteAsync(Globex, "job/1", "globex job");

        Assert.Equal("acme job", await surface.ReadAsync(Acme, Acme.ScopeKey("job/1")));
        Assert.Equal("globex job", await surface.ReadAsync(Globex, Globex.ScopeKey("job/1")));
    }

    [Fact]
    public async Task AWriteFromOneTenantDoesNotOverwriteAnothersRowOfTheSameName()
    {
        var surface = CreateSurface();
        await surface.WriteAsync(Acme, "nightly", "acme");
        await surface.WriteAsync(Globex, "nightly", "globex");

        Assert.Equal("acme", await surface.ReadAsync(Acme, Acme.ScopeKey("nightly")));
    }

    [Fact]
    public async Task EnumerationReturnsOnlyTheCallersOwnRows()
    {
        var surface = CreateSurface();
        await surface.WriteAsync(Acme, "a1", "acme");
        await surface.WriteAsync(Acme, "a2", "acme");
        await surface.WriteAsync(Globex, "g1", "globex");

        var globexRows = await surface.ListAsync(Globex);

        Assert.Single(globexRows);
        Assert.DoesNotContain(globexRows, row => row.Contains("acme", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATenantPrefixThatMerelyLooksLikeAnothersIsNotTreatedAsIt()
    {
        var surface = CreateSurface();
        var acmeEvil = TenantContext.FromHostConfiguration("acme-evil");
        await surface.WriteAsync(Acme, "report", "acme numbers");

        var leaked = await ReadOrRefusedAsync(surface, acmeEvil, Acme.ScopeKey("report"));

        Assert.Null(leaked);
    }

    /// <summary>
    /// A surface may refuse either by returning nothing or by throwing. Both are correct; silently
    /// returning another tenant's data is the only wrong answer, so the contract accepts both shapes
    /// rather than forcing an implementation to pick one.
    /// </summary>
    private static async Task<string?> ReadOrRefusedAsync(
        ISharedTenantSurface surface, TenantContext context, string callerSuppliedId)
    {
        try
        {
            return await surface.ReadAsync(context, callerSuppliedId);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// A reference shared surface, present so the contract above is proven executable rather than
/// aspirational, and so the first real implementation has a worked example of scoping every access
/// through the server-derived context.
/// </summary>
internal sealed class InMemorySharedTenantSurface : ISharedTenantSurface
{
    private readonly Dictionary<string, string> _rows = new(StringComparer.Ordinal);

    public Task WriteAsync(TenantContext context, string logicalId, string value)
    {
        _rows[context.ScopeKey(logicalId)] = value;
        return Task.CompletedTask;
    }

    public Task<string?> ReadAsync(TenantContext context, string callerSuppliedId)
    {
        // The caller's identifier is checked against the context before it is used as a key. Doing
        // it the other way round -- look up, then check what came back -- is the bug this contract
        // is written to catch.
        var owned = context.RequireOwned(
            callerSuppliedId.Contains('/', StringComparison.Ordinal)
                ? callerSuppliedId
                : context.ScopeKey(callerSuppliedId),
            "identifier");
        return Task.FromResult(_rows.GetValueOrDefault(owned));
    }

    public Task<IReadOnlyList<string>> ListAsync(TenantContext context)
    {
        var prefix = context.ScopePrefix;
        return Task.FromResult<IReadOnlyList<string>>(
            [.. _rows.Where(r => r.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(r => r.Value)]);
    }
}

public sealed class InMemorySharedTenantSurfaceTests : SharedTenantSurfaceContractTests
{
    protected override ISharedTenantSurface CreateSurface() => new InMemorySharedTenantSurface();
}
