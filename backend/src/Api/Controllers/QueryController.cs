using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRAG.Application.Services;
using EnterpriseRAG.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseRAG.Api.Controllers;

public class RagQueryRequest
{
    public string Query { get; set; } = string.Empty;
}

[ApiController]
[Route("api/v1/query")]
public class QueryController : ControllerBase
{
    private readonly IRagQueryService _ragQueryService;
    private readonly AppDbContext? _dbContext;

    public QueryController(IRagQueryService ragQueryService, AppDbContext? dbContext = null)
    {
        _ragQueryService = ragQueryService;
        _dbContext = dbContext;
    }

    [HttpPost]
    public async Task<IActionResult> Query([FromBody] RagQueryRequest? request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new
            {
                success = false,
                error = "Query cannot be empty."
            });
        }

        try
        {
            var result = await _ragQueryService.ExecuteQueryAsync(request.Query, cancellationToken);

            return Ok(new
            {
                success = true,
                data = new
                {
                    answer = result.Answer,
                    citations = result.Citations,
                    isCached = result.IsCached,
                    costUsd = result.CostUsd
                }
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    [HttpGet("/api/v1/audit/metrics")]
    public async Task<IActionResult> GetAuditMetrics(CancellationToken cancellationToken = default)
    {
        if (_dbContext == null)
        {
            return Ok(new
            {
                success = true,
                data = new
                {
                    totalQueries = 0,
                    totalTokens = 0,
                    totalCostUsd = 0.000000m,
                    cacheHitRatio = 0.0
                }
            });
        }

        var totalQueries = await _dbContext.AuditLogs.CountAsync(cancellationToken);
        var totalTokens = await _dbContext.AuditLogs.SumAsync(a => a.TotalTokens, cancellationToken);
        var totalCostUsd = await _dbContext.AuditLogs.SumAsync(a => a.EstimatedCostUsd, cancellationToken);
        var cachedQueries = await _dbContext.AuditLogs.CountAsync(a => a.ResponseCached, cancellationToken);

        var cacheHitRatio = totalQueries > 0 ? Math.Round((double)cachedQueries / totalQueries, 4) : 0.0;

        return Ok(new
        {
            success = true,
            data = new
            {
                totalQueries,
                totalTokens,
                totalCostUsd,
                cacheHitRatio
            }
        });
    }
}
