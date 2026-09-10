using EnterpriseRAG.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add DbContext with PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseInMemoryDatabase("EnterpriseRAG_Dev"));
}

builder.Services.AddScoped<EnterpriseRAG.Application.Common.Interfaces.IAzureBlobQueueService, EnterpriseRAG.Infrastructure.Storage.AzureBlobQueueService>();
builder.Services.AddScoped<EnterpriseRAG.Application.Services.IDocumentUploadService, EnterpriseRAG.Application.Services.DocumentUploadService>();
builder.Services.AddSignalR();
builder.Services.AddScoped<EnterpriseRAG.Application.Common.Interfaces.IIngestionNotifier, EnterpriseRAG.Infrastructure.Notifications.SignalRIngestionNotifier>();

// Redis & Rate Limiting
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
{
    var options = StackExchange.Redis.ConfigurationOptions.Parse(redisConnection);
    options.AbortOnConnectFail = false;
    return StackExchange.Redis.ConnectionMultiplexer.Connect(options);
});
builder.Services.AddSingleton<EnterpriseRAG.Infrastructure.Cache.IRedisRateLimiter, EnterpriseRAG.Infrastructure.Cache.RedisRateLimiter>();

// Semantic Kernel & RAG Retrieval
builder.Services.AddSingleton<EnterpriseRAG.Infrastructure.AI.ISemanticKernelOrchestrator, EnterpriseRAG.Infrastructure.AI.SemanticKernelOrchestrator>();
builder.Services.AddSingleton<EnterpriseRAG.Infrastructure.AI.IQdrantRetrievalService, EnterpriseRAG.Infrastructure.AI.QdrantRetrievalService>();
builder.Services.AddScoped<EnterpriseRAG.Application.Services.IRagQueryService, EnterpriseRAG.Application.Services.RagQueryService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseRouting();
app.UseMiddleware<EnterpriseRAG.Api.Middleware.RedisRateLimitingMiddleware>();

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

app.MapControllers();
app.MapHub<EnterpriseRAG.Api.Hubs.IngestionHub>("/hubs/ingestion");

app.Run();
