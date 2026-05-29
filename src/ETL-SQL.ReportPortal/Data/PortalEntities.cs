using Microsoft.AspNetCore.Identity;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;

namespace ETL_SQL.ReportPortal.Data;

// ── Identity ──────────────────────────────────────────────────────────────────

public class PortalUser : IdentityUser<int>
{
    public string? FirstName            { get; set; }
    public string? LastName             { get; set; }
    public string? MiddleInitial        { get; set; }
    public bool    IsActive             { get; set; } = true;
    public bool    MustChangePassword   { get; set; } = false;
    public string  Provider             { get; set; } = "Local";
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;

    public ICollection<UserGroup>    UserGroups    { get; set; } = [];
    public ICollection<Subscription> Subscriptions { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<ReportFavorite> ReportFavorites { get; set; } = [];
    public ICollection<SavedReportView> SavedViews { get; set; } = [];
    public ICollection<ReportAlert> ReportAlerts { get; set; } = [];
}

public class PortalRole : IdentityRole<int>
{
    public PortalRole() { }
    public PortalRole(string roleName) : base(roleName) { }
}

// ── Groups ────────────────────────────────────────────────────────────────────

public class Group
{
    public int     Id          { get; set; }
    public string  Name        { get; set; } = "";
    public string? Description { get; set; }
    public string  Provider    { get; set; } = "Local";
    public string? AdGroup     { get; set; }

    public ICollection<UserGroup>  UserGroups  { get; set; } = [];
    public ICollection<FolderAcl>  FolderAcls  { get; set; } = [];
    public ICollection<DatasetAcl> DatasetAcls { get; set; } = [];
}

public class UserGroup
{
    public int        UserId  { get; set; }
    public PortalUser User    { get; set; } = null!;
    public int        GroupId { get; set; }
    public Group      Group   { get; set; } = null!;
}

// ── Folders ───────────────────────────────────────────────────────────────────

public class Folder
{
    public int     Id       { get; set; }
    public int?    ParentId { get; set; }
    public Folder? Parent   { get; set; }
    public string  Name     { get; set; } = "";
    public string  Path     { get; set; } = "";
    public int     OwnerId  { get; set; }

    public ICollection<Folder>    Children { get; set; } = [];
    public ICollection<FolderAcl> Acls     { get; set; } = [];
    public ICollection<Report>    Reports  { get; set; } = [];
}

public enum FolderPermission { Read, Execute, Manage }

public class FolderAcl
{
    public int              Id         { get; set; }
    public int              FolderId   { get; set; }
    public Folder           Folder     { get; set; } = null!;
    public int              GroupId    { get; set; }
    public Group            Group      { get; set; } = null!;
    public FolderPermission Permission { get; set; }
}

// ── Reports ───────────────────────────────────────────────────────────────────

public class Report
{
    public int       Id                 { get; set; }
    public int       FolderId           { get; set; }
    public Folder    Folder             { get; set; } = null!;
    public string    Name               { get; set; } = "";
    public string?   Description        { get; set; }
    public string?   Owner              { get; set; }
    public string?   Contact            { get; set; }
    public string?   Tags               { get; set; }
    public string?   Category           { get; set; }
    public string?   Domain             { get; set; }
    public string?   Steward            { get; set; }
    public string?   Certification      { get; set; }
    public string?   MetadataJson       { get; set; }
    public string    ScriptPath           { get; set; } = "";
    public DateTime  ScriptLastModified   { get; set; }
    public string?   PublishedScriptHash  { get; set; }
    public int       CreatedBy            { get; set; }
    public DateTime  CreatedAt          { get; set; } = DateTime.UtcNow;
    public DateTime  UpdatedAt          { get; set; } = DateTime.UtcNow;
    public DateTime? LastViewedAt       { get; set; }
    public DateTime? LastRefreshStartedAt   { get; set; }
    public DateTime? LastRefreshCompletedAt { get; set; }
    public string?   LastRefreshStatus      { get; set; }
    public string?   LastRefreshError       { get; set; }
    public long?     LastRefreshDurationMs  { get; set; }
    public bool      IsDeleted          { get; set; } = false;

