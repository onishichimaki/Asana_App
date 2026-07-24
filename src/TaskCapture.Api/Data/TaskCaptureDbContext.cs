using Microsoft.EntityFrameworkCore;

namespace TaskCapture.Api.Data;

public sealed class TaskCaptureDbContext(DbContextOptions<TaskCaptureDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<TaskRequest> TaskRequests => Set<TaskRequest>();
    public DbSet<TaskCandidate> TaskCandidates => Set<TaskCandidate>();
    public DbSet<TaskCandidateSubtask> TaskCandidateSubtasks => Set<TaskCandidateSubtask>();
    public DbSet<AsanaRegistration> AsanaRegistrations => Set<AsanaRegistration>();
    public DbSet<AsanaSubtaskRegistration> AsanaSubtaskRegistrations => Set<AsanaSubtaskRegistration>();
    public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<WbsImportProfile> WbsImportProfiles => Set<WbsImportProfile>();
    public DbSet<WbsImportBatch> WbsImportBatches => Set<WbsImportBatch>();
    public DbSet<WbsImportRow> WbsImportRows => Set<WbsImportRow>();

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

        modelBuilder.Entity<TaskCandidateSubtask>(entity =>
        {
            entity.HasIndex(x => new { x.TaskCandidateId, x.SortOrder });
            entity.HasOne(x => x.TaskCandidate).WithMany(x => x.Subtasks).HasForeignKey(x => x.TaskCandidateId);
            entity.Property(x => x.CreatedAtUtc).HasPrecision(0);
            entity.Property(x => x.UpdatedAtUtc).HasPrecision(0);
        });

        modelBuilder.Entity<AsanaSubtaskRegistration>(entity =>
        {
            entity.HasIndex(x => x.TaskCandidateSubtaskId);
            entity.HasIndex(x => x.ExternalTaskGid);
            entity.HasOne(x => x.TaskCandidateSubtask).WithMany(x => x.Registrations).HasForeignKey(x => x.TaskCandidateSubtaskId);
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

        modelBuilder.Entity<WbsImportProfile>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.LayoutSignature });
            entity.HasOne(x => x.User).WithMany(x => x.WbsImportProfiles).HasForeignKey(x => x.UserId);
            entity.Property(x => x.CreatedAtUtc).HasPrecision(0);
            entity.Property(x => x.UpdatedAtUtc).HasPrecision(0);
        });

        modelBuilder.Entity<WbsImportBatch>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
            entity.HasIndex(x => x.FileHash);
            entity.HasOne(x => x.User).WithMany(x => x.WbsImportBatches).HasForeignKey(x => x.UserId);
            entity.HasOne(x => x.WbsImportProfile).WithMany(x => x.Batches)
                .HasForeignKey(x => x.WbsImportProfileId).OnDelete(DeleteBehavior.NoAction);
            entity.Property(x => x.CreatedAtUtc).HasPrecision(0);
            entity.Property(x => x.UpdatedAtUtc).HasPrecision(0);
        });

        modelBuilder.Entity<WbsImportRow>(entity =>
        {
            entity.HasIndex(x => new { x.WbsImportBatchId, x.SourceKey }).IsUnique();
            entity.HasIndex(x => x.RowHash);
            entity.HasIndex(x => x.ExternalTaskGid);
            entity.HasOne(x => x.WbsImportBatch).WithMany(x => x.Rows)
                .HasForeignKey(x => x.WbsImportBatchId);
            entity.HasOne(x => x.ParentRow).WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentRowId).OnDelete(DeleteBehavior.NoAction);
            entity.Property(x => x.DueDate).HasColumnType("date");
            entity.Property(x => x.CreatedAtUtc).HasPrecision(0);
            entity.Property(x => x.UpdatedAtUtc).HasPrecision(0);
        });
    }
}
