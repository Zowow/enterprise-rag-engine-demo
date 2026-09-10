using System;
using System.Threading.Tasks;
using EnterpriseRAG.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace EnterpriseRAG.Api.Hubs;

public interface IIngestionClient
{
    Task IngestionProgress(IngestionProgressUpdate update);
}

public class IngestionHub : Hub<IIngestionClient>
{
    public async Task JoinJobGroup(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID cannot be null or empty.", nameof(jobId));
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, jobId);
    }

    public async Task LeaveJobGroup(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID cannot be null or empty.", nameof(jobId));
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, jobId);
    }
}
