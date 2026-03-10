using System.Diagnostics;
using OrderService.Middleware;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting OrderService...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithProcessId()
        .Enrich.WithThreadId()
        .Enrich.WithCorrelationId()
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] [OrderService] {Message:lj} " +
            "{Properties:j}{NewLine}{Exception}")
        .WriteTo.Seq(context.Configuration["Seq:ServerUrl"] ?? "http://seq:5341")
    );

    var serviceName = "OrderService";
    builder.Services.AddSingleton(new ActivitySource(serviceName));

    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .AddSource(serviceName)
            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(serviceName, serviceVersion: "1.0.0"))
            .AddAspNetCoreInstrumentation(opt => opt.RecordException = true)
            .AddHttpClientInstrumentation(opt => opt.RecordException = true)
            .AddSqlClientInstrumentation(opt =>
            {
                opt.RecordException       = true;
            })
            .AddOtlpExporter(opt =>
            {
                opt.Endpoint = new Uri(builder.Configuration["Jaeger:OtlpEndpoint"] ?? "http://jaeger:4317");
            })
        )
        .WithMetrics(metrics => metrics
            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(serviceName, serviceVersion: "1.0.0"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter()
        );

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddHealthChecks();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseAuthorization();
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
    app.MapControllers();
    app.MapPrometheusScrapingEndpoint();
    app.MapHealthChecks("/health");

    Log.Information("OrderService started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "OrderService terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

