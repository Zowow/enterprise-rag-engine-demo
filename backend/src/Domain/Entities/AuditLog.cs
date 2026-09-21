using System;

namespace EnterpriseRAG.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string QueryText { get; set; } = string.Empty;
    public bool ResponseCached { get; set; } = false;
    public double? CacheScore { get; set; }
    public int PromptTokens { get; set; } = 0;
    public int CompletionTokens { get; set; } = 0;
    public int TotalTokens { get; set; } = 0;
    public decimal EstimatedCostUsd { get; set; } = 0.000000m;
    public int ExecutionDurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
