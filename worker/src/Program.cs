using EnterpriseRAG.Worker;
using EnterpriseRAG.Worker.Chunking;
using EnterpriseRAG.Worker.Data;
using EnterpriseRAG.Worker.Parsers;
using EnterpriseRAG.Worker.Services;
using EnterpriseRAG.Worker.VectorStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// 1. PostgreSQL Database Context
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<WorkerDbContext>(options =>
        options.UseNpgsql(connectionString));
}
else
{
    builder.Services.AddDbContext<WorkerDbContext>(options =>
        options.UseInMemoryDatabase("EnterpriseRAG_Worker_Dev"));
}

// 2. Storage & AI Services
builder.Services.AddSingleton<IBlobStorageReader, AzureBlobStorageReader>();
builder.Services.AddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();
builder.Services.AddSingleton<ITokenTextChunker, TokenTextChunker>();
builder.Services.AddSingleton<IQdrantService, QdrantGrpcService>();
builder.Services.AddSingleton<IQdrantIndexer, QdrantIndexer>();
builder.Services.AddSingleton<IWorkerNotifier, SignalRWorkerNotifier>();
builder.Services.AddScoped<IDocumentIngestionProcessor, DocumentIngestionProcessor>();

// 3. Queue Background Worker
builder.Services.AddHostedService<IngestionWorker>();

var host = builder.Build();
host.Run();