    public ICollection<ReportSnapshot> Snapshots    { get; set; } = [];
    public ICollection<Subscription>   Subscriptions { get; set; } = [];
    public ICollection<DatasetJob>     DatasetJobs  { get; set; } = [];
    public ICollection<ReportFavorite> Favorites    { get; set; } = [];
    public ICollection<ReportShareLink> ShareLinks   { get; set; } = [];
    public ICollection<ReportEmbedToken> EmbedTokens { get; set; } = [];
    public ICollection<SavedReportView> SavedViews   { get; set; } = [];
    public ICollection<ReportAlert> Alerts           { get; set; } = [];
}

public class ReportFavorite
{
    public int        Id        { get; set; }
    public int        UserId    { get; set; }
    public PortalUser User      { get; set; } = null!;
    public int        ReportId  { get; set; }
    public Report     Report    { get; set; } = null!;
    public DateTime   CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ReportShareLink
{
    public int        Id          { get; set; }
    public int        ReportId    { get; set; }
    public Report     Report      { get; set; } = null!;
    public int        CreatedBy   { get; set; }
    public PortalUser Creator     { get; set; } = null!;
    public string     Token       { get; set; } = "";
    public DateTime   CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime?  ExpiresAt   { get; set; }
    public DateTime?  RevokedAt   { get; set; }
}

public class ReportEmbedToken
{
    public int        Id        { get; set; }
    public int        ReportId  { get; set; }
    public Report     Report    { get; set; } = null!;
    public int        CreatedBy { get; set; }
    public PortalUser Creator   { get; set; } = null!;
    public string     Name      { get; set; } = "";
    public string     Token     { get; set; } = "";
    public DateTime   CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime?  ExpiresAt { get; set; }
    public DateTime?  RevokedAt { get; set; }
}

public class SavedReportView
{
    public int        Id             { get; set; }
    public int        ReportId       { get; set; }
    public Report     Report         { get; set; } = null!;
    public int        UserId         { get; set; }
    public PortalUser User           { get; set; } = null!;
    public string     Name           { get; set; } = "";
    public string?    ParametersJson { get; set; }
    public string?    FiltersJson    { get; set; }
    public bool       IsDefault      { get; set; }
    public DateTime   CreatedAt      { get; set; } = DateTime.UtcNow;
    public DateTime   UpdatedAt      { get; set; } = DateTime.UtcNow;
}

public class ReportAlert
{
    public int        Id          { get; set; }
    public int        ReportId    { get; set; }
    public Report     Report      { get; set; } = null!;
    public int        OwnerId     { get; set; }
    public PortalUser Owner       { get; set; } = null!;
    public string     Name        { get; set; } = "";
    public string     VisualName  { get; set; } = "";
    public string     Operator    { get; set; } = ">=";
    public decimal    Threshold   { get; set; }
    public string?    Recipient   { get; set; }
    public string?    SmtpAlias   { get; set; }
    public bool       IsActive    { get; set; } = true;
    public DateTime   CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime   UpdatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime?  LastCheckedAt { get; set; }
    public DateTime?  LastTriggeredAt { get; set; }
}

public class ReportSnapshot
{
    public int      Id                   { get; set; }
    public int      ReportId             { get; set; }
    public Report   Report               { get; set; } = null!;
    public string   ManifestPath         { get; set; } = "";
    public DateTime BuiltAt              { get; set; } = DateTime.UtcNow;
    public int      BuiltBy              { get; set; }
    public string?  ParametersJson       { get; set; }
    public string?  ScriptHashAtRunTime  { get; set; }
    public bool?    HashMatched          { get; set; }
}

// ── Subscriptions ─────────────────────────────────────────────────────────────

public enum SubscriptionFormat { PDF, CSV, Markdown, Link }

public class Subscription
{
    public int                 Id               { get; set; }
    public int                 ReportId         { get; set; }
    public Report              Report           { get; set; } = null!;
    public int                 UserId           { get; set; }
    public PortalUser          User             { get; set; } = null!;
    public string?             Schedule         { get; set; }
    public bool                DeliverOnRefresh  { get; set; } = false;
    public SubscriptionFormat  Format           { get; set; } = SubscriptionFormat.PDF;
    public string              SmtpAlias        { get; set; } = "";
    public string              Recipients       { get; set; } = "";
    public string?             ScriptPath       { get; set; }
    public string?             Name             { get; set; }
    public string?             ParametersJson   { get; set; }
    public DateTime?           LastSentAt       { get; set; }
    public DateTime?           NextRunAt        { get; set; }
    public int                 FailCount        { get; set; } = 0;
    public bool                IsActive         { get; set; } = true;
}

// ── SMTP Connections ──────────────────────────────────────────────────────────

public class SmtpConnection
{
    public int     Id                { get; set; }
    public string  Alias             { get; set; } = "";
    public string  Host              { get; set; } = "";
    public int     Port              { get; set; } = 587;
    public string? Username          { get; set; }
    public string? EncryptedPassword { get; set; }
    public string? FromAddress       { get; set; }
    public bool    UseSsl            { get; set; } = true;
}

// ── Audit Log ─────────────────────────────────────────────────────────────────

public class AuditLog
{
    public int      Id           { get; set; }
    public int?     UserId       { get; set; }
    public string   Action       { get; set; } = "";
    public string?  ResourceType { get; set; }
    public string?  ResourceId   { get; set; }
    public DateTime Timestamp    { get; set; } = DateTime.UtcNow;
    public string?  Detail       { get; set; }
}

// ── Dataset Refresh Jobs ──────────────────────────────────────────────────────

public class DatasetJob
{
    public int      Id                  { get; set; }
    public int      ReportId            { get; set; }
    public Report   Report              { get; set; } = null!;
    public string   OrchestratorJobName { get; set; } = "";
    public string   RefreshInterval     { get; set; } = "";
    public DateTime? LastRefreshedAt    { get; set; }
}

// ── Refresh Tokens ────────────────────────────────────────────────────────────

public class RefreshToken
{
    public int        Id        { get; set; }
    public int        UserId    { get; set; }
    public PortalUser User      { get; set; } = null!;
    public string     Token     { get; set; } = "";
    public DateTime   ExpiresAt { get; set; }
    public DateTime?  RevokedAt { get; set; }
}

// ── Datasets ──────────────────────────────────────────────────────────────────

public class Dataset
{
    public int                Id              { get; set; }
    public string             Name            { get; set; } = "";
    public string             FolderPath      { get; set; } = "";
    public string             ParquetFilePath { get; set; } = "";
    public int?               OwningReportId  { get; set; }
    public Report?            OwningReport    { get; set; }
    public string             SourceQuery     { get; set; } = "";
    public DatasetAccessLevel    AccessLevel    { get; set; } = DatasetAccessLevel.Private;
    public DatasetEncryptionMode EncryptionMode { get; set; } = DatasetEncryptionMode.MachineBound;
    public DateTime?             LastRefresh    { get; set; }
    public string?               Ttl            { get; set; }
    public string?               RefreshInterval { get; set; }
    public long                  RowCount       { get; set; }
    public string?               ColumnSchema   { get; set; } // JSON
    public DateTime              CreatedAt      { get; set; } = DateTime.UtcNow;
    public DateTime              UpdatedAt      { get; set; } = DateTime.UtcNow;

    public ICollection<DatasetAcl> Acls { get; set; } = [];
}

public class DatasetAcl
{
    public int               Id         { get; set; }
    public int               DatasetId  { get; set; }
    public Dataset           Dataset    { get; set; } = null!;
    public int               GroupId    { get; set; }
    public Group             Group      { get; set; } = null!;
    public DatasetPermission Permission { get; set; }
}
