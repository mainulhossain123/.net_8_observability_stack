using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ApiService.HealthChecks;

/// <summary>
/// Background service that polls the registered health checks every 30 seconds
/// and publishes the results as a Prometheus gauge:
///   health_check_status{check_name="..."} =  1 (Healthy)
///                                          =  0 (Degraded)
///                                          = -1 (Unhealthy)
///
/// This enables Grafana to track dependency health over time and fire alerts
/// before users notice a failure.
/// </summary>
public class HealthMetricsPublisher : BackgroundService
{
    private readonly HealthCheckService _healthCheckService;
    private readonly ILogger<HealthMetricsPublisher> _logger;

    private static readonly Meter   _meter        = new("ApiService.HealthMetrics", "1.0.0");
    private static readonly Gauge<int> _statusGauge = _meter.CreateGauge<int>(
        "health_check_status",
        description: "Health check status: 1=Healthy, 0=Degraded, -1=Unhealthy");

    public HealthMetricsPublisher(
        HealthCheckService healthCheckService,
        ILogger<HealthMetricsPublisher> logger)
    {
        _healthCheckService = healthCheckService;
        _logger             = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var report = await _healthCheckService.CheckHealthAsync(stoppingToken);

                foreach (var (name, entry) in report.Entries)
                {
                    var status = entry.Status switch
                    {
                        HealthStatus.Healthy  =>  1,
                        HealthStatus.Degraded =>  0,
                        _                     => -1
                    };

                    _statusGauge.Record(status, new KeyValuePair<string, object?>("check_name", name));

                    if (entry.Status != HealthStatus.Healthy)
                        _logger.LogWarning(
                            "Health check {CheckName} is {Status}: {Description}",
                            name, entry.Status, entry.Description);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running health metrics publisher");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
