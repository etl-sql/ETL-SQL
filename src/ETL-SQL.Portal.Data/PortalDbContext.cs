using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ETL_SQL.Portal.Data;

public class PortalDbContext(DbContextOptions<PortalDbContext> options)
    : IdentityDbContext<PortalUser, PortalRole, int,
        IdentityUserClaim<int>, IdentityUserRole<int>, IdentityUserLogin<int>,
        IdentityRoleClaim<int>, IdentityUserToken<int>>(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    public DbSet<Group> Groups => Set<Group>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FolderAcl> FolderAcls => Set<FolderAcl>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ReportSnapshot> ReportSnapshots => Set<ReportSnapshot>();
    public DbSet<PortalExecutionJob> PortalExecutionJobs => Set<PortalExecutionJob>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionDelivery> SubscriptionDeliveries => Set<SubscriptionDelivery>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AuditOutboxMessage> AuditOutboxMessages => Set<AuditOutboxMessage>();
    public DbSet<ReportJobLink> ReportJobLinks => Set<ReportJobLink>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Dataset> Datasets => Set<Dataset>();
    public DbSet<DatasetAcl> DatasetAcls => Set<DatasetAcl>();
    public DbSet<DatasetUserAcl> DatasetUserAcls => Set<DatasetUserAcl>();
    public DbSet<GroupStudioCapability> GroupStudioCapabilities => Set<GroupStudioCapability>();
    public DbSet<ReportFavorite> ReportFavorites => Set<ReportFavorite>();
    public DbSet<ReportAccessRequest> ReportAccessRequests => Set<ReportAccessRequest>();
    public DbSet<ReportAcl> ReportAcls => Set<ReportAcl>();
    public DbSet<ReportShareLink> ReportShareLinks => Set<ReportShareLink>();
    public DbSet<ReportEmbedToken> ReportEmbedTokens => Set<ReportEmbedToken>();
    public DbSet<SavedReportView> SavedReportViews => Set<SavedReportView>();
    public DbSet<ReportAlert> ReportAlerts => Set<ReportAlert>();
    public DbSet<AlertNotification> AlertNotifications => Set<AlertNotification>();
    public DbSet<ServiceAccount> ServiceAccounts => Set<ServiceAccount>();
    public DbSet<PolicyVersionEntity> PolicyVersions => Set<PolicyVersionEntity>();
    public DbSet<PolicyMachineEntity> PolicyMachines => Set<PolicyMachineEntity>();
    public DbSet<PortalSecret> PortalSecrets => Set<PortalSecret>();
    public DbSet<PortalSharedConnection> PortalSharedConnections => Set<PortalSharedConnection>();
    public DbSet<SharedConnectionAcl> SharedConnectionAcls => Set<SharedConnectionAcl>();
    public DbSet<SharedConnectionUsage> SharedConnectionUsages => Set<SharedConnectionUsage>();
    public DbSet<AdminServiceRun> AdminServiceRuns => Set<AdminServiceRun>();
    public DbSet<GovernanceSettings> GovernanceSettings => Set<GovernanceSettings>();
    public DbSet<GovernanceResolutionCategory> GovernanceResolutionCategories => Set<GovernanceResolutionCategory>();
    public DbSet<GovernanceGlossaryTerm> GovernanceGlossaryTerms => Set<GovernanceGlossaryTerm>();
    public DbSet<GovernanceAssetBadge> GovernanceAssetBadges => Set<GovernanceAssetBadge>();
    public DbSet<GovernanceAssetReview> GovernanceAssetReviews => Set<GovernanceAssetReview>();
    public DbSet<GovernanceFinding> GovernanceFindings => Set<GovernanceFinding>();
    public DbSet<GovernanceFindingDecision> GovernanceFindingDecisions => Set<GovernanceFindingDecision>();
    public DbSet<GovernanceScan> GovernanceScans => Set<GovernanceScan>();

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
            e.HasMany(x => x.ReportJobLinks).WithOne(l => l.Report).HasForeignKey(l => l.ReportId);
            e.HasMany(x => x.ShareLinks).WithOne(l => l.Report).HasForeignKey(l => l.ReportId);
            e.HasMany(x => x.EmbedTokens).WithOne(t => t.Report).HasForeignKey(t => t.ReportId);
            e.HasMany(x => x.SavedViews).WithOne(v => v.Report).HasForeignKey(v => v.ReportId);
            e.HasMany(x => x.Alerts).WithOne(a => a.Report).HasForeignKey(a => a.ReportId);
        });

        builder.Entity<ReportJobLink>(e =>
        {
            e.HasIndex(x => new { x.ReportId, x.OrchestratorAlias, x.JobName }).IsUnique();
            e.HasIndex(x => x.JobName);
            e.Property(x => x.OrchestratorAlias).HasMaxLength(128);
            e.Property(x => x.JobName).HasMaxLength(256);
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
            e.HasIndex(x => x.Token).IsUnique();
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

        builder.Entity<ReportAccessRequest>(e =>
        {
            e.HasIndex(x => new { x.RequesterUserId, x.ReportId })
                .IsUnique()
                .HasFilter("\"Status\" = 'Pending'");
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.DecisionReason).HasMaxLength(1000);
            e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterUserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DecidedBy).WithMany().HasForeignKey(x => x.DecidedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Report).WithMany().HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<ReportAcl>(e =>
        {
            e.HasIndex(x => new { x.ReportId, x.UserId })
                .IsUnique()
                .HasFilter("\"UserId\" IS NOT NULL");
            e.HasIndex(x => new { x.ReportId, x.GroupId })
                .IsUnique()
                .HasFilter("\"GroupId\" IS NOT NULL");
            e.HasOne(x => x.Report).WithMany(r => r.Acls).HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ReportShareLink>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => new { x.ReportId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne(x => x.Report).WithMany(r => r.ShareLinks).HasForeignKey(x => x.ReportId);
            e.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy);
        });

        builder.Entity<ReportEmbedToken>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => new { x.ReportId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
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
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(300);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.HasMany(x => x.Notifications).WithOne(n => n.Alert).HasForeignKey(n => n.AlertId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AlertNotification>(e =>
        {
            e.HasIndex(x => new { x.AlertId, x.OrchestratorAlias, x.NotificationName }).IsUnique();
            e.Property(x => x.OrchestratorAlias).HasMaxLength(200);
            e.Property(x => x.NotificationName).HasMaxLength(200);
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

        // ── Governance workflow state ────────────────────────────────────────────────────────
        builder.Entity<GovernanceSettings>(e =>
        {
            // Single logical row. The unique index is the constraint, not a convention someone has
            // to remember: two settings rows would mean two answers to "is this asset governed?".
            e.HasIndex(x => x.Scope).IsUnique();
            e.Property(x => x.Scope).HasMaxLength(64);
            e.Property(x => x.PolicyLevel).HasMaxLength(32);
        });

        builder.Entity<GovernanceResolutionCategory>(e =>
        {
            e.HasIndex(x => x.Value).IsUnique();
            e.Property(x => x.Value).HasMaxLength(64);
            e.Property(x => x.Label).HasMaxLength(200);
            e.Property(x => x.Color).HasMaxLength(32);
        });

        builder.Entity<GovernanceGlossaryTerm>(e =>
        {
            e.HasIndex(x => x.Term).IsUnique();
            e.Property(x => x.Term).HasMaxLength(200);
            e.Property(x => x.DataType).HasMaxLength(100);
            e.Property(x => x.Steward).HasMaxLength(256);
        });

        builder.Entity<GovernanceAssetBadge>(e =>
        {
            e.HasIndex(x => new { x.AssetKey, x.Badge }).IsUnique();
            e.Property(x => x.AssetKey).HasMaxLength(512);
            e.Property(x => x.Badge).HasMaxLength(64);
            e.Property(x => x.AssetVersion).HasMaxLength(128);
        });

        builder.Entity<GovernanceAssetReview>(e =>
        {
            e.HasIndex(x => x.AssetKey).IsUnique();
            e.Property(x => x.AssetKey).HasMaxLength(512);
            e.Property(x => x.ReviewedVersion).HasMaxLength(128);
        });

        builder.Entity<GovernanceFinding>(e =>
        {
            // One live finding per asset+rule. Re-scanning updates it rather than accumulating
            // duplicates, so the queue length means "problems", not "scans".
            e.HasIndex(x => new { x.AssetKey, x.RuleKey }).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.AssetKey).HasMaxLength(512);
            e.Property(x => x.RuleKey).HasMaxLength(64);
            e.Property(x => x.AssetVersion).HasMaxLength(128);
            e.Property(x => x.Status).HasMaxLength(32);
        });

        builder.Entity<GovernanceFindingDecision>(e =>
        {
            e.HasIndex(x => new { x.FindingId, x.DecidedAtUtc });
            e.HasOne(x => x.Finding).WithMany(f => f.Decisions).HasForeignKey(x => x.FindingId);
            e.Property(x => x.Decision).HasMaxLength(32);
            e.Property(x => x.CategoryValue).HasMaxLength(64);
            e.Property(x => x.AssetVersion).HasMaxLength(128);
            e.Property(x => x.DecidedByUserName).HasMaxLength(256);
        });

        builder.Entity<GovernanceScan>(e =>
        {
            e.HasIndex(x => x.StartedAtUtc);
            e.Property(x => x.Trigger).HasMaxLength(32);
            e.Property(x => x.Status).HasMaxLength(32);
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

        builder.Entity<GroupStudioCapability>(e =>
        {
            e.HasOne(x => x.Group).WithMany(g => g.StudioCapabilities).HasForeignKey(x => x.GroupId);
            e.Property(x => x.Capability).HasMaxLength(64);
            e.HasIndex(x => new { x.GroupId, x.Capability }).IsUnique();
        });

        builder.Entity<DatasetUserAcl>(e =>
        {
            e.HasOne(x => x.Dataset).WithMany(d => d.UserAcls).HasForeignKey(x => x.DatasetId);
            // Deleting a user removes their direct grants: a grant that outlived its principal would
            // be re-attached to whoever next receives that id.
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.DatasetId, x.UserId }).IsUnique();
        });
    }
}

