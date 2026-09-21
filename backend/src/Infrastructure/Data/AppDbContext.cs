using EnterpriseRAG.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseRAG.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Document Entity Configuration
        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("documents");

            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id)
                .HasColumnName("id");

            entity.Property(d => d.Title)
                .HasColumnName("title")
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(d => d.FileName)
                .HasColumnName("file_name")
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(d => d.FileSizeBytes)
                .HasColumnName("file_size_bytes")
                .IsRequired();

            entity.Property(d => d.ContentType)
                .HasColumnName("content_type")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(d => d.BlobUri)
                .HasColumnName("blob_uri")
                .IsRequired();

            entity.Property(d => d.Status)
                .HasColumnName("status")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(d => d.FailureReason)
                .HasColumnName("failure_reason");

            entity.Property(d => d.ChunkCount)
                .HasColumnName("chunk_count")
                .HasDefaultValue(0);

            entity.Property(d => d.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()");

            entity.Property(d => d.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("NOW()");

            entity.HasIndex(d => d.Status)
                .HasDatabaseName("idx_documents_status");
        });

        // AuditLog Entity Configuration
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");

            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id)
                .HasColumnName("id");

            entity.Property(a => a.QueryText)
                .HasColumnName("query_text")
                .IsRequired();

            entity.Property(a => a.ResponseCached)
                .HasColumnName("response_cached")
                .HasDefaultValue(false);

            entity.Property(a => a.CacheScore)
                .HasColumnName("cache_score");

            entity.Property(a => a.PromptTokens)
                .HasColumnName("prompt_tokens")
                .HasDefaultValue(0);

            entity.Property(a => a.CompletionTokens)
                .HasColumnName("completion_tokens")
                .HasDefaultValue(0);

            entity.Property(a => a.TotalTokens)
                .HasColumnName("total_tokens")
                .HasDefaultValue(0);

            entity.Property(a => a.EstimatedCostUsd)
                .HasColumnName("estimated_cost_usd")
                .HasPrecision(10, 6)
                .HasDefaultValue(0.000000m);

            entity.Property(a => a.ExecutionDurationMs)
                .HasColumnName("execution_duration_ms")
                .IsRequired();

            entity.Property(a => a.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()");

            entity.HasIndex(a => a.CreatedAt)
                .IsDescending()
                .HasDatabaseName("idx_audit_logs_created_at");
        });
    }
}
