using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ETL_SQL.ReportPortal.Data;

public class PortalDbContext(DbContextOptions<PortalDbContext> options)
    : IdentityDbContext<PortalUser, PortalRole, int,
        IdentityUserClaim<int>, IdentityUserRole<int>, IdentityUserLogin<int>,
        IdentityRoleClaim<int>, IdentityUserToken<int>>(options)
{
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FolderAcl> FolderAcls => Set<FolderAcl>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ReportSnapshot> ReportSnapshots => Set<ReportSnapshot>();
    public DbSet<PortalExecutionJob> PortalExecutionJobs => Set<PortalExecutionJob>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionDelivery> SubscriptionDeliveries => Set<SubscriptionDelivery>();
    public DbSet<SmtpConnection> SmtpConnections => Set<SmtpConnection>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AuditOutboxMessage> AuditOutboxMessages => Set<AuditOutboxMessage>();
    public DbSet<DatasetJob> DatasetJobs => Set<DatasetJob>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Dataset> Datasets => Set<Dataset>();
    public DbSet<DatasetAcl> DatasetAcls => Set<DatasetAcl>();
    public DbSet<ReportFavorite> ReportFavorites => Set<ReportFavorite>();
    public DbSet<ReportShareLink> ReportShareLinks => Set<ReportShareLink>();
    public DbSet<ReportEmbedToken> ReportEmbedTokens => Set<ReportEmbedToken>();
    public DbSet<SavedReportView> SavedReportViews => Set<SavedReportView>();
    public DbSet<ReportAlert> ReportAlerts => Set<ReportAlert>();
    public DbSet<ServiceAccount> ServiceAccounts => Set<ServiceAccount>();
    public DbSet<PolicyVersionEntity> PolicyVersions => Set<PolicyVersionEntity>();
    public DbSet<PolicyMachineEntity> PolicyMachines => Set<PolicyMachineEntity>();
    public DbSet<PortalSecret> PortalSecrets => Set<PortalSecret>();
    public DbSet<PortalSharedConnection> PortalSharedConnections => Set<PortalSharedConnection>();
    public DbSet<SharedConnectionAcl> SharedConnectionAcls => Set<SharedConnectionAcl>();
    public DbSet<SharedConnectionUsage> SharedConnectionUsages => Set<SharedConnectionUsage>();
    public DbSet<AdminServiceRun> AdminServiceRuns => Set<AdminServiceRun>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // The protector is owned by this context's options (see PortalEncryptionOptions.UsePortalEncryption).
        // When absent (design-time/migrations) the converters pass values through as plaintext.
        var protector = ((IDbContextOptions)options)
            .FindExtension<PortalEncryptionOptionsExtension>()?.Protector;
        var piiConverter = new EncryptedDbConverter(protector);
        var piiNullableConverter = new EncryptedDbNullableConverter(protector);

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
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne(x => x.Parent).WithMany(f => f.Children).HasForeignKey(x => x.ParentId);
            e.HasMany(x => x.Reports).WithOne(r => r.Folder).HasForeignKey(r => r.FolderId);
        });

        builder.Entity<Report>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasMany(x => x.Snapshots).WithOne(s => s.Report).HasForeignKey(s => s.ReportId);
            e.HasMany(x => x.Subscriptions).WithOne(s => s.Report).HasForeignKey(s => s.ReportId);
            e.HasMany(x => x.DatasetJobs).WithOne(j => j.Report).HasForeignKey(j => j.ReportId);
            e.HasMany(x => x.ShareLinks).WithOne(l => l.Report).HasForeignKey(l => l.ReportId);
            e.HasMany(x => x.EmbedTokens).WithOne(t => t.Report).HasForeignKey(t => t.ReportId);
            e.HasMany(x => x.SavedViews).WithOne(v => v.Report).HasForeignKey(v => v.ReportId);
            e.HasMany(x => x.Alerts).WithOne(a => a.Report).HasForeignKey(a => a.ReportId);
        });

        builder.Entity<PortalExecutionJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ReportId, x.Kind })
                .IsUnique()
                .HasFilter("\"Kind\" = 'Refresh' AND \"Status\" IN ('Pending', 'Running')");
            e.HasIndex(x => x.CompletedAt);
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.HasOne(x => x.User).WithMany(u => u.RefreshTokens).HasForeignKey(x => x.UserId);
        });

        builder.Entity<ServiceAccount>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ClientId).IsUnique();
            e.HasIndex(x => x.NormalizedName).IsUnique();
            e.HasIndex(x => x.OwnerUserId);
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.ClientId).HasMaxLength(35);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.NormalizedName).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.SecretHash).HasMaxLength(512);
            e.Property(x => x.Scopes).HasMaxLength(256);
            e.Property(x => x.RoleNames).HasMaxLength(512);
            e.Property(x => x.SecurityStamp).HasMaxLength(32);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne(x => x.OwnerUser).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PortalExecutionJob>(e =>
        {
            e.Property(x => x.ActorType).HasDefaultValue("User");
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

        builder.Entity<ReportEmbedToken>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasOne(x => x.Report).WithMany(r => r.EmbedTokens).HasForeignKey(x => x.ReportId);
            e.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy);
        });

        builder.Entity<SavedReportView>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.ReportId, x.Name }).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.SavedViews).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Report).WithMany(r => r.SavedViews).HasForeignKey(x => x.ReportId);
        });

        builder.Entity<ReportAlert>(e =>
        {
            e.HasOne(x => x.Owner).WithMany(u => u.ReportAlerts).HasForeignKey(x => x.OwnerId);
            e.HasOne(x => x.Report).WithMany(r => r.Alerts).HasForeignKey(x => x.ReportId);
            e.Property(x => x.Recipient).HasConversion(piiNullableConverter);
        });

        builder.Entity<SmtpConnection>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Alias).IsUnique();
        });

        builder.Entity<PortalSecret>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
        });

        builder.Entity<PortalSharedConnection>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Alias).IsUnique();
            e.Property(x => x.Alias).HasMaxLength(200);
            e.Property(x => x.ConnectorType).HasMaxLength(100);
            e.Property(x => x.EnvironmentScope).HasMaxLength(100);
            e.Property(x => x.Target).HasConversion(piiNullableConverter);
            e.Property(x => x.OptionsJson).HasConversion(piiConverter);
        });

        builder.Entity<SharedConnectionAcl>(e =>
        {
            e.HasIndex(x => new { x.SharedConnectionId, x.GroupId }).IsUnique();
            e.HasOne(x => x.SharedConnection).WithMany(c => c.Acls).HasForeignKey(x => x.SharedConnectionId);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId);
        });

        builder.Entity<SharedConnectionUsage>(e =>
        {
            e.HasIndex(x => new { x.SharedConnectionId, x.ConsumerUser }).IsUnique();
            e.Property(x => x.ConsumerUser).HasMaxLength(256);
            e.HasOne(x => x.SharedConnection).WithMany().HasForeignKey(x => x.SharedConnectionId);
        });

        builder.Entity<AdminServiceRun>(e =>
        {
            e.HasIndex(x => new { x.ServiceName, x.StartedAtUtc });
            e.Property(x => x.ServiceName).HasMaxLength(100);
            e.Property(x => x.Outcome).HasMaxLength(20);
        });

        builder.Entity<SubscriptionDelivery>(e =>
        {
            // At-most-once per recipient and scheduler completion.
            e.HasIndex(x => new { x.SubscriptionId, x.TriggerKey, x.RecipientKey }).IsUnique();
            e.HasIndex(x => x.DeliveryId);
            e.Property(x => x.Recipients).HasConversion(piiConverter);
        });

        builder.Entity<AuditOutboxMessage>(e =>
        {
            e.Property(x => x.ActorType).HasDefaultValue("User");
            e.HasIndex(x => x.EventId).IsUnique();
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => x.AuditLogId);
            e.HasOne(x => x.AuditLog)
                .WithMany(x => x.OutboxMessages)
                .HasForeignKey(x => x.AuditLogId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PolicyVersionEntity>(e =>
        {
            // A version string is unique within a tenant/environment; the active-lookup index keeps
            // retrieval and supersession bounded on the append-only table.
            e.HasIndex(x => new { x.Tenant, x.Environment, x.PolicyVersion }).IsUnique();
            e.HasIndex(x => new { x.Tenant, x.Environment, x.RolloutState });
        });

        builder.Entity<PolicyMachineEntity>(e =>
        {
            e.HasIndex(x => x.MachineId).IsUnique();
            e.HasIndex(x => new { x.Tenant, x.Environment });
        });

        builder.Entity<AuditLog>(e =>
        {
            e.Property(x => x.ActorType).HasDefaultValue("User");
            // Read paths that previously full-scanned this (append-heavy) table: the admin audit
            // viewer (optional action filter, newest-first, paged), usage metrics (action + time
            // window), per-report change history (resource lookup), and retention purge (time range).
            // Narrow, non-unique secondary indexes keep insert cost bounded.
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => new { x.Action, x.Timestamp });
            e.HasIndex(x => new { x.ResourceType, x.ResourceId });
        });

        builder.Entity<Group>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<PortalUser>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            // Federated accounts are looked up by their immutable provider subject.
            e.HasIndex(x => new { x.Provider, x.ExternalSubject });
            e.Property(x => x.Email).HasConversion(piiNullableConverter);
            e.Property(x => x.NormalizedEmail).HasConversion(piiNullableConverter);
            e.Property(x => x.FirstName).HasConversion(piiNullableConverter);
            e.Property(x => x.LastName).HasConversion(piiNullableConverter);
            e.Property(x => x.PhoneNumber).HasConversion(piiNullableConverter);
        });

        builder.Entity<Subscription>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Recipients).HasConversion(piiConverter);
        });

        builder.Entity<Folder>(e =>
        {
            e.HasIndex(x => x.Path).IsUnique();
        });

        builder.Entity<Dataset>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne(x => x.OwningReport).WithMany().HasForeignKey(x => x.OwningReportId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Folder>().WithMany().HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.Name).IsUnique();   // Names are globally unique portal-wide; USE DATASET resolves by name.
        });

        builder.Entity<DatasetAcl>(e =>
        {
            e.HasOne(x => x.Dataset).WithMany(d => d.Acls).HasForeignKey(x => x.DatasetId);
            e.HasOne(x => x.Group).WithMany(g => g.DatasetAcls).HasForeignKey(x => x.GroupId);
        });
    }
}
