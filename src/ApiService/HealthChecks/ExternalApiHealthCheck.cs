using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ApiService.HealthChecks;

/// <summary>
/// Checks connectivity to an external payment gateway endpoint.
/// Uses a simple HTTP GET with a short timeout. In production you would
/// add Polly-based retry/circuit-breaker before reaching this check.
/// </summary>
public class ExternalApiHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration     _configuration;
    private readonly ILogger<ExternalApiHealthCheck> _logger;

    public ExternalApiHealthCheck(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ExternalApiHealthCheck> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration     = configuration;
        _logger            = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Use a dedicated, short-timeout client
        using var client = _httpClientFactory.CreateClient();
        client.Timeout   = TimeSpan.FromSeconds(5);

        var endpoint = _configuration["ExternalApis:PaymentGateway"]
                       ?? "https://httpstat.us/200"; // safe public stub

        try
        {
            var response = await client.GetAsync(endpoint, cancellationToken);
            if (response.IsSuccessStatusCode)
                return HealthCheckResult.Healthy($"Payment gateway reachable ({(int)response.StatusCode})");

            _logger.LogWarning("Payment gateway returned {StatusCode}", response.StatusCode);
            return HealthCheckResult.Degraded(
                $"Payment gateway returned {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Payment gateway health check failed — marking Degraded");
            return HealthCheckResult.Degraded("Payment gateway unreachable", ex);
        }
    }
}
