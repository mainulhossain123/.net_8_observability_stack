using System.Diagnostics;
using ApiService.Metrics;
using Microsoft.AspNetCore.Mvc;

namespace ApiService.Controllers;

/// <summary>
/// Orchestrates order creation, calling OrderService and InventoryService
/// to demonstrate multi-service trace propagation (Milestone 2).
/// </summary>
[ApiController]
[Route("[controller]")]
public class OrdersController : ControllerBase
{
    private readonly ILogger<OrdersController>  _logger;
    private readonly ActivitySource             _activitySource;
    private readonly AppMetrics                 _metrics;
    private readonly IHttpClientFactory         _httpClientFactory;

    public OrdersController(
        ILogger<OrdersController> logger,
        ActivitySource activitySource,
        AppMetrics metrics,
        IHttpClientFactory httpClientFactory)
    {
        _logger            = logger;
        _activitySource    = activitySource;
        _metrics           = metrics;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public IActionResult GetOrders()
    {
        _logger.LogInformation("Fetching orders list");
        return Ok(new[]
        {
            new { Id = 1, Product = "Widget A", Status = "Completed", Total = 29.99m },
            new { Id = 2, Product = "Gadget B", Status = "Pending",   Total = 49.99m },
            new { Id = 3, Product = "Doohickey", Status = "Processing", Total = 99.99m }
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        using var activity = _activitySource.StartActivity("ProcessOrder");
        activity?.SetTag("order.product", request.Product);
        activity?.SetTag("order.quantity", request.Quantity);

        var sw = Stopwatch.StartNew();
        _metrics.ActiveOrders.Add(1);

        try
        {
            _logger.LogInformation(
                "Creating order for {Product} x{Quantity}",
                request.Product, request.Quantity);

            // Step 1 — Check inventory (calls InventoryService, propagates trace)
            using (var inventorySpan = _activitySource.StartActivity("CheckInventory"))
            {
                var inventoryClient = _httpClientFactory.CreateClient("InventoryService");
                var inventoryResp   = await inventoryClient.GetAsync(
                    $"inventory/check?product={request.Product}&qty={request.Quantity}");

                if (!inventoryResp.IsSuccessStatusCode)
                {
                    inventorySpan?.SetStatus(ActivityStatusCode.Error, "Inventory check failed");
                    inventorySpan?.SetTag("error", true);
                    _logger.LogWarning(
                        "Inventory check failed for {Product}: {Status}",
                        request.Product, inventoryResp.StatusCode);
                    _metrics.OrdersFailed.Add(1, new KeyValuePair<string, object?>("reason", "inventory"));
                    return StatusCode(503, new { error = "Inventory service unavailable" });
                }
            }

            // Step 2 — Submit to OrderService (propagates trace)
            using (var orderSpan = _activitySource.StartActivity("SubmitToOrderService"))
            {
                var orderClient = _httpClientFactory.CreateClient("OrderService");
                var orderResp   = await orderClient.PostAsJsonAsync("orders/process", request);

                if (!orderResp.IsSuccessStatusCode)
                {
                    orderSpan?.SetStatus(ActivityStatusCode.Error, "Order submission failed");
                    orderSpan?.SetTag("error", true);
                    _metrics.OrdersFailed.Add(1, new KeyValuePair<string, object?>("reason", "order_service"));
                    return StatusCode(503, new { error = "Order service unavailable" });
                }
            }

            sw.Stop();
            _metrics.OrdersCreated.Add(1);
            _metrics.OrderProcessingDuration.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("product", request.Product));

            activity?.SetTag("order.status", "created");

            _logger.LogInformation(
                "Order created successfully for {Product} in {ElapsedMs}ms",
                request.Product, sw.ElapsedMilliseconds);

            return CreatedAtAction(nameof(GetOrders), new { id = Guid.NewGuid() },
                new { message = "Order created", product = request.Product });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
            _metrics.OrdersFailed.Add(1, new KeyValuePair<string, object?>("reason", "exception"));
            _logger.LogError(ex, "Failed to create order for {Product}", request.Product);
            return StatusCode(500, new { error = "Internal server error" });
        }
        finally
        {
            _metrics.ActiveOrders.Add(-1);
        }
    }
}

public record CreateOrderRequest(string Product, int Quantity, decimal UnitPrice);
