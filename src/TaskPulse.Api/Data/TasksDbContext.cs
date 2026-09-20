using Microsoft.EntityFrameworkCore;
using TaskPulse.Api.Models;

namespace TaskPulse.Api.Data;

public sealed class TasksDbContext(DbContextOptions<TasksDbContext> options) : DbContext(options)
{
    public DbSet<TaskEntity> Tasks => Set<TaskEntity>();

    public DbSet<CatalogEntity> Catalog => Set<CatalogEntity>();

    public DbSet<CatalogSchemaEntity> CatalogSchemas => Set<CatalogSchemaEntity>();

    public DbSet<PreferenceEntity> Preferences => Set<PreferenceEntity>();

    public DbSet<UploadEntity> Uploads => Set<UploadEntity>();

    public DbSet<AuditEntity> Audit => Set<AuditEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tasks = modelBuilder.Entity<TaskEntity>();
        tasks.ToTable("tasks");
        tasks.HasKey(t => t.Id);

        tasks.Property(t => t.Title).HasMaxLength(TaskLimits.TitleMaxLength).IsRequired();
        tasks.Property(t => t.Description).HasMaxLength(TaskLimits.DescriptionMaxLength);

        tasks.Property(t => t.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        tasks.Property(t => t.CreatedAtUtc).HasColumnType("timestamp with time zone");
        tasks.Property(t => t.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        tasks.Property(t => t.DeletedAtUtc).HasColumnType("timestamp with time zone");
        tasks.Property(t => t.CreatedBy).HasMaxLength(AuditLimits.ActorMaxLength);
        tasks.Property(t => t.UpdatedBy).HasMaxLength(AuditLimits.ActorMaxLength);

        tasks.Property(t => t.Version).IsRowVersion();

        tasks.Property(t => t.Priority).HasConversion<string>().HasMaxLength(16).HasDefaultValue(TaskPriority.Normal);
        tasks.Property(t => t.DueAtUtc).HasColumnType("timestamp with time zone");
        tasks.Property(t => t.AssigneeId).HasMaxLength(64);
        tasks.Property(t => t.AssigneeName).HasMaxLength(TaskLimits.AssigneeMaxLength);
        tasks.Property(t => t.Labels).HasColumnType("text[]").HasDefaultValueSql("'{}'");
        tasks.HasIndex(t => t.DueAtUtc);
        tasks.HasIndex(t => t.AssigneeId);
        tasks.HasIndex(t => t.Labels).HasMethod("gin");
        // English stemming: "upgrading" finds "upgrade"; the query side adds prefix matching so "post" still finds "postgres".
        tasks.Property(t => t.SearchVector)
            .HasComputedColumnSql("to_tsvector('english', coalesce(\"Title\", '') || ' ' || coalesce(\"Description\", ''))", stored: true);
        tasks.HasIndex(t => t.SearchVector).HasMethod("gin");

        tasks.HasIndex(t => t.Status);
        tasks.HasIndex(t => t.CreatedAtUtc);
        tasks.HasIndex(t => t.DeletedAtUtc);

        var catalog = modelBuilder.Entity<CatalogEntity>();
        catalog.ToTable("catalog");
        catalog.HasKey(c => c.Id);

        catalog.Property(c => c.Kind).HasMaxLength(CatalogLimits.CodeMaxLength).IsRequired();
        catalog.Property(c => c.Code).HasMaxLength(CatalogLimits.CodeMaxLength).IsRequired();
        catalog.Property(c => c.Label).HasMaxLength(CatalogLimits.LabelMaxLength).IsRequired();
        catalog.Property(c => c.Parents).HasColumnType("text[]").IsRequired();
        catalog.Property(c => c.Attributes).HasColumnType("jsonb");
        catalog.Property(c => c.CreatedBy).HasMaxLength(AuditLimits.ActorMaxLength);
        catalog.Property(c => c.UpdatedBy).HasMaxLength(AuditLimits.ActorMaxLength);

        catalog.Property(c => c.CreatedAtUtc).HasColumnType("timestamp with time zone");
        catalog.Property(c => c.UpdatedAtUtc).HasColumnType("timestamp with time zone");

        catalog.Property(c => c.Version).IsRowVersion();

        catalog.HasIndex(c => new { c.Kind, c.Code }).IsUnique();
        catalog.HasIndex(c => c.Parents).HasMethod("gin");

        var schemas = modelBuilder.Entity<CatalogSchemaEntity>();
        schemas.ToTable("catalog_schemas");
        schemas.HasKey(s => s.Kind);
        schemas.Property(s => s.Kind).HasMaxLength(CatalogLimits.CodeMaxLength);
        schemas.Property(s => s.Schema).HasColumnType("jsonb").IsRequired();
        schemas.Property(s => s.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        schemas.Property(s => s.UpdatedBy).HasMaxLength(AuditLimits.ActorMaxLength);

        var preferences = modelBuilder.Entity<PreferenceEntity>();
        preferences.ToTable("preferences");
        preferences.HasKey(p => p.UserId);
        preferences.Property(p => p.UserId).HasMaxLength(PreferenceLimits.UserIdMaxLength);
        preferences.Property(p => p.Theme).HasMaxLength(16).IsRequired();
        preferences.Property(p => p.Nickname).HasMaxLength(PreferenceLimits.NicknameMaxLength);
        preferences.Property(p => p.UpdatedAtUtc).HasColumnType("timestamp with time zone");

        var uploads = modelBuilder.Entity<UploadEntity>();
        uploads.ToTable("uploads");
        uploads.HasKey(u => u.Id);
        uploads.Property(u => u.FileName).HasMaxLength(UploadLimits.FileNameMaxLength).IsRequired();
        uploads.Property(u => u.ContentType).HasMaxLength(100).IsRequired();
        uploads.Property(u => u.Source).HasMaxLength(UploadLimits.SourceMaxLength);
        uploads.Property(u => u.Note).HasMaxLength(UploadLimits.NoteMaxLength);
        uploads.Property(u => u.OwnerId).HasMaxLength(AuditLimits.ActorMaxLength);
        uploads.Property(u => u.CreatedAtUtc).HasColumnType("timestamp with time zone");
        uploads.HasIndex(u => u.CreatedAtUtc);
        uploads.HasIndex(u => u.Source);

        var audit = modelBuilder.Entity<AuditEntity>();
        audit.ToTable("audit");
        audit.HasKey(a => a.Id);
        audit.Property(a => a.Id).UseIdentityAlwaysColumn();
        audit.Property(a => a.AtUtc).HasColumnType("timestamp with time zone");
        audit.Property(a => a.Actor).HasMaxLength(AuditLimits.ActorMaxLength);
        audit.Property(a => a.Action).HasMaxLength(AuditLimits.ActionMaxLength).IsRequired();
        audit.Property(a => a.Resource).HasMaxLength(AuditLimits.ResourceMaxLength).IsRequired();
        audit.Property(a => a.Kind).HasMaxLength(AuditLimits.KindMaxLength);
        audit.Property(a => a.TargetId).HasMaxLength(AuditLimits.TargetMaxLength).IsRequired();
        audit.Property(a => a.Summary).HasMaxLength(AuditLimits.SummaryMaxLength).IsRequired();
        audit.HasIndex(a => a.AtUtc);
        audit.HasIndex(a => a.Resource);
    }
}
