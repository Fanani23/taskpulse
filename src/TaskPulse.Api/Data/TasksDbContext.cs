using TaskPulse.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace TaskPulse.Api.Data;

public sealed class TasksDbContext(DbContextOptions<TasksDbContext> options) : DbContext(options)
{
    public DbSet<TaskEntity> Tasks => Set<TaskEntity>();

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
    }
}
