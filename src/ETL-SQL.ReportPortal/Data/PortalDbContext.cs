using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Data;

public class PortalDbContext(DbContextOptions<PortalDbContext> options)
    : IdentityDbContext<PortalUser, PortalRole, int,
        IdentityUserClaim<int>, IdentityUserRole<int>, IdentityUserLogin<int>,
        IdentityRoleClaim<int>, IdentityUserToken<int>>(options)
{
    public DbSet<Group>          Groups          => Set<Group>();
    public DbSet<UserGroup>      UserGroups      => Set<UserGroup>();
    public DbSet<Folder>         Folders         => Set<Folder>();
    public DbSet<FolderAcl>      FolderAcls      => Set<FolderAcl>();
    public DbSet<Report>         Reports         => Set<Report>();
    public DbSet<ReportSnapshot> ReportSnapshots => Set<ReportSnapshot>();
    public DbSet<Subscription>   Subscriptions   => Set<Subscription>();
    public DbSet<SmtpConnection> SmtpConnections => Set<SmtpConnection>();
    public DbSet<AuditLog>       AuditLogs       => Set<AuditLog>();
    public DbSet<DatasetJob>     DatasetJobs     => Set<DatasetJob>();
    public DbSet<RefreshToken>   RefreshTokens   => Set<RefreshToken>();
    public DbSet<Dataset>        Datasets        => Set<Dataset>();
    public DbSet<DatasetAcl>     DatasetAcls     => Set<DatasetAcl>();
    public DbSet<ReportFavorite> ReportFavorites => Set<ReportFavorite>();
    public DbSet<ReportShareLink> ReportShareLinks => Set<ReportShareLink>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserGroup>(e =>
        {
            e.HasKey(x => new { x.UserId, x.GroupId });
            e.HasOne(x => x.User).WithMany(u => u.UserGroups).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Group).WithMany(g => g.UserGroups).HasForeignKey(x => x.GroupId);
        });

        builder.Entity<FolderAcl>(e =>
        {
            e.HasOne(x => x.Folder).WithMany(f => f.Acls).HasForeignKey(x => x.FolderId);
            e.HasOne(x => x.Group).WithMany(g => g.FolderAcls).HasForeignKey(x => x.GroupId);
        });

        builder.Entity<Folder>(e =>
        {
            e.HasOne(x => x.Parent).WithMany(f => f.Children).HasForeignKey(x => x.ParentId);
            e.HasMany(x => x.Reports).WithOne(r => r.Folder).HasForeignKey(r => r.FolderId);
        });

        builder.Entity<Report>(e =>
        {
            e.HasMany(x => x.Snapshots).WithOne(s => s.Report).HasForeignKey(s => s.ReportId);
            e.HasMany(x => x.Subscriptions).WithOne(s => s.Report).HasForeignKey(s => s.ReportId);
            e.HasMany(x => x.DatasetJobs).WithOne(j => j.Report).HasForeignKey(j => j.ReportId);
            e.HasMany(x => x.ShareLinks).WithOne(l => l.Report).HasForeignKey(l => l.ReportId);
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.HasOne(x => x.User).WithMany(u => u.RefreshTokens).HasForeignKey(x => x.UserId);
        });

        builder.Entity<ReportFavorite>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.ReportId }).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.ReportFavorites).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Report).WithMany(r => r.Favorites).HasForeignKey(x => x.ReportId);
        });

        builder.Entity<ReportShareLink>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasOne(x => x.Report).WithMany(r => r.ShareLinks).HasForeignKey(x => x.ReportId);
            e.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy);
        });

        builder.Entity<SmtpConnection>(e =>
        {
            e.HasIndex(x => x.Alias).IsUnique();
        });

        builder.Entity<Group>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<Folder>(e =>
        {
            e.HasIndex(x => x.Path).IsUnique();
        });

        builder.Entity<Dataset>(e =>
        {
            e.HasOne(x => x.OwningReport).WithMany().HasForeignKey(x => x.OwningReportId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.FolderPath, x.Name }).IsUnique();
        });

        builder.Entity<DatasetAcl>(e =>
        {
            e.HasOne(x => x.Dataset).WithMany(d => d.Acls).HasForeignKey(x => x.DatasetId);
            e.HasOne(x => x.Group).WithMany(g => g.DatasetAcls).HasForeignKey(x => x.GroupId);
        });
    }
}
