using System.Diagnostics;
using ApiService.HealthChecks;
using ApiService.Metrics;
using ApiService.Middleware;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

// ─── Bootstrap logger (catches startup errors before DI) ───────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting ApiService...");

    var builder = WebApplication.CreateBuilder(args);

    // ─── Serilog ────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithProcessId()
        .Enrich.WithThreadId()
        .Enrich.WithCorrelationId()
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} " +
            "{Properties:j}{NewLine}{Exception}")
        .WriteTo.Seq(context.Configuration["Seq:ServerUrl"] ?? "http://seq:5341")
    );

    // ─── OpenTelemetry ──────────────────────────────────────────────────────
    var serviceName    = "ApiService";
    var serviceVersion = "1.0.0";

    builder.Services.AddSingleton(new ActivitySource(serviceName));

    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .AddSource(serviceName)
            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(serviceName, serviceVersion: serviceVersion))
            .AddAspNetCoreInstrumentation(opt =>
            {
                opt.RecordException = true;
                opt.Filter = ctx => ctx.Request.Path != "/metrics"
                                 && ctx.Request.Path != "/health";
            })
            .AddHttpClientInstrumentation(opt => opt.RecordException = true)
            .AddSqlClientInstrumentation(opt =>
            {
                opt.RecordException = true;
            })
            .AddOtlpExporter(opt =>
            {
                opt.Endpoint = new Uri(builder.Configuration["Jaeger:OtlpEndpoint"] ?? "http://jaeger:4317");
            })
        )
        .WithMetrics(metrics => metrics
            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(serviceName, serviceVersion: serviceVersion))
            .AddMeter("ApiService.Metrics")
            .AddMeter("ApiService.HealthMetrics")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter()
        );

    // ─── Application Metrics ────────────────────────────────────────────────
    builder.Services.AddSingleton<AppMetrics>();

    // ─── Health Checks ──────────────────────────────────────────────────────
    var sqlConnStr      = builder.Configuration.GetConnectionString("SqlServer")
                         ?? "Server=sqlserver;Database=ObservabilityDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True";
    var redisConn       = builder.Configuration["Redis:ConnectionString"] ?? "redis:6379";
    var orderSvcUrl     = builder.Configuration["Services:OrderService"] ?? "http://order-service:8081";
    var inventorySvcUrl = builder.Configuration["Services:InventoryService"] ?? "http://inventory-service:8082";

    // Use master DB for the health check — ObservabilityDb may not exist yet
    var sqlHealthConnStr = sqlConnStr
        .Replace("Database=ObservabilityDb;", "Database=master;")
        .Replace("Database=ObservabilityDb",  "Database=master");

    builder.Services
        .AddHealthChecks()
        .AddCheck<DatabaseMigrationHealthCheck>("database_migrations",
            tags: new[] { "db", "ready" })
        .AddSqlServer(sqlHealthConnStr,
            name: "sql_server",
            tags: new[] { "db", "ready" })
        .AddRedis(redisConn,
            name: "redis",
            tags: new[] { "cache", "ready" })
        .AddUrlGroup(new Uri($"{orderSvcUrl}/health"),
            name: "order_service",
            tags: new[] { "upstream", "ready" })
        .AddUrlGroup(new Uri($"{inventorySvcUrl}/health"),
            name: "inventory_service",
            tags: new[] { "upstream", "ready" })
        .AddCheck<ExternalApiHealthCheck>("payment_gateway",
            tags: new[] { "external", "ready" })
        .AddProcessAllocatedMemoryHealthCheck(512, "memory_512mb",
            tags: new[] { "system" })
        .AddDiskStorageHealthCheck(setup => setup.AddDrive("/", 1024),
            name: "disk_storage",
            tags: new[] { "system" });

    builder.Services
        .AddHealthChecksUI(setup =>
        {
            setup.SetEvaluationTimeInSeconds(15);
            setup.MaximumHistoryEntriesPerEndpoint(50);
            setup.AddHealthCheckEndpoint("ApiService", "http://localhost:8080/health");
        })
        .AddInMemoryStorage();

    // ─── Background service: publishes health state → Prometheus gauge ───────
    builder.Services.AddHostedService<HealthMetricsPublisher>();

    // ─── HTTP Clients (typed, auto-propagates OTel trace context) ───────────
    builder.Services.AddHttpClient("OrderService", client =>
    {
        client.BaseAddress = new Uri(orderSvcUrl + "/");
        client.Timeout     = TimeSpan.FromSeconds(30);
    });
    builder.Services.AddHttpClient("InventoryService", client =>
    {
        client.BaseAddress = new Uri(inventorySvcUrl + "/");
        client.Timeout     = TimeSpan.FromSeconds(30);
    });

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // ─── Build ──────────────────────────────────────────────────────────────
    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // Correlation ID must be FIRST so subsequent enrichers pick it up
    app.UseMiddleware<CorrelationIdMiddleware>();

    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost",   httpContext.Request.Host.Value ?? string.Empty);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("UserAgent",     httpContext.Request.Headers.UserAgent.ToString());
            diagnosticContext.Set("ClientIp",      httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        };
    });

    app.UseAuthorization();
    app.MapControllers();

    // Prometheus scraping endpoint
    app.MapPrometheusScrapingEndpoint();  // → /metrics

    // Health check endpoints
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        Predicate      = _ => true,
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate      = check => !check.Tags.Contains("ready"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate      = check => check.Tags.Contains("ready"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });
    app.MapHealthChecksUI(options =>
    {
        options.UIPath  = "/health-ui";
        options.ApiPath = "/health-ui-api";
    });

    Log.Information("ApiService started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "ApiService terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
