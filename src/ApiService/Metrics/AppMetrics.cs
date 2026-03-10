using System.Diagnostics.Metrics;

namespace ApiService.Metrics;

/// <summary>
/// Centralised application-level metrics using System.Diagnostics.Metrics.
/// Registered as a singleton and injected into controllers / services.
/// All instruments are automatically picked up by the OTel MeterProvider.
/// </summary>
public sealed class AppMetrics : IDisposable
{
    private readonly Meter _meter;

    // ── Counters ──────────────────────────────────────────────────────────
    public readonly Counter<long> OrdersCreated;
    public readonly Counter<long> OrdersFailed;
    public readonly Counter<long> HttpErrors;

    // ── Histograms ────────────────────────────────────────────────────────
    public readonly Histogram<double> OrderProcessingDuration;
    public readonly Histogram<double> ExternalApiCallDuration;

    // ── UpDownCounters ────────────────────────────────────────────────────
    public readonly UpDownCounter<long> ActiveOrders;
    public readonly UpDownCounter<long> CacheHits;
    public readonly UpDownCounter<long> CacheMisses;

    public AppMetrics()
    {
        _meter = new Meter("ApiService.Metrics", "1.0.0");

        OrdersCreated = _meter.CreateCounter<long>(
            "orders_created_total",
            description: "Total number of orders successfully created");

        OrdersFailed = _meter.CreateCounter<long>(
            "orders_failed_total",
            description: "Total number of order creation failures");

        HttpErrors = _meter.CreateCounter<long>(
            "http_errors_total",
            description: "Total number of HTTP 5xx responses returned");

        OrderProcessingDuration = _meter.CreateHistogram<double>(
            "order_processing_duration_ms",
            unit: "ms",
            description: "Time taken to process an order end-to-end");

        ExternalApiCallDuration = _meter.CreateHistogram<double>(
            "external_api_call_duration_ms",
            unit: "ms",
            description: "Duration of calls to external APIs");

        ActiveOrders = _meter.CreateUpDownCounter<long>(
            "active_orders",
            description: "Number of orders currently being processed");

        CacheHits = _meter.CreateUpDownCounter<long>(
            "cache_hits_total",
            description: "Number of cache hits");

        CacheMisses = _meter.CreateUpDownCounter<long>(
            "cache_misses_total",
            description: "Number of cache misses");
    }

    public void Dispose() => _meter.Dispose();
}
