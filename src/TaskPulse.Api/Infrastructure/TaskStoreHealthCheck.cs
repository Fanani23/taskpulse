using TaskPulse.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TaskPulse.Api.Infrastructure;

public sealed class TaskStoreHealthCheck(TasksDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!await db.Database.CanConnectAsync(cancellationToken))
        {
            return HealthCheckResult.Unhealthy("cannot connect to PostgreSQL");
        }

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
        if (pending > 0)
        {
            return HealthCheckResult.Degraded($"{pending} pending migration(s)");
        }

        var count = await db.Tasks.CountAsync(cancellationToken);
        return HealthCheckResult.Healthy($"postgresql reachable ({count} tasks)");
    }
}
