using TaskPulse.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace TaskPulse.Api.Data;

public sealed class TasksDbContext(DbContextOptions<TasksDbContext> options) : DbContext(options)
{
    public DbSet<TaskEntity> Tasks => Set<TaskEntity>();

    public DbSet<CatalogEntity> Catalog => Set<CatalogEntity>();

    public DbSet<PreferenceEntity> Preferences => Set<PreferenceEntity>();

    public DbSet<UploadEntity> Uploads => Set<UploadEntity>();

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

        tasks.Property(t => t.Version).IsRowVersion();

        tasks.HasIndex(t => t.Status);
        tasks.HasIndex(t => t.CreatedAtUtc);

        var catalog = modelBuilder.Entity<CatalogEntity>();
        catalog.ToTable("catalog");
        catalog.HasKey(c => c.Id);

        catalog.Property(c => c.Kind).HasMaxLength(CatalogLimits.CodeMaxLength).IsRequired();
        catalog.Property(c => c.Code).HasMaxLength(CatalogLimits.CodeMaxLength).IsRequired();
        catalog.Property(c => c.Label).HasMaxLength(CatalogLimits.LabelMaxLength).IsRequired();
        catalog.Property(c => c.Parents).HasColumnType("text[]").IsRequired();
        catalog.Property(c => c.Attributes).HasColumnType("jsonb");

        catalog.Property(c => c.CreatedAtUtc).HasColumnType("timestamp with time zone");
        catalog.Property(c => c.UpdatedAtUtc).HasColumnType("timestamp with time zone");

        catalog.Property(c => c.Version).IsRowVersion();

        catalog.HasIndex(c => new { c.Kind, c.Code }).IsUnique();
        catalog.HasIndex(c => c.Parents).HasMethod("gin");

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
        uploads.Property(u => u.CreatedAtUtc).HasColumnType("timestamp with time zone");
        uploads.HasIndex(u => u.CreatedAtUtc);
        uploads.HasIndex(u => u.Source);
    }
}
