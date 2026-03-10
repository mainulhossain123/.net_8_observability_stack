namespace ApiService.Middleware;

/// <summary>
/// Ensures every request carries a X-Correlation-ID header.
/// Generates a new GUID if none is supplied. Stores the ID in both
/// HttpContext.Items and Serilog LogContext so every downstream log entry
/// is enriched with the same correlation ID.
/// </summary>
public class CorrelationIdMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // Read incoming header or generate a fresh ID
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()
                            ?? Guid.NewGuid().ToString();

        // Make it available to downstream code
        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        // Push into Serilog's LogContext so every log statement in this
        // request scope includes CorrelationId automatically
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
