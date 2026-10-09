using LogEase.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace LogEase.Api.Data;

public class LogEaseDbContext : DbContext
{
    public LogEaseDbContext(DbContextOptions<LogEaseDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<Worklog> Worklogs => Set<Worklog>();
    public DbSet<StickyNote> StickyNotes => Set<StickyNote>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AiParseEvent> AiParseEvents => Set<AiParseEvent>();
    public DbSet<OpsIncident> OpsIncidents => Set<OpsIncident>();
    public DbSet<TechDebtItem> TechDebtItems => Set<TechDebtItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQL Server dbo tables with logease_ prefix (shared test DB friendly)
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("logease_Users");
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.AzureObjectId).IsUnique().HasFilter("[AzureObjectId] IS NOT NULL");
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.AzureObjectId).HasMaxLength(64);
            // Data URLs from Graph photo bytes; keep as nvarchar(max).
            e.Property(x => x.ProfilePhotoUrl).HasColumnType("nvarchar(max)");
            e.Property(x => x.TimeZoneId).HasMaxLength(80);
            e.Property(x => x.KimaiApiToken).HasMaxLength(512);
            e.Property(x => x.AiParseInstructions).HasMaxLength(4000);
            e.Property(x => x.GitlabPersonalAccessToken).HasMaxLength(512);
            e.Property(x => x.GitlabBaseUrl).HasMaxLength(200);
            e.Property(x => x.GitlabUsername).HasMaxLength(120);
            e.Property(x => x.MailImapHost).HasMaxLength(200);
            e.Property(x => x.MailImapUser).HasMaxLength(200);
            e.Property(x => x.MailImapPassword).HasMaxLength(512);
        });

        modelBuilder.Entity<StickyNote>(e =>
        {
            e.ToTable("logease_StickyNotes");
            e.Property(x => x.Body).HasMaxLength(4000);
            e.HasOne(x => x.User).WithMany(u => u.StickyNotes).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.NoteDate });
        });

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.ToTable("logease_AppSettings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(80);
            e.Property(x => x.Value).HasMaxLength(200);
        });

        modelBuilder.Entity<Project>(e =>
        {
            e.ToTable("logease_Projects");
            e.Property(x => x.Name).HasMaxLength(160);
        });

        modelBuilder.Entity<Activity>(e =>
        {
            e.ToTable("logease_Activities");
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasOne(x => x.Project).WithMany(p => p.Activities).HasForeignKey(x => x.ProjectId);
        });

        modelBuilder.Entity<Worklog>(e =>
        {
            e.ToTable("logease_Worklogs");
            e.Property(x => x.Hours).HasPrecision(5, 2);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.HasOne(x => x.User).WithMany(u => u.Worklogs).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Activity).WithMany().HasForeignKey(x => x.ActivityId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UserId, x.WorkDate });
        });

        modelBuilder.Entity<AiParseEvent>(e =>
        {
            e.ToTable("logease_AiParseEvents");
            e.Property(x => x.Parser).HasMaxLength(80);
            e.Property(x => x.Warning).HasMaxLength(500);
            e.Property(x => x.Source).HasMaxLength(40);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.Parser);
        });

        modelBuilder.Entity<OpsIncident>(e =>
        {
            e.ToTable("logease_OpsIncidents");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Detail).HasMaxLength(2000);
            e.Property(x => x.Severity).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(30);
            e.Property(x => x.Category).HasMaxLength(40);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<TechDebtItem>(e =>
        {
            e.ToTable("logease_TechDebtItems");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Area).HasMaxLength(40);
            e.Property(x => x.Priority).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(30);
            e.HasIndex(x => x.Status);
        });
    }
}
