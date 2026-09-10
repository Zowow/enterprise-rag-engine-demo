using EnterpriseRAG.Worker.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseRAG.Worker.Data;

public class WorkerDbContext : DbContext
{
    public WorkerDbContext(DbContextOptions<WorkerDbContext> options) : base(options)
    {
    }

    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DocumentEntity>(entity =>
        {
            entity.ToTable("documents");

            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).HasColumnName("id");
            entity.Property(d => d.Title).HasColumnName("title").HasMaxLength(255).IsRequired();
            entity.Property(d => d.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
            entity.Property(d => d.FileSizeBytes).HasColumnName("file_size_bytes").IsRequired();
            entity.Property(d => d.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
            entity.Property(d => d.BlobUri).HasColumnName("blob_uri").IsRequired();
            entity.Property(d => d.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            entity.Property(d => d.FailureReason).HasColumnName("failure_reason");
            entity.Property(d => d.ChunkCount).HasColumnName("chunk_count").HasDefaultValue(0);
            entity.Property(d => d.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.Property(d => d.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

            entity.HasIndex(d => d.Status).HasDatabaseName("idx_documents_status");
        });
    }
}
