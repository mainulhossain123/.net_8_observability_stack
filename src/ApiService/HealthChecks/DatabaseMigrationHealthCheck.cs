using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ApiService.HealthChecks;

/// <summary>
/// Simulates a check that verifies all EF Core migrations have been applied.
/// In a real project this would query __EFMigrationsHistory.
/// </summary>
public class DatabaseMigrationHealthCheck : IHealthCheck
{
    private readonly ILogger<DatabaseMigrationHealthCheck> _logger;

    public DatabaseMigrationHealthCheck(ILogger<DatabaseMigrationHealthCheck> logger)
        => _logger = logger;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // In a real app: query DbContext.Database.GetPendingMigrationsAsync()
        // and return Degraded/Unhealthy when pending migrations exist.
        _logger.LogDebug("Checking database migrations...");

        var data = new Dictionary<string, object>
        {
            ["applied_migrations"] = 5,
            ["pending_migrations"] = 0,
            ["check_time_utc"]     = DateTime.UtcNow
        };

        return Task.FromResult(HealthCheckResult.Healthy(
            "All migrations applied", data));
    }
}
