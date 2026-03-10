using System.Diagnostics;
using ApiService.Metrics;
using Microsoft.AspNetCore.Mvc;

namespace ApiService.Controllers;

/// <summary>
/// Demo endpoints that simulate slow operations and errors.
/// Used in the end-to-end demo scenario to trigger:
///   - Slow spans visible in Jaeger
///   - Error rate spikes visible in Grafana
///   - Correlated error logs in Seq
/// </summary>
[ApiController]
[Route("simulate")]
public class DemoController : ControllerBase
{
    private readonly ILogger<DemoController>  _logger;
    private readonly ActivitySource           _activitySource;
    private readonly AppMetrics               _metrics;

    public DemoController(
        ILogger<DemoController> logger,
        ActivitySource activitySource,
        AppMetrics metrics)
    {
        _logger         = logger;
        _activitySource = activitySource;
        _metrics        = metrics;
    }

    /// <summary>
    /// Simulates a slow database query. Visible as a slow span in Jaeger.
    /// </summary>
    [HttpGet("slow")]
    public async Task<IActionResult> SimulateSlow([FromQuery] int delayMs = 2000)
    {
        using var activity = _activitySource.StartActivity("SlowDatabaseQuery");
        activity?.SetTag("db.system",    "sqlserver");
        activity?.SetTag("db.name",      "ObservabilityDb");
        activity?.SetTag("db.statement", "SELECT * FROM Orders WHERE CreatedAt > @cutoff");
        activity?.SetTag("db.operation", "SELECT");

        _logger.LogWarning(
            "Simulating slow database query with {DelayMs}ms delay", delayMs);

        await Task.Delay(delayMs);

        activity?.SetTag("db.rows_returned", 1500);
        activity?.SetTag("db.execution_ms",  delayMs);

        _logger.LogInformation("Slow query completed in {DelayMs}ms", delayMs);

        return Ok(new
        {
            message    = "Slow query completed",
            delayMs,
            rowsScanned = 1500
        });
    }

    /// <summary>
    /// Simulates an application error. Creates a red span in Jaeger,
    /// logs at Error level in Seq, and increments error counters in Prometheus.
    /// </summary>
    [HttpGet("error")]
    public IActionResult SimulateError()
    {
        using var activity = _activitySource.StartActivity("FailingOperation");
        activity?.SetTag("operation.type", "database_write");

        try
        {
            _logger.LogWarning("Simulating a downstream failure for demo...");

            // Deliberately throw to produce a real stack trace in Jaeger
            throw new InvalidOperationException(
                "Simulated failure: downstream payment service returned 503");
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
            activity?.SetTag("exception.stacktrace", ex.StackTrace);

            _metrics.HttpErrors.Add(1,
                new KeyValuePair<string, object?>("endpoint", "/simulate/error"),
                new KeyValuePair<string, object?>("error_type", ex.GetType().Name));

            _logger.LogError(ex,
                "Simulated operation failed. CorrelationId: {CorrelationId}",
                HttpContext.Items["CorrelationId"]);

            return StatusCode(500, new
            {
                error      = "Simulated failure",
                message    = ex.Message,
                traceId    = activity?.TraceId.ToString()
            });
        }
    }

    /// <summary>
    /// Generates baseline load for metric visualisation.
    /// </summary>
    [HttpGet("load")]
    public IActionResult GenerateLoad([FromQuery] int requests = 10)
    {
        requests = Math.Clamp(requests, 1, 100); // cap to prevent abuse
        _logger.LogInformation("Load simulation: {Requests} requests", requests);

        for (var i = 0; i < requests; i++)
        {
            _metrics.OrdersCreated.Add(1,
                new KeyValuePair<string, object?>("product", $"Widget-{i % 5}"));
        }

        return Ok(new { message = $"Simulated {requests} order events" });
    }
}
