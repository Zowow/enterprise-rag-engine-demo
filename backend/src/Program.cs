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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseRouting();

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

app.MapControllers();

app.Run();
