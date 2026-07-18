using Microsoft.EntityFrameworkCore;

namespace TaskCapture.Api.Data;

public sealed class TaskCaptureDbContext(DbContextOptions<TaskCaptureDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<TaskRequest> TaskRequests => Set<TaskRequest>();
    public DbSet<TaskCandidate> TaskCandidates => Set<TaskCandidate>();
    public DbSet<AsanaRegistration> AsanaRegistrations => Set<AsanaRegistration>();
    public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(x => x.ClientKey).IsUnique();
            entity.Property(x => x.CreatedAtUtc).HasPrecision(0);
        });

        modelBuilder.Entity<TaskRequest>(entity =>
        {
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
            entity.HasOne(x => x.User).WithMany(x => x.TaskRequests).HasForeignKey(x => x.UserId);
            entity.Property(x => x.CreatedAtUtc).HasPrecision(0);
            entity.Property(x => x.UpdatedAtUtc).HasPrecision(0);
        });

        modelBuilder.Entity<TaskCandidate>(entity =>
        {
            entity.HasIndex(x => x.TaskRequestId);
            entity.HasOne(x => x.TaskRequest).WithMany(x => x.Candidates).HasForeignKey(x => x.TaskRequestId);
            entity.Property(x => x.DueDate).HasColumnType("date");
            entity.Property(x => x.CreatedAtUtc).HasPrecision(0);
            entity.Property(x => x.UpdatedAtUtc).HasPrecision(0);
        });

        modelBuilder.Entity<AsanaRegistration>(entity =>
        {
            entity.HasIndex(x => x.TaskCandidateId);
            entity.HasIndex(x => x.ExternalTaskGid);
            entity.HasOne(x => x.TaskCandidate).WithMany(x => x.Registrations).HasForeignKey(x => x.TaskCandidateId);
            entity.Property(x => x.CreatedAtUtc).HasPrecision(0);
        });

        modelBuilder.Entity<ApplicationSetting>(entity =>
        {
            entity.HasKey(x => x.Key);
            entity.Property(x => x.UpdatedAtUtc).HasPrecision(0);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => x.CorrelationId);
            entity.HasOne(x => x.User).WithMany(x => x.AuditLogs).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            entity.Property(x => x.CreatedAtUtc).HasPrecision(0);
        });
    }
}
